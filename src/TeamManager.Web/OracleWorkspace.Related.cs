using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<List<IssueLink>> Links(int user, int id, int project) => Read(async w =>
    {
        var current = (await RequireIssue(w, user, id));
        if (Int(current, "ProjectId") != project)
            throw new UnauthorizedAccessException();
        var result = new List<IssueLink>();
        foreach (var link in await w.Where("IssueLinks", "t.\"SourceId\"=:id OR t.\"TargetId\"=:id", ("id", id)))
        {
            var other = await w.Find("Issues", Int(link, "SourceId") == id ? Int(link, "TargetId") : Int(link, "SourceId"));
            if (other is null || !await Visible(w, user, other)) continue;
            result.Add(new IssueLink(Id(other), Text(other, "Title"), Text(link, "Kind"), Int(link, "SourceId") == id, await Can(w, user, Int(other, "ProjectId"), "issues.edit")));
        }
        return result;
    });
    internal Task SetLink(int user, int id, int other, string kind, bool outgoing, bool remove) => WriteLink(user, id, other, kind, async w =>
    {
        if (id == other || kind is not ("related" or "duplicate" or "blocks" or "parent"))
            throw new ArgumentException("Odaberi drugi zadatak i valjanu vrstu veze.");
        await RequireIssue(w, user, id, "issues.edit");
        await RequireIssue(w, user, other, "issues.edit");
        var source = outgoing ? id : other;
        var target = outgoing ? other : id;
        if (kind is "related" or "duplicate")
        {
            source = Math.Min(id, other);
            target = Math.Max(id, other);
        }

        bool Match(JsonObject l) => Int(l, "SourceId") == source && Int(l, "TargetId") == target && Text(l, "Kind") == kind;
        if (remove)
            await w.RemoveBy("IssueLinks", "SourceId", source, Match);
        else if (!(await w.By("IssueLinks", "SourceId", source)).Any(Match))
        {
            if (kind == "parent" && (await w.By("IssueLinks", "TargetId", target)).Any(l => Text(l, "Kind") == "parent"))
                throw new ArgumentException("Podzadatak već ima nadređeni zadatak.");
            if (kind is "blocks" or "parent")
            {
                var pending = new Stack<int>();
                pending.Push(target);
                var seen = new HashSet<int>();
                while (pending.TryPop(out var node))
                {
                    if (node == source)
                        throw new ArgumentException("Ova bi veza stvorila ciklus.");
                    if (!seen.Add(node))
                        continue;
                    foreach (var link in (await w.By("IssueLinks", "SourceId", node)).Where(l => Text(l, "Kind") == kind))
                        pending.Push(Int(link, "TargetId"));
                }
            }

            w.Add("IssueLinks", ("SourceId", source), ("TargetId", target), ("Kind", kind));
        }

        return true;
    });
    internal Task<List<PrivateIssueNote>> Notes(int user, int issue, int project) => Read(async w =>
    {
        var row = (await RequireIssue(w, user, issue));
        if (Int(row, "ProjectId") != project)
            throw new UnauthorizedAccessException();
        return (await w.By("IssuePrivateNotes", "IssueId", issue)).Where(n => Int(n, "UserId") == user).OrderByDescending(Id).Select(n => new PrivateIssueNote(Text(n, "Body"), Date(n, "CreatedAt")!.Value)).ToList();
    });
    internal Task AddNote(int user, int issue, string? text) => WriteIssues(user, [issue], async w =>
    {
        var row = (await RequireIssue(w, user, issue));
        var p = (await w.Find("Projects", Int(row, "ProjectId")))!;
        if (Text(p, "State") != "Active")
            throw new UnauthorizedAccessException();
        text = text?.Trim();
        if (text is null or { Length: 0 or > 4000 })
            throw new ArgumentException("Bilješka mora imati od 1 do 4000 znakova.");
        w.Add("IssuePrivateNotes", ("Id", (await w.Next("IssuePrivateNotes"))), ("IssueId", issue), ("UserId", user), ("Body", text), ("CreatedAt", DateTime.UtcNow));
        return true;
    });
    internal Task<List<AttachmentInfo>> Attachments(int user, int issue, int project) => AttachmentsSql(user, issue, project);
    internal Task<AttachmentDownload?> Download(int user, int issue, int attachment) => Read(async w =>
    {
        var visible = (await w.Find("Issues", issue));
        if (visible is null || !(await Visible(w, user, visible)))
            return null;
        var row = (await w.Find("IssueAttachments", attachment));
        return row is not null && Int(row, "IssueId") == issue ? new AttachmentDownload(Text(row, "FileName"), Convert.FromBase64String(Text(row, "Content"))) : null;
    });
    internal async Task Upload(int user, int issue, IFormFile? file, CancellationToken token)
    {
        var upload = await UploadedFile.Read(file, token);
        await WriteIssues(user, [issue], async w =>
        {
            await RequireIssue(w, user, issue, "issues.edit");
            w.Add("IssueAttachments", ("Id", (await w.Next("IssueAttachments"))), ("IssueId", issue), ("AuthorId", user), ("FileName", upload.Name), ("Content", Convert.ToBase64String(upload.Content)), ("CreatedAt", DateTime.UtcNow));
            return true;
        });
    }

    internal Task<bool> RemoveAttachment(int user, int issue, int attachment) => WriteIssues(user, [issue], async w =>
    {
        await RequireIssue(w, user, issue, "issues.edit");
        var row = (await w.Find("IssueAttachments", attachment));
        if (row is null || Int(row, "IssueId") != issue)
            return false;
        w.Table("IssueAttachments").Remove(row);
        return true;
    });
    internal Task<List<IssueField>> IssueFields(int? user, int project, int? issue, bool managers) => Read(async w =>
    {
        if (issue is int selected)
        {
            if (user is not int actor)
                throw new UnauthorizedAccessException();
            var row = (await RequireIssue(w, actor, selected));
            if (Int(row, "ProjectId") != project)
                throw new UnauthorizedAccessException();
        }

        var values = await w.By("IssueFieldValues", "IssueId", issue);
        return (await w.By("ProjectIssueFields", "ProjectId", project)).Where(f => !Flag(f, "ManagersOnly") || managers).OrderBy(f => Text(f, "Name")).Select(f => new IssueField(Id(f), Text(f, "Name"), values.Where(v => Int(v, "FieldId") == Id(f)).Select(v => Text(v, "Value")).FirstOrDefault(), Flag(f, "ManagersOnly"))).ToList();
    });
    internal Task SetIssueField(int user, int issue, int field, int revision, string? value) => WriteIssues(user, [issue], async w =>
    {
        var row = (await RequireIssue(w, user, issue, "issues.edit"));
        var project = Int(row, "ProjectId");
        var f = (await w.Find("ProjectIssueFields", field));
        if (f is null || Int(f, "ProjectId") != project || Flag(f, "ManagersOnly") && !(await Manage(w, user, project)))
            throw new UnauthorizedAccessException();
        value = value?.Trim();
        if (value?.Length > 500)
            throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");
        Revision(row, revision, "Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");
        await w.RemoveBy("IssueFieldValues", "IssueId", issue, v => Int(v, "FieldId") == field);
        if (!string.IsNullOrEmpty(value))
            w.Add("IssueFieldValues", ("IssueId", issue), ("FieldId", field), ("Value", value));
        Revise(row);
        return true;
    });
    internal Task<List<RoadmapItem>> Roadmap(int project) => Read(async w => (await w.By("ProjectMilestones", "ProjectId", project)).OrderBy(m => Flag(m, "IsComplete")).ThenBy(m => Date(m, "TargetDate") ?? DateTime.MaxValue).Select(m => new RoadmapItem(Id(m), Text(m, "Kind"), Text(m, "Name"), Text(m, "Description"), Date(m, "TargetDate"), Flag(m, "IsComplete"), Int(m, "Revision"))).ToList());
}

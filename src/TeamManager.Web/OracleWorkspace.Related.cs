using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<List<IssueLink>> Links(int user, int id, int project) => Read(w =>
    {
        var current = RequireIssue(w, user, id);
        if (Int(current, "ProjectId") != project)
            throw new UnauthorizedAccessException();
        return w.Rows("IssueLinks").Where(l => Int(l, "SourceId") == id || Int(l, "TargetId") == id).Select(l => (Link: l, Other: w.Find("Issues", Int(l, "SourceId") == id ? Int(l, "TargetId") : Int(l, "SourceId")))).Where(x => x.Other is not null && Visible(w, user, x.Other)).Select(x => new IssueLink(Id(x.Other!), Text(x.Other!, "Title"), Text(x.Link, "Kind"), Int(x.Link, "SourceId") == id, Can(w, user, Int(x.Other!, "ProjectId"), "issues.edit"))).ToList();
    });
    internal Task SetLink(int user, int id, int other, string kind, bool outgoing, bool remove) => Write(w =>
    {
        if (id == other || kind is not ("related" or "duplicate" or "blocks" or "parent"))
            throw new ArgumentException("Odaberi drugi zadatak i valjanu vrstu veze.");
        RequireIssue(w, user, id, "issues.edit");
        RequireIssue(w, user, other, "issues.edit");
        var source = outgoing ? id : other;
        var target = outgoing ? other : id;
        if (kind is "related" or "duplicate")
        {
            source = Math.Min(id, other);
            target = Math.Max(id, other);
        }

        bool Match(JsonObject l) => Int(l, "SourceId") == source && Int(l, "TargetId") == target && Text(l, "Kind") == kind;
        if (remove)
            w.Remove("IssueLinks", Match);
        else if (!w.Rows("IssueLinks").Any(Match))
        {
            if (kind == "parent" && w.Rows("IssueLinks").Any(l => Int(l, "TargetId") == target && Text(l, "Kind") == "parent"))
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
                    foreach (var link in w.Rows("IssueLinks").Where(l => Int(l, "SourceId") == node && Text(l, "Kind") == kind))
                        pending.Push(Int(link, "TargetId"));
                }
            }

            w.Add("IssueLinks", ("SourceId", source), ("TargetId", target), ("Kind", kind));
        }

        return true;
    });
    internal Task<List<PrivateIssueNote>> Notes(int user, int issue, int project) => Read(w =>
    {
        var row = RequireIssue(w, user, issue);
        if (Int(row, "ProjectId") != project)
            throw new UnauthorizedAccessException();
        return w.Rows("IssuePrivateNotes").Where(n => Int(n, "UserId") == user && Int(n, "IssueId") == issue).OrderByDescending(Id).Select(n => new PrivateIssueNote(Text(n, "Body"), Date(n, "CreatedAt")!.Value)).ToList();
    });
    internal Task AddNote(int user, int issue, string? text) => Write(w =>
    {
        var row = RequireIssue(w, user, issue);
        var p = w.Find("Projects", Int(row, "ProjectId"))!;
        if (Text(p, "State") != "Active")
            throw new UnauthorizedAccessException();
        text = text?.Trim();
        if (text is null or { Length: 0 or > 4000 })
            throw new ArgumentException("Bilješka mora imati od 1 do 4000 znakova.");
        w.Add("IssuePrivateNotes", ("Id", w.Next("IssuePrivateNotes")), ("IssueId", issue), ("UserId", user), ("Body", text), ("CreatedAt", DateTime.UtcNow));
        return true;
    });
    internal Task<List<AttachmentInfo>> Attachments(int user, int issue, int project) => Read(w =>
    {
        var row = RequireIssue(w, user, issue);
        if (Int(row, "ProjectId") != project)
            throw new UnauthorizedAccessException();
        return w.Rows("IssueAttachments").Where(a => Int(a, "IssueId") == issue).OrderByDescending(Id).Select(a => new AttachmentInfo(Id(a), Text(a, "FileName"), Convert.FromBase64String(Text(a, "Content")).LongLength)).ToList();
    });
    internal Task<AttachmentDownload?> Download(int user, int issue, int attachment) => Read(w =>
    {
        var visible = w.Find("Issues", issue);
        if (visible is null || !Visible(w, user, visible))
            return null;
        var row = w.Find("IssueAttachments", attachment);
        return row is not null && Int(row, "IssueId") == issue ? new AttachmentDownload(Text(row, "FileName"), Convert.FromBase64String(Text(row, "Content"))) : null;
    });
    internal async Task Upload(int user, int issue, IFormFile? file, CancellationToken token)
    {
        var upload = await UploadedFile.Read(file, token);
        await Write(w =>
        {
            RequireIssue(w, user, issue, "issues.edit");
            w.Add("IssueAttachments", ("Id", w.Next("IssueAttachments")), ("IssueId", issue), ("AuthorId", user), ("FileName", upload.Name), ("Content", Convert.ToBase64String(upload.Content)), ("CreatedAt", DateTime.UtcNow));
            return true;
        });
    }

    internal Task<bool> RemoveAttachment(int user, int issue, int attachment) => Write(w =>
    {
        RequireIssue(w, user, issue, "issues.edit");
        var row = w.Find("IssueAttachments", attachment);
        if (row is null || Int(row, "IssueId") != issue)
            return false;
        w.Table("IssueAttachments").Remove(row);
        return true;
    });
    internal Task<List<IssueField>> IssueFields(int? user, int project, int? issue, bool managers) => Read(w =>
    {
        if (issue is int selected)
        {
            if (user is not int actor)
                throw new UnauthorizedAccessException();
            var row = RequireIssue(w, actor, selected);
            if (Int(row, "ProjectId") != project)
                throw new UnauthorizedAccessException();
        }

        return w.Rows("ProjectIssueFields").Where(f => Int(f, "ProjectId") == project && (!Flag(f, "ManagersOnly") || managers)).OrderBy(f => Text(f, "Name")).Select(f => new IssueField(Id(f), Text(f, "Name"), w.Rows("IssueFieldValues").Where(v => Int(v, "IssueId") == issue && Int(v, "FieldId") == Id(f)).Select(v => Text(v, "Value")).FirstOrDefault(), Flag(f, "ManagersOnly"))).ToList();
    });
    internal Task SetIssueField(int user, int issue, int field, int revision, string? value) => Write(w =>
    {
        var row = RequireIssue(w, user, issue, "issues.edit");
        var project = Int(row, "ProjectId");
        var f = w.Find("ProjectIssueFields", field);
        if (f is null || Int(f, "ProjectId") != project || Flag(f, "ManagersOnly") && !Manage(w, user, project))
            throw new UnauthorizedAccessException();
        value = value?.Trim();
        if (value?.Length > 500)
            throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");
        Revision(row, revision, "Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");
        w.Remove("IssueFieldValues", v => Int(v, "IssueId") == issue && Int(v, "FieldId") == field);
        if (!string.IsNullOrEmpty(value))
            w.Add("IssueFieldValues", ("IssueId", issue), ("FieldId", field), ("Value", value));
        Revise(row);
        return true;
    });
    internal Task<List<RoadmapItem>> Roadmap(int project) => Read(w => w.Rows("ProjectMilestones").Where(m => Int(m, "ProjectId") == project).OrderBy(m => Flag(m, "IsComplete")).ThenBy(m => Date(m, "TargetDate") ?? DateTime.MaxValue).Select(m => new RoadmapItem(Id(m), Text(m, "Kind"), Text(m, "Name"), Text(m, "Description"), Date(m, "TargetDate"), Flag(m, "IsComplete"), Int(m, "Revision"))).ToList());
}

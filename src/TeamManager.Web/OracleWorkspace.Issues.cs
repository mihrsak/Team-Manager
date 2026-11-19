using System.Text.Json;
using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal static IssueDetail Detail(JsonObject r) => new(Id(r), Int(r, "ProjectId"), new() { Title = Text(r, "Title"), Description = Text(r, "Description"), Type = Text(r, "Type"), Priority = Text(r, "Priority"), Status = Text(r, "Status"), AssigneeId = OptionalInt(r, "AssigneeId"), DueDate = Date(r, "DueDate"), StartDate = Date(r, "StartDate"), RoadmapItemId = OptionalInt(r, "RoadmapItemId"), Revision = Int(r, "Revision"), IsPrivate = Flag(r, "IsPrivate") }, Int(r, "CreatedBy"));
    internal Task<IssueDetail?> Issue(int user, int id) => IssueSql(user,id);
    internal Task<List<IssueRow>> IssueList(int user,int? project,string? status,string? query,DateTime? from,DateTime? to,bool schedule,int? assignee,bool open,bool watched) => IssueListSql(user,project,status,query,from,to,schedule,assignee,open,watched);
    internal static int Event(Workspace w, int issue, int user, string kind, string body)
    {
        var id = w.Next("IssueEvents");
        w.Add("IssueEvents", ("Id", id), ("IssueId", issue), ("UserId", user), ("Kind", kind), ("Body", body), ("CreatedAt", DateTime.UtcNow));
        foreach (var watcher in w.By("IssueWatchers", "IssueId", issue).Where(r => Int(r, "UserId") != user).ToArray())
        {
            var recipient = Int(watcher, "UserId");
            w.Add("IssueNotifications", ("EventId", id), ("IssueId", issue), ("UserId", recipient), ("IsRead", false));
            if (w.By("EmailNotificationPreferences", "UserId", recipient).Any(p => Flag(p, "SendWatchedIssues")) && w.Find("Issues", issue)is { } row && Visible(w, recipient, row))
                w.Add("IssueEmailQueue", ("UserId", recipient), ("EventId", id), ("QueuedAt", DateTime.UtcNow), ("Attempts", 0), ("AvailableAt", DateTime.UtcNow), ("LeaseId", null), ("CompletedAt", null));
        }

        return id;
    }

    internal static int SaveIssue(Workspace w, int user, int project, int? id, IssueInput input)
    {
        if (!Can(w, user, project, id is null ? "issues.create" : "issues.edit") || !Can(w, user, project, "issues.view"))
            throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 200 || (input.Description?.Length ?? 0) > 4000 || !w.Rows("IssueTypes").Any(r => Text(r, "Name") == input.Type) || !w.Rows("IssueStatuses").Any(r => Text(r, "Name") == input.Status) || !w.Rows("IssuePriorities").Any(r => Text(r, "Name") == input.Priority) || input.DueDate?.Year < 1753)
            throw new ArgumentException("Provjeri polja zadatka.");
        if (input.StartDate?.Year < 1753 || input.StartDate?.Date > input.DueDate?.Date)
            throw new ArgumentException("Početak mora biti valjan datum i ne može biti poslije roka.");
        if (input.AssigneeId is int assignee && !w.By("ProjectMembers", "ProjectId", project).Any(r => Int(r, "UserId") == assignee))
            throw new ArgumentException("Nositelj mora biti član projekta.");
        if (input.RoadmapItemId is int milestone && (w.Find("ProjectMilestones", milestone)is not { } m || Int(m, "ProjectId") != project))
            throw new ArgumentException("Odaberi verziju ili prekretnicu ovog projekta.");
        JsonObject? row = null;
        if (id is int selected)
        {
            row = RequireIssue(w, user, selected, "issues.edit");
            if (Int(row, "ProjectId") != project)
                throw new UnauthorizedAccessException();
            Revision(row, input.Revision, "Zadatak je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
            if (input.IsPrivate is not null && input.IsPrivate != Flag(row, "IsPrivate") && Int(row, "CreatedBy") != user && !Manage(w, user, project))
                throw new UnauthorizedAccessException();
            if (Text(row, "Status") != input.Status && !Rights(w, user).Contains("*"))
            {
                var roles = RolesFor(w, user, "project", project);
                if (!w.Rows("WorkflowTransitions").Any(t => roles.Contains(Text(t, "RoleName")) && Text(t, "Type") == Text(row, "Type") && Text(t, "FromStatus") == Text(row, "Status") && Text(t, "ToStatus") == input.Status))
                    throw new ArgumentException("Tvoja rola ne dopušta taj prijelaz statusa za ovaj tip zadatka.");
            }
        }
        else if (input.Status != "Novo")
            throw new ArgumentException("Novi zadatak mora početi statusom Novo.");
        var before = row?.ToJsonString();
        row ??= w.Add("Issues", ("Id", w.Next("Issues")), ("ProjectId", project), ("CreatedBy", user), ("CreatedAt", DateTime.UtcNow), ("Revision", 0), ("IsPrivate", false));
        Set(row, "Title", input.Title.Trim());
        Set(row, "Description", input.Description ?? "");
        Set(row, "Type", input.Type);
        Set(row, "Priority", input.Priority);
        Set(row, "Status", input.Status);
        Set(row, "AssigneeId", input.AssigneeId);
        Set(row, "RoadmapItemId", input.RoadmapItemId);
        Set(row, "DueDate", input.DueDate);
        Set(row, "StartDate", input.StartDate);
        if (input.IsPrivate is not null)
            Set(row, "IsPrivate", input.IsPrivate.Value);
        Revise(row);
        Event(w, Id(row), user, id is null ? "created" : "updated", JsonSerializer.Serialize(new { Before = before, After = input }));
        return Id(row);
    }

    internal Task<int> SaveIssue(int user,int project,int? id,IssueInput input) => WriteIssueChanges(user,id is int existing?[existing]:[],w=>SaveIssue(w,user,project,id,input),project,input);
    internal Task Bulk(int user, IReadOnlyDictionary<int, int> revisions, string? status, string? priority) => WriteIssueChanges(user,revisions.Keys, w =>
    {
        if (revisions.Count is < 1 or > 100 || status is null && priority is null)
            throw new ArgumentException("Odaberi od 1 do 100 zadataka i barem jednu promjenu.");
        foreach (var(id, revision)in revisions.OrderBy(r => r.Key))
        {
            var row = RequireIssue(w, user, id, "issues.edit");
            var input = Detail(row).Input;
            input.Revision = revision;
            input.Status = status ?? input.Status;
            input.Priority = priority ?? input.Priority;
            SaveIssue(w, user, Int(row, "ProjectId"), id, input);
        }

        return true;
    },catalogs: [..status is null?Array.Empty<string>():["value:IssueStatuses:"+status],..priority is null?Array.Empty<string>():["value:IssuePriorities:"+priority]]);
    internal Task<int> Transfer(int user, int id, int revision, int destination, bool move) => WriteIssueChanges(user,[id], w =>
    {
        var row = RequireIssue(w, user, id);
        var source = Int(row, "ProjectId");
        if (!Can(w, user, destination, "issues.create") || !Can(w, user, destination, "issues.view") || move && (!Can(w, user, source, "issues.edit") || !Manage(w, user, source) || !Manage(w, user, destination)))
            throw new UnauthorizedAccessException();
        Revision(row, revision, "Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");
        if (!move)
        {
            var input = Detail(row).Input;
            input.Status = "Novo";
            input.AssigneeId = null;
            if (source != destination)
                input.RoadmapItemId = null;
            return SaveIssue(w, user, destination, null, input);
        }

        if (source == destination)
            throw new ArgumentException("Odaberi drugi projekt za premještanje.");
        Set(row, "ProjectId", destination);
        Set(row, "RoadmapItemId", null);
        Revise(row);
        if (!w.By("ProjectMembers", "ProjectId", destination).Any(m => OptionalInt(row, "AssigneeId") == Int(m, "UserId")))
            Set(row, "AssigneeId", null);
        Event(w, id, user, "moved", JsonSerializer.Serialize(new { FromProjectId = source, ToProjectId = destination }));
        return id;
    },destination,catalogs:["value:IssueStatuses:Novo"]);
    internal Task Comment(int user, int id, string text) => WriteIssues(user,[id], w =>
    {
        RequireIssue(w, user, id, "issues.comment");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
            throw new ArgumentException("Komentar mora imati 1–4000 znakova.");
        return Event(w, id, user, "comment", text.Trim());
    });
    internal Task LogTime(int user, int id, DateTime date, decimal hours, string? note, string? activity) => WriteIssues(user,[id], w =>
    {
        RequireIssue(w, user, id, "time.log");
        if (hours <= 0 || hours > 24 || decimal.Round(hours, 2) != hours || date.Year < 1753 || (note?.Length ?? 0) > 1000)
            throw new ArgumentException("Unesi datum i sate od 0,01 do 24 (najviše dvije decimale).");
        activity = string.IsNullOrWhiteSpace(activity) ? "Općenito" : activity.Trim();
        if (activity.Length > 60)
            throw new ArgumentException("Aktivnost može imati najviše 60 znakova.");
        w.Add("TimeEntries", ("Id", w.Next("TimeEntries")), ("IssueId", id), ("UserId", user), ("WorkDate", date.Date), ("Hours", hours), ("Note", note ?? ""), ("Activity", activity), ("CreatedAt", DateTime.UtcNow));
        return Event(w, id, user, "time", $"{date:yyyy-MM-dd}: {hours} h — {note}");
    });
    internal Task<(List<IssueEvent> Events, List<TimeEntry> Time)> Activity(int user, int id) => Read(w =>
    {
        var issue = RequireIssue(w, user, id);
        var time = Can(w, user, Int(issue, "ProjectId"), "time.view");
        var events = w.By("IssueEvents", "IssueId", id).Where(e => time || Text(e, "Kind") != "time").OrderByDescending(Id).Select(e => new IssueEvent(Text(w.Find("AppUsers", Int(e, "UserId"))!, "DisplayName"), Text(e, "Kind"), Text(e, "Body"), Date(e, "CreatedAt")!.Value)).ToList();
        var entries = w.By("TimeEntries", "IssueId", id).Where(t => time).OrderByDescending(t => Date(t, "WorkDate")).Select(t => new TimeEntry(Text(w.Find("AppUsers", Int(t, "UserId"))!, "DisplayName"), Date(t, "WorkDate")!.Value, Number(t, "Hours"), Text(t, "Note"), Text(t, "Activity"), Id(t))).ToList();
        return (events, entries);
    });
}

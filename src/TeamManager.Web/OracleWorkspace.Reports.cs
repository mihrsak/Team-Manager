using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<List<TimeReportRow>> TimeReport(int user, IEnumerable<int> projects, DateTime? from, DateTime? to, string? person, string? activity) => Read(w =>
    {
        var allowed = projects.ToHashSet();
        return w.Rows("TimeEntries").Select(t => (Time: t, Issue: w.Find("Issues", Int(t, "IssueId")), Person: w.Find("AppUsers", Int(t, "UserId")))).Where(x => x.Issue is not null && x.Person is not null && allowed.Contains(Int(x.Issue, "ProjectId")) && Visible(w, user, x.Issue) && Can(w, user, Int(x.Issue, "ProjectId"), "time.view") && (from is null || Date(x.Time, "WorkDate") >= from) && (to is null || Date(x.Time, "WorkDate") <= to) && (string.IsNullOrEmpty(person) || (Text(x.Person, "DisplayName") + " " + Text(x.Person, "EmployeeNumber")).Contains(person, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(activity) || Text(x.Time, "Activity").Contains(activity, StringComparison.OrdinalIgnoreCase))).GroupBy(x => (Project: Int(x.Issue!, "ProjectId"), Person: Id(x.Person!), Activity: Text(x.Time, "Activity"))).Select(g => new TimeReportRow(Text(w.Find("Projects", g.Key.Project)!, "Name"), Text(w.Find("AppUsers", g.Key.Person)!, "DisplayName"), Text(w.Find("AppUsers", g.Key.Person)!, "EmployeeNumber"), g.Key.Activity, g.Sum(x => Number(x.Time, "Hours")))).ToList();
    });
    internal Task<List<ActivityRow>> RecentActivity(int user, IEnumerable<int> projects) => Read(w =>
    {
        var allowed = projects.ToHashSet();
        return w.Rows("IssueEvents").Select(e => (Event: e, Issue: w.Find("Issues", Int(e, "IssueId")))).Where(x => x.Issue is not null && allowed.Contains(Int(x.Issue, "ProjectId")) && Visible(w, user, x.Issue) && (Text(x.Event, "Kind") != "time" || Can(w, user, Int(x.Issue, "ProjectId"), "time.view"))).OrderByDescending(x => Date(x.Event, "CreatedAt")).ThenByDescending(x => Id(x.Event)).Take(50).Select(x => new ActivityRow(Id(x.Event), Id(x.Issue!), Text(x.Issue!, "Title"), Text(w.Find("Projects", Int(x.Issue!, "ProjectId"))!, "Name"), Text(w.Find("AppUsers", Int(x.Event, "UserId"))!, "DisplayName"), Text(x.Event, "Kind"), Date(x.Event, "CreatedAt")!.Value)).ToList();
    });
    internal Task<List<IssueNotification>> Notifications(int user) => Read(w => w.By("IssueNotifications", "UserId", user).Select(n => (Notification: n, Issue: w.Find("Issues", Int(n, "IssueId")), Event: w.Find("IssueEvents", Int(n, "EventId")))).Where(x => x.Issue is not null && x.Event is not null && Visible(w, user, x.Issue) && (Text(x.Event, "Kind") != "time" || Can(w, user, Int(x.Issue, "ProjectId"), "time.view"))).OrderBy(x => Flag(x.Notification, "IsRead")).ThenByDescending(x => Date(x.Event!, "CreatedAt")).Take(50).Select(x => new IssueNotification(Id(x.Event!), Id(x.Issue!), Text(x.Issue!, "Title"), Text(w.Find("Projects", Int(x.Issue!, "ProjectId"))!, "Name"), Text(x.Event!, "Kind"), Date(x.Event!, "CreatedAt")!.Value, Flag(x.Notification, "IsRead"))).ToList());
}

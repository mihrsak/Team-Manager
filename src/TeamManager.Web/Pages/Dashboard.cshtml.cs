using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class DashboardModel(IssueStore issues) : PageModel
{
    public List<IssueRow> Overdue { get; private set; } = [];
    public List<IssueRow> WithDeadline { get; private set; } = [];
    public List<IssueRow> Watched { get; private set; } = [];
    public List<IssueRow> WithoutDeadline { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var today = DateTime.Today;
        Response.Headers.CacheControl = "private, no-store";
        var assigned = await issues.List(user, null, null, null, assigneeId: user, openOnly: true);
        Overdue = assigned.Where(issue => issue.DueDate < today).OrderBy(issue => issue.DueDate).ToList();
        WithDeadline = assigned.Where(issue => issue.DueDate >= today).OrderBy(issue => issue.DueDate).ToList();
        WithoutDeadline = assigned.Where(issue => issue.DueDate is null).ToList();
        Watched = await issues.List(user, null, null, null, watchedOnly: true);
    }
}

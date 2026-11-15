using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed record GanttTask(IssueRow Issue, int FirstDay, int Span, string Dates);

public sealed class GanttModel(IssueStore issues, IssueLinkStore links, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }

    public List<Team> Projects { get; private set; } = [];
    public List<GanttTask> Tasks { get; private set; } = [];
    public ILookup<int, int> Dependencies { get; private set; } = Array.Empty<int>().ToLookup(id => id);
    public int Days { get; private set; }
    public string Caption { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid) return BadRequest();
        Month ??= DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (!DateTime.TryParseExact(Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            || month.Year < 1753)
            return BadRequest();

        Days = DateTime.DaysInMonth(month.Year, month.Month);
        var end = month.AddDays(Days - 1);
        Caption = month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en-GB" : "hr-HR"));
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Projects = await issues.Projects(user);
        if (ProjectId is int project && !Projects.Any(item => item.Id == project))
            return NotFound();

        var rows = await issues.List(user, ProjectId, null, null, month, end, schedule: true);
        foreach (var issue in rows.OrderBy(issue => issue.StartDate ?? issue.DueDate).ThenBy(issue => issue.Id))
        {
            var startDate = (issue.StartDate ?? issue.DueDate)!.Value;
            var endDate = (issue.DueDate ?? issue.StartDate)!.Value;
            var first = startDate < month ? 1 : startDate.Day;
            var last = endDate > end ? Days : endDate.Day;
            var dates = issue.StartDate is null ? $"{text["Rok"]} {endDate:dd.MM.yyyy}" :
                issue.DueDate is null ? $"{text["Početak"]} {startDate:dd.MM.yyyy}" :
                $"{startDate:dd.MM.yyyy} – {endDate:dd.MM.yyyy}";
            Tasks.Add(new(issue, first, last - first + 1, dates));
        }
        if (Tasks.Count > 0)
            Dependencies = await links.Dependencies(Tasks.Select(task => task.Issue.Id));

        return Page();
    }
}

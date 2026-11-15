using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class CalendarModel(IssueStore issues) : PageModel
{
    private static CultureInfo CalendarCulture => CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en-GB" : "hr-HR");
    private static readonly DateTime FirstMonth = new(1753, 1, 1);
    private static readonly DateTime LastMonth = new(9999, 12, 1);

    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }

    public List<Team> Projects { get; private set; } = [];
    public ILookup<int, IssueRow> Deadlines { get; private set; } = Array.Empty<IssueRow>().ToLookup(_ => 0);
    public DateTime Start { get; private set; }
    public int Days { get; private set; }
    public int FirstDayOffset => ((int)Start.DayOfWeek + 6) % 7;
    public int WeekCount => (FirstDayOffset + Days + 6) / 7;
    public string Caption => Start.ToString("MMMM yyyy", CalendarCulture);
    public string? Previous => Start > FirstMonth ? FormatMonth(Start.AddMonths(-1)) : null;
    public string? Next => Start < LastMonth ? FormatMonth(Start.AddMonths(1)) : null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid) return BadRequest();
        var today = DateTime.Today;
        var start = new DateTime(today.Year, today.Month, 1);
        if (Month is not null &&
            (!DateTime.TryParseExact(Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) ||
             start < FirstMonth))
            return BadRequest();

        Start = start;
        Month = FormatMonth(Start);
        Days = DateTime.DaysInMonth(Start.Year, Start.Month);
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Projects = await issues.Projects(user);
        if (ProjectId is int project && !Projects.Any(item => item.Id == project)) return NotFound();

        var rows = await issues.List(user, ProjectId, null, null, Start, Start.AddDays(Days - 1));
        Deadlines = rows.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToLookup(row => row.DueDate!.Value.Day);
        return Page();
    }

    private static string FormatMonth(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

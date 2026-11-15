using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class TimeReportModel(IssueStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true), StringLength(100)] public string? Person { get; set; }
    [BindProperty(SupportsGet = true), StringLength(60)] public string? Activity { get; set; }

    public List<Team> Projects { get; private set; } = [];
    public List<TimeReportRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Projects = await store.Projects(user, "time.view");
        if (ProjectId is int project && !Projects.Any(p => p.Id == project))
            return NotFound();

        if (From > To)
            ModelState.AddModelError(nameof(From), "Početni datum mora biti prije završnog datuma ili jednak njemu.");

        if (ModelState.IsValid)
        {
            var projectIds = ProjectId is int selected ? [selected] : Projects.Select(p => p.Id).ToArray();
            if (projectIds.Length > 0)
                Rows = await store.TimeReport(user, projectIds, From?.Date, To?.Date, Person?.Trim(), Activity?.Trim());
        }

        return Page();
    }
}

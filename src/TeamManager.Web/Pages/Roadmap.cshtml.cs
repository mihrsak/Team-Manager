using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class RoadmapModel(ProjectStore projects, RoadmapStore roadmap, IssueStore issues) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<RoadmapItem> Items { get; private set; } = [];
    public ILookup<int, RoadmapStatusCount>? Progress { get; private set; }
    public bool CanEdit { get; private set; }
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        Project = project;
        CanEdit = project.Input.State == "Active" && await projects.CanManage(UserId, projectId);
        Items = await roadmap.List(projectId);
        if (await issues.Can(UserId, projectId, "issues.view"))
            Progress = await roadmap.Progress(UserId, projectId);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int projectId, int itemId, int revision,
        string? kind, string? name, string? description, DateTime? targetDate, bool isComplete)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();

        name = name?.Trim();
        description = description?.Trim() ?? "";
        if (!ModelState.IsValid || itemId < 0 || revision < 0 ||
            kind is not ("version" or "milestone") || name is null or { Length: 0 or > 100 } || description.Length > 1000)
            return Invalid("Provjeri vrstu, naziv, opis i datum.");
        try { await roadmap.Save(UserId, projectId, new(itemId, kind, name, description, targetDate, isComplete, revision)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Invalid("Taj naziv već postoji za odabranu vrstu."); }
        catch (InvalidOperationException ex) { return Invalid(ex.Message); }
        return RedirectToPage(new { projectId });
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", message);
        return Page();
    }
}

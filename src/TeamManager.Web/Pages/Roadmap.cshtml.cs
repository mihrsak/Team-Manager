using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed record RoadmapDraft(int ItemId, string IdValue, string Revision, string Kind, string Name,
    string Description, string TargetDate, bool IsComplete);

public sealed class RoadmapModel(ProjectStore projects, RoadmapStore roadmap, IssueStore issues) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<RoadmapItem> Items { get; private set; } = [];
    public ILookup<int, RoadmapStatusCount>? Progress { get; private set; }
    public bool CanEdit { get; private set; }
    public RoadmapDraft? Draft { get; private set; }
    public bool HasConflict => Draft is { ItemId: > 0 } draft &&
        int.TryParse(draft.Revision, out var expected) &&
        Items.Find(item => item.Id == draft.ItemId) is { } current && current.Revision != expected;
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
        string? kind, string? name, string? description, DateTime? targetDate, bool isComplete,
        bool resolveConflict = false, int? reviewedRevision = null)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();

        if (resolveConflict && reviewedRevision is >= 0) revision = reviewedRevision.Value;
        Draft = new(itemId, Request.Form["itemId"].ToString(),
            resolveConflict && reviewedRevision is >= 0 ? revision.ToString(System.Globalization.CultureInfo.InvariantCulture) : Request.Form["revision"].ToString(),
            Request.Form["kind"].ToString(), Request.Form["name"].ToString(),
            Request.Form["description"].ToString(), Request.Form["targetDate"].ToString(), isComplete);
        name = name?.Trim();
        description = description?.Trim() ?? "";
        if (!ModelState.IsValid || itemId < 0 || revision < 0 ||
            kind is not ("version" or "milestone") || name is null or { Length: 0 or > 100 } || description.Length > 1000)
            return Invalid("Provjeri vrstu, naziv, opis i datum.");
        try { await roadmap.Save(UserId, projectId, new(itemId, kind, name, description, targetDate, isComplete, revision)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Invalid("Taj naziv već postoji za odabranu vrstu."); }
        catch (InvalidOperationException ex)
        {
            // A second writer may have won after the initial read. Refresh the saved
            // version for review, but retain the submitted draft and expected revision.
            var latest = await OnGetAsync(projectId);
            if (latest is not PageResult) return latest;
            return Invalid(ex.Message);
        }
        catch (ArgumentException ex) { return Invalid(ex.Message); }
        return RedirectToPage(new { projectId });
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", message);
        return Page();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class RepositoryHistoryModel(ProjectStore projects, ProjectRepositoryStore repositories,
    Permissions permissions, RepositoryHistory history, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public int ProjectId { get; private set; }
    public ProjectRepository Repository { get; private set; } = null!;
    public List<RepositoryCommit> Commits { get; private set; } = [];
    public RepositoryCommit? Selected { get; private set; }
    public RepositoryPatch? Patch { get; private set; }

    public async Task<IActionResult> OnGetAsync(int projectId, int repositoryId, string? commit)
    {
        if (!ModelState.IsValid || repositoryId <= 0) return NotFound();
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!(await projects.List(user)).Any(project => project.Id == projectId)) return NotFound();

        var rights = await permissions.Effective(user, "project", projectId);
        if (!rights.Contains("*") && !rights.Contains("repositories.view")) return Forbid();

        var repository = await repositories.ForHistory(user, projectId, repositoryId);
        if (repository is null || !history.IsConfigured(repositoryId)) return NotFound();

        ProjectId = projectId;
        Repository = repository;
        Response.Headers.CacheControl = "private, no-store";
        try
        {
            Commits = await history.Recent(repositoryId, HttpContext.RequestAborted);
            if (commit is not null)
            {
                Selected = Commits.Find(item => item.Id == commit);
                if (Selected is null) return NotFound();
                Patch = await history.Diff(repositoryId, Selected, HttpContext.RequestAborted);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError("", text["Povijest repozitorija trenutačno nije dostupna."]);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        return Page();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class RepositoriesModel(ProjectStore projects, ProjectRepositoryStore repositories, Permissions permissions, RepositoryHistory history) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<ProjectRepository> Repositories { get; private set; } = [];
    public HashSet<int> Browsable { get; private set; } = [];
    public bool CanEdit { get; private set; }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        if (!ModelState.IsValid) return BadRequest();
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        Project = project;
        CanEdit = project.Input.State == "Active" && await projects.CanManage(UserId, projectId);
        Repositories = await repositories.List(projectId);
        var rights = await permissions.Effective(UserId, "project", projectId);
        if (rights.Contains("*") || rights.Contains("repositories.view"))
            Browsable = Repositories.Where(repo => history.IsConfigured(repo.Id)).Select(repo => repo.Id).ToHashSet();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(int projectId, string? name, string? url)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();
        name = name?.Trim();
        url = url?.Trim();
        if (name is null or { Length: 0 or > 80 } ||
            url is null or { Length: 0 or > 500 } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsoluteUri.Length > 500)
        {
            ModelState.AddModelError("", "Unesi naziv do 80 znakova i HTTPS adresu repozitorija bez vjerodajnica, parametara ili fragmenta.");
            return Page();
        }

        try
        {
            if (!await repositories.Add(UserId, projectId, name, uri)) return Forbid();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            ModelState.AddModelError("", "Taj naziv već postoji.");
            return Page();
        }
        return RedirectToPage(new { projectId });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int projectId, int repositoryId)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();
        if (repositoryId <= 0) return NotFound();
        try
        {
            if (!await repositories.Remove(UserId, projectId, repositoryId)) return NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToPage(new { projectId });
    }
}

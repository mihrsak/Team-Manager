using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class IssueFieldsModel(ProjectStore projects, IssueFieldStore fields) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<IssueField> Fields { get; private set; } = [];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        if (!await projects.CanManage(UserId, projectId)) return Forbid();
        Project = project;
        Fields = await fields.Definitions(projectId);
        return Page();
    }

    public async Task<IActionResult> OnPostVisibilityAsync(int projectId, int fieldId, bool? managersOnly)
    {
        if (fieldId <= 0 || managersOnly is null) return BadRequest();
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (Project.Input.State != "Active") return Forbid();
        try
        {
            if (!await fields.SetVisibility(UserId, projectId, fieldId, managersOnly.Value))
                return NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToPage(new { projectId });
    }

    public async Task<IActionResult> OnPostCreateAsync(int projectId, string? name, bool managersOnly)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (Project.Input.State != "Active") return Forbid();
        name = name?.Trim();
        if (!ModelState.IsValid || name is null or { Length: 0 or > 100 })
        {
            ModelState.AddModelError("", "Unesi valjan naziv do 100 znakova.");
            return Page();
        }
        try { await fields.Create(UserId, projectId, name, managersOnly); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            ModelState.AddModelError("", "Taj naziv već postoji.");
            return Page();
        }
        return RedirectToPage(new { projectId });
    }
}

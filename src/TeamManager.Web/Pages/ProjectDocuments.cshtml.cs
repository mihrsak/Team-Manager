using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class ProjectDocumentsModel(ProjectStore projects, ProjectDocumentStore documents, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<ProjectDocument> Documents { get; private set; } = [];
    public bool CanEdit { get; private set; }
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<ProjectDetail?> VisibleProject(int id)
        => (await projects.List(UserId)).Find(project => project.Id == id);

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        Response.Headers.CacheControl = "private, no-store";
        var project = await VisibleProject(projectId);
        if (project is null) return NotFound();
        Project = project;
        CanEdit = project.Input.State == "Active" && await projects.CanManage(UserId, projectId);
        Documents = await documents.List(UserId, projectId);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(int projectId, int documentId)
    {
        if (documentId <= 0 || await VisibleProject(projectId) is null) return NotFound();
        var document = await documents.Download(UserId, projectId, documentId);
        if (document is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Content, "application/octet-stream", document.Name);
    }

    public async Task<IActionResult> OnPostAddAsync(int projectId, string? title, string? description, IFormFile? attachment)
    {
        var result = await OnGetAsync(projectId);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();

        title = title?.Trim();
        description = description?.Trim() ?? "";
        if (!ModelState.IsValid || title is null or { Length: 0 or > 150 } || description.Length > 2000)
            return Invalid("Unesi naslov do 150 znakova i opis do 2000 znakova.");

        try { await documents.Add(UserId, projectId, title, description, attachment, HttpContext.RequestAborted); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return Invalid(ex.Message); }
        return BackToLibrary(projectId);
    }

    public async Task<IActionResult> OnPostRemoveAsync(int projectId, int documentId)
    {
        var project = await VisibleProject(projectId);
        if (project is null || documentId <= 0) return NotFound();
        if (project.Input.State != "Active" || !await projects.CanManage(UserId, projectId)) return Forbid();
        try
        {
            if (!await documents.Remove(UserId, projectId, documentId)) return NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return BackToLibrary(projectId);
    }

    private IActionResult BackToLibrary(int projectId)
    {
        Response.Headers.Location = Url.Page("/ProjectDocuments", new { projectId });
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", text[message]);
        return Page();
    }
}

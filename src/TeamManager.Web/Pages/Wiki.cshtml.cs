using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class WikiModel(ProjectStore projects, ProjectWikiStore wiki, Permissions permissions, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<WikiPage> Pages { get; private set; } = [];
    public WikiPage? Selected { get; private set; }
    public WikiRevision? Displayed { get; private set; }
    public WikiRevision? Compared { get; private set; }
    public List<WikiRevisionInfo> History { get; private set; } = [];
    public bool CanEdit { get; private set; }
    public string? DraftTitle { get; private set; }
    public string? DraftBody { get; private set; }
    public int? DraftRevision { get; private set; }
    public bool HasDraft => DraftRevision is not null;
    public bool HasConflict => Selected is not null && DraftRevision is int revision && revision != Selected.Revision;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId, int? wikiId, int? revision, int? compare)
    {
        if (!ModelState.IsValid) return BadRequest();
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        Project = project;
        Pages = await wiki.Pages(UserId, projectId);

        var rights = await permissions.Effective(UserId, "project", projectId);
        CanEdit = project.Input.State == "Active" && (rights.Contains("*") || rights.Contains("wiki.edit"));
        if (wikiId is not int selectedId) return Page();

        Selected = Pages.Find(page => page.Id == selectedId);
        if (Selected is null) return NotFound();
        Displayed = await wiki.Revision(UserId, projectId, selectedId, revision ?? Selected.Revision);
        if (Displayed is null) return NotFound();
        if (compare is int other)
        {
            Compared = await wiki.Revision(UserId, projectId, selectedId, other);
            if (Compared is null) return NotFound();
        }
        History = await wiki.History(UserId, projectId, selectedId);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int projectId, int? wikiId, int revision, string? title, string? body, bool resolveConflict = false, int? reviewedRevision = null)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await OnGetAsync(projectId, wikiId, null, null);
        if (result is not PageResult) return result;
        if (!CanEdit) return Forbid();

        DraftTitle = Request.Form["title"].ToString();
        DraftBody = Request.Form["body"].ToString();
        DraftRevision = resolveConflict && reviewedRevision is int reviewed ? reviewed : revision;
        title = title?.Trim();
        body = body?.Trim();
        if (title is null or { Length: 0 or > 120 } || body is null or { Length: 0 or > 20000 })
            return Invalid("Unesi naslov do 120 znakova i sadržaj do 20 000 znakova.");

        try
        {
            var saved = await wiki.Save(UserId, projectId, wikiId, DraftRevision.Value, title, body);
            if (saved is null)
            {
                // Refresh the current version after a race, while retaining both the
                // user's draft and its original revision. A retry must not silently
                // replace a newer version without explicit review.
                var latest = await OnGetAsync(projectId, wikiId, null, null);
                if (latest is not PageResult) return latest;
                return Invalid("Stranica je u međuvremenu izmijenjena. Pregledaj najnoviju verziju prije ponovnog uređivanja.");
            }
            return RedirectToPage(new { projectId, wikiId = saved });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (WikiTitleConflictException ex) { return Invalid(ex.Message); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Invalid("Stranica s tim naslovom već postoji u projektu.");
        }
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", text[message]);
        return Page();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class ProjectNewsModel(ProjectStore projects, ProjectNewsStore news, Permissions permissions, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<ProjectNewsItem> Items { get; private set; } = [];
    public List<ProjectNewsComment> Comments { get; private set; } = [];
    public int? NewsId { get; private set; }
    public bool CanPublish { get; private set; }
    public bool CanComment { get; private set; }
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId, int? newsId)
    {
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        Project = project;
        NewsId = newsId;

        Items = await news.List(UserId, projectId, newsId);
        if (newsId is int selected)
        {
            if (Items.Count == 0) return NotFound();
            Comments = await news.Comments(UserId, projectId, selected);
        }

        if (project.Input.State == "Active")
        {
            CanPublish = await projects.CanManage(UserId, projectId);
            var rights = await permissions.Effective(UserId, "project", projectId);
            CanComment = rights.Contains("*") || rights.Contains("news.comment");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(int projectId, string? title, string? body)
    {
        var result = await OnGetAsync(projectId, null);
        if (result is not PageResult) return result;
        if (!CanPublish) return Forbid();
        title = title?.Trim();
        body = body?.Trim();
        if (!ModelState.IsValid || title is null or { Length: 0 or > 150 } || body is null or { Length: 0 or > 4000 })
            return Invalid("Unesi naslov do 150 znakova i tekst do 4000 znakova.");

        var newsId = await news.Publish(UserId, projectId, title, body);
        if (newsId is null) return Forbid();
        return RedirectToPage(new { projectId, newsId });
    }

    public async Task<IActionResult> OnPostCommentAsync(int projectId, int newsId, string? body)
    {
        var result = await OnGetAsync(projectId, newsId);
        if (result is not PageResult) return result;
        if (!CanComment) return Forbid();
        body = body?.Trim();
        if (!ModelState.IsValid || body is null or { Length: 0 or > 2000 })
            return Invalid("Unesi komentar do 2000 znakova.");

        if (!await news.Comment(UserId, projectId, newsId, body)) return Forbid();
        return RedirectToPage(new { projectId, newsId });
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", text[message]);
        return Page();
    }
}

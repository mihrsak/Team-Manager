using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class ForumModel(ProjectStore projects, ProjectForumStore forum, Permissions permissions) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public List<ForumTopic> Topics { get; private set; } = [];
    public ForumTopic? Selected { get; private set; }
    public List<ForumReply> Replies { get; private set; } = [];
    public bool CanPost { get; private set; }
    public bool CanModerate { get; private set; }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync(int projectId, int? topicId)
    {
        if (!ModelState.IsValid) return BadRequest();
        var project = (await projects.List(UserId)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        Project = project;

        Topics = await forum.Topics(projectId, topicId);
        if (topicId is int id)
        {
            Selected = Topics.SingleOrDefault();
            if (Selected is null) return NotFound();
            Replies = await forum.Replies(projectId, id);
        }

        if (project.Input.State == "Active")
        {
            var rights = await permissions.Effective(UserId, "project", projectId);
            CanPost = rights.Contains("*") || rights.Contains("forum.post");
            CanModerate = rights.Contains("*") || rights.Contains("forum.moderate");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(int projectId, string? title, string? body)
    {
        var result = await OnGetAsync(projectId, null);
        if (result is not PageResult) return result;
        if (!CanPost) return Forbid();
        title = title?.Trim();
        body = body?.Trim();
        if (title is null or { Length: 0 or > 150 } || body is null or { Length: 0 or > 4000 })
            return Invalid("Unesi naslov do 150 znakova i tekst do 4000 znakova.");
        var topicId = await forum.Create(UserId, projectId, title, body);
        return topicId is null ? Forbid() : RedirectToPage(new { projectId, topicId });
    }

    public async Task<IActionResult> OnPostReplyAsync(int projectId, int topicId, string? body)
    {
        var result = await OnGetAsync(projectId, topicId);
        if (result is not PageResult) return result;
        if (!CanPost || Selected!.IsLocked) return Forbid();
        body = body?.Trim();
        if (body is null or { Length: 0 or > 4000 }) return Invalid("Unesi odgovor do 4000 znakova.");
        if (!await forum.Reply(UserId, projectId, topicId, body)) return Forbid();
        return RedirectToPage(new { projectId, topicId });
    }

    public async Task<IActionResult> OnPostLockAsync(int projectId, int topicId, bool? locked)
    {
        if (locked is null) return BadRequest();
        var result = await OnGetAsync(projectId, topicId);
        if (result is not PageResult) return result;
        if (!CanModerate || !await forum.SetLocked(projectId, topicId, locked.Value)) return Forbid();
        return RedirectToPage(new { projectId, topicId });
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", message);
        return Page();
    }
}

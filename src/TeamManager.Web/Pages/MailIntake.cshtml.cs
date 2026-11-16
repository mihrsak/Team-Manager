using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class MailIntakeModel(ProjectStore projects, IConfiguration configuration) : PageModel
{
    public ProjectDetail Project { get; private set; } = null!;
    public string? EmployeeNumber { get; private set; }

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var project = (await projects.List(user)).Find(item => item.Id == projectId);
        if (project is null) return NotFound();
        if (!await projects.CanManage(user,projectId)) return Forbid();

        Response.Headers.CacheControl = "private, no-store";
        Project = project;
        EmployeeNumber = IncomingMail.ConfiguredAccount(configuration,projectId);
        return Page();
    }
}

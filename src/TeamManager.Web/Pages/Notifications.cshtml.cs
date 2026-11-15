using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class NotificationsModel(IssuePersonalStore personal) : PageModel
{
    public List<IssueNotification> Entries { get; private set; } = [];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        Entries = await personal.Notifications(UserId);
    }

    public async Task<IActionResult> OnPostReadAsync(int eventId)
    {
        if (eventId <= 0 || !await personal.MarkRead(UserId, eventId)) return NotFound();
        return RedirectToPage();
    }
}

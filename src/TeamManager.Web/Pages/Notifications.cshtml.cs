using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class NotificationsModel(IssuePersonalStore personal) : PageModel
{
    public List<IssueNotification> Entries { get; private set; } = [];
    [BindProperty(SupportsGet=true)]public int PageNumber {get;set;}=1;
    [BindProperty(SupportsGet=true)]public string? Query {get;set;}
    public bool HasMore {get;private set;}
    public string PageUrl(int page)=>Url.Page("/Notifications",new{PageNumber=page,Query})!;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        PageNumber=Math.Clamp(PageNumber,1,10000);Query=Query?.Trim();if(Query?.Length>200)Query=Query[..200];
        Entries = await personal.Notifications(UserId,(PageNumber-1)*50,51,Query);
        HasMore=Entries.Count>50;Entries=Entries.Take(50).ToList();
    }

    public async Task<IActionResult> OnPostReadAsync(int eventId)
    {
        if (eventId <= 0 || !await personal.MarkRead(UserId, eventId)) return NotFound();
        return RedirectToPage(new{PageNumber,Query});
    }
}

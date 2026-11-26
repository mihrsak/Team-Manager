using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class NotificationsModel(IssuePersonalStore personal) : PageModel
{
    public List<IssueNotification> Entries { get; private set; } = [];
    [BindProperty(SupportsGet=true)]public int PageNumber {get;set;}=1;
    [BindProperty(SupportsGet=true),StringLength(200)]public string? Query {get;set;}
    public bool FiltersValid=>ListFilterValidation.Valid(ModelState,"PageNumber","Query");
    public bool HasMore {get;private set;}
    public string PageUrl(int page)=>Url.Page("/Notifications",new{PageNumber=page,Query})!;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        if(!ValidateFilters())return;
        Entries = await personal.Notifications(UserId,(PageNumber-1)*50,51,Query?.Trim());
        HasMore=Entries.Count>50;Entries=Entries.Take(50).ToList();
    }

    public async Task<IActionResult> OnPostReadAsync(int eventId)
    {
        Response.Headers.CacheControl = "private, no-store";
        if(!ValidateFilters())return Page();
        if (eventId <= 0 || !await personal.MarkRead(UserId, eventId)) return NotFound();
        return RedirectToPage(new{PageNumber,Query});
    }

    private bool ValidateFilters()
    {
        ListFilterValidation.SingleValues(Request,ModelState,"PageNumber","Query");
        if(PageNumber is <1 or >10000)ModelState.TryAddModelError(nameof(PageNumber),"Unesi valjan broj.");
        return FiltersValid;
    }
}

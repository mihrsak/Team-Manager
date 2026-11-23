using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class NotFoundModel : PageModel
{
    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        Response.Headers.CacheControl = "private, no-store";
    }
}

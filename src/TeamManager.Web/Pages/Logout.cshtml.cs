using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class LogoutModel:PageModel
{
    public IActionResult OnGet()=>RedirectToPage("/Users");
    public async Task<IActionResult> OnPostAsync(){await HttpContext.SignOutAsync();return RedirectToPage("/Login");}
}

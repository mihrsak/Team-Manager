using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;
public class LoginModel(UserStore store):PageModel
{
    [BindProperty,Required,StringLength(30)] public string EmployeeNumber {get;set;}="";
    [BindProperty,Required,StringLength(200)] public string Password {get;set;}="";
    public IActionResult OnGet()=>User.Identity?.IsAuthenticated==true?RedirectToPage("/Users"):Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if(!ModelState.IsValid)return Page();
        var user=await store.Authenticate(EmployeeNumber.Trim().ToLowerInvariant(),Password);
        if(user is null){ModelState.AddModelError("","Neispravna prijava ili privremeno zaključan račun. Nakon više pokušaja pričekajte pet minuta.");return Page();}
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,user.Id.ToString()),new(ClaimTypes.Name,user.EmployeeNumber)};
        if(user.IsAdministrator)claims.Add(new(ClaimTypes.Role,"Administrator"));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)));
        return RedirectToPage(user.EmployeeNumber=="admin" && Password=="admin" ? "/InitialPassword" : "/Users");
    }
}

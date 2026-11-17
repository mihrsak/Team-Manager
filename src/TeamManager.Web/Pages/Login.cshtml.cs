using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;
public class LoginModel(UserStore store, IConfiguration configuration):PageModel
{
    [BindProperty,Required,StringLength(30)] public string EmployeeNumber {get;set;}="";
    [BindProperty,Required,StringLength(200)] public string Password {get;set;}="";
    public bool SsoEnabled => SsoLogin.Enabled(configuration);
    public IActionResult OnGet(bool ssoError = false)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Users");
        if (ssoError && SsoEnabled) ModelState.AddModelError("", "SSO prijava nije uspjela ili identitet nije povezan s aktivnim računom.");
        return Page();
    }
    public IActionResult OnPostSso()
    {
        if (!SsoEnabled) return NotFound();
        return Challenge(new AuthenticationProperties { RedirectUri = Url.Page("/Dashboard") }, SsoLogin.Scheme);
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(!ModelState.IsValid)return Page();
        var user=await store.Authenticate(EmployeeNumber.Trim().ToLowerInvariant(),Password);
        if(user is null){ModelState.AddModelError("","Neispravna prijava ili privremeno zaključan račun. Nakon više pokušaja pričekajte pet minuta.");return Page();}
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,SsoLogin.LocalPrincipal(user));
        return RedirectToPage(user.EmployeeNumber=="admin" && Password=="admin" ? "/InitialPassword" : "/Users");
    }
}

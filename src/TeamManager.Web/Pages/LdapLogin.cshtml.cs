using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace TeamManager.Web.Pages;

[EnableRateLimiting("LdapLogin")]
public class LdapLoginModel(UserStore users, LdapDirectory directory) : PageModel
{
    [BindProperty, Required, StringLength(30)] public string EmployeeNumber { get; set; } = "";
    [BindProperty, Required, StringLength(200)] public string Password { get; set; } = "";

    public IActionResult OnGet()
    {
        if (!directory.Enabled) return NotFound();
        return User.Identity?.IsAuthenticated == true ? RedirectToPage("/Dashboard") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!directory.Enabled) return NotFound();
        if (!ModelState.IsValid) return Page();

        var user = await users.Find(EmployeeNumber.Trim().ToLowerInvariant());
        var principal = directory.Authenticate(user, Password);
        if (principal is null)
        {
            ModelState.AddModelError("", "LDAP prijava nije uspjela. Provjeri podatke ili se obrati administratoru.");
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return RedirectToPage("/Dashboard");
    }
}

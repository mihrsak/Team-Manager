using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace TeamManager.Web.Pages;

[EnableRateLimiting("Registration")]
public sealed class RegisterModel(RegistrationStore registrations, IConfiguration configuration, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public bool Submitted { get; private set; }

    public IActionResult OnGet()
    {
        if (!configuration.GetValue<bool>("Registration:Enabled")) return NotFound();
        return User.Identity?.IsAuthenticated == true ? RedirectToPage("/Profile") : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? employee, string? name, string? password, string? confirm)
    {
        if (!configuration.GetValue<bool>("Registration:Enabled")) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Profile");

        employee = employee?.Trim().ToLowerInvariant();
        name = name?.Trim();
        if (!ModelState.IsValid || employee is null || !Regex.IsMatch(employee, @"^[a-z0-9._-]{1,30}$") ||
            name is null or { Length: 0 or > 100 } || password is null or { Length: < 10 or > 200 } || password != confirm)
        {
            ModelState.AddModelError("", text["Provjeri broj radnika, ime i podudaranje lozinki od 10 do 200 znakova."]);
            return Page();
        }

        await registrations.Submit(employee, name, password);
        Submitted = true;
        return Page();
    }
}

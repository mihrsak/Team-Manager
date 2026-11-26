using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class RegistrationRequestsModel(RegistrationStore registrations, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public List<PendingRegistration> Pending { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("*")) return Forbid();
        Response.Headers.CacheControl = "private, no-store";
        Pending = await registrations.Pending(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return Page();
    }

    public async Task<IActionResult> OnPostDecisionAsync(int id, bool? approve)
    {
        if (!User.IsInRole("*")) return Forbid();
        if (id <= 0 || approve is null) return BadRequest();
        var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            if (!await registrations.Decide(actor, id, approve.Value)) return NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (RegistrationConflictException ex) { var result = await OnGetAsync(); ModelState.AddModelError("", text[ex.Message]); return result; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            var result = await OnGetAsync();
            ModelState.AddModelError("", text["Račun s tim brojem radnika već postoji. Odbij zahtjev ako više nije potreban."]);
            return result;
        }
        return RedirectToPage();
    }
}

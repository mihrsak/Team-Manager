using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class AdministrationAuditModel(AdministrationAuditStore audit) : PageModel
{
    public List<AdministrationAuditEntry> Entries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("*")) return Forbid();
        Response.Headers.CacheControl = "private, no-store";
        Entries = await audit.Recent();
        return Page();
    }

    public static string Label(string action) => action switch
    {
        "account.reset" => "Reset lozinke",
        "account.activate" => "Aktivacija računa",
        "account.deactivate" => "Deaktivacija računa",
        "account.revoke" => "Opoziv sesija",
        _ => action
    };
}

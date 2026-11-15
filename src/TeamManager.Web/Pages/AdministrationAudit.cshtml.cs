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
        "account.create" => "Kreiranje računa",
        "account.update" => "Uređivanje računa",
        "role.create" => "Kreiranje uloge",
        "role.update" => "Izmjena ovlasti uloge",
        "role.assign" => "Dodjela globalne uloge",
        "role.revoke" => "Uklanjanje globalne uloge",
        "group.create" => "Kreiranje grupe",
        "group.member.add" => "Dodavanje člana grupe",
        "group.member.remove" => "Uklanjanje člana grupe",
        "group.role.add" => "Dodjela uloge grupi",
        "group.role.remove" => "Uklanjanje uloge grupe",
        "membership.role.add" => "Dodjela uloge u članstvu",
        "membership.role.remove" => "Uklanjanje dodatne uloge",
        "membership.role.replace" => "Zamjena osnovne uloge",
        "membership.remove" => "Uklanjanje članstva",
        "organization.unit.create" => "Kreiranje organizacijske jedinice",
        "organization.team.create" => "Kreiranje tima",
        "organization.project.create" => "Kreiranje projekta iz imenika",
        "project.create" => "Kreiranje projekta",
        "project.update" => "Izmjena postavki projekta",
        "project.modules" => "Izmjena modula projekta",
        "workflow.value.create" => "Dodavanje vrijednosti šifrarnika",
        "workflow.transition.add" => "Dodavanje prijelaza statusa",
        "workflow.transition.remove" => "Uklanjanje prijelaza statusa",
        "roadmap.create" => "Dodavanje stavke plana",
        "roadmap.update" => "Izmjena stavke plana",
        _ => action
    };
}

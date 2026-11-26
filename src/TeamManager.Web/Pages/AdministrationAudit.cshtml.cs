using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class AdministrationAuditModel(AdministrationAuditStore audit) : PageModel
{
    public List<AdministrationAuditEntry> Entries { get; private set; } = [];
    [BindProperty(SupportsGet=true)]public int PageNumber {get;set;}=1;
    [BindProperty(SupportsGet=true),StringLength(200)]public string? Query {get;set;}
    public bool FiltersValid=>ListFilterValidation.Valid(ModelState,"PageNumber","Query");
    public bool HasMore {get;private set;}
    public string PageUrl(int page)=>Url.Page("/AdministrationAudit",new{PageNumber=page,Query})!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("*")) return Forbid();
        Response.Headers.CacheControl = "private, no-store";
        ListFilterValidation.SingleValues(Request,ModelState,"PageNumber","Query");
        if(PageNumber is <1 or >5000)ModelState.TryAddModelError(nameof(PageNumber),"Unesi valjan broj.");
        if(!FiltersValid)return Page();
        Entries = await audit.Recent((PageNumber-1)*100,101,Query?.Trim());
        HasMore=Entries.Count>100;Entries=Entries.Take(100).ToList();
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
        "workflow.value.update" => "Izmjena vrijednosti šifrarnika",
        "workflow.transition.add" => "Dodavanje prijelaza statusa",
        "workflow.transition.remove" => "Uklanjanje prijelaza statusa",
        "roadmap.create" => "Dodavanje stavke plana",
        "roadmap.update" => "Izmjena stavke plana",
        "project.issue-field.create" => "Dodavanje polja zadatka",
        "custom-field.create" => "Dodavanje prilagodljivog polja",
        "registration.approve" => "Odobrenje registracije",
        "registration.reject" => "Odbijanje registracije",
        "project.issue-field.restrict" => "Ograničavanje vidljivosti polja zadatka",
        "project.issue-field.share" => "Dijeljenje polja zadatka",
        "project.repository.add" => "Dodavanje Git repozitorija",
        "project.repository.remove" => "Uklanjanje Git repozitorija",
        _ => action
    };
}

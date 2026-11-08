using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public class RolesModel(Permissions permissions):PageModel
{
    public List<AccessRole> Roles{get;set;}=[];
    public async Task<IActionResult> OnGetAsync(){if(!User.IsInRole("roles.manage"))return Forbid();Roles=await permissions.Roles();return Page();}
    public async Task<IActionResult> OnPostAsync(string? name,string? description,string[] rights,bool create)
    {
        if(!User.IsInRole("roles.manage"))return Forbid();Roles=await permissions.Roles();name=name?.Trim();description=description?.Trim()??"";
        if(string.IsNullOrWhiteSpace(name)||name.Length>30||description.Length>200||rights.Any(x=>!Permissions.Catalog.ContainsKey(x))||(!create&&!Roles.Any(x=>x.Name==name))){ModelState.AddModelError("","Provjeri naziv i odabrane ovlasti.");return Page();}
        try{await permissions.Save(name,description,rights,create);}
        catch(SqlException ex) when(ex.Number is 2601 or 2627 or 51001){ModelState.AddModelError("","Naziv već postoji ili bi promjena uklonila posljednjeg punog administratora.");return Page();}
        return RedirectToPage();
    }
}

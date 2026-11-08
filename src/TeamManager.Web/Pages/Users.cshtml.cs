using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web.Pages;
public class UsersModel(UserStore store):PageModel
{
    public List<DirectoryEntry> Entries {get;set;}=[];
    public List<Team> Units {get;set;}=[];
    public List<Team> Teams {get;set;}=[];
    public List<Team> Projects {get;set;}=[];
    public async Task OnGetAsync(){Entries=await store.Directory();Units=await store.Choices("unit");Teams=await store.Choices("team");Projects=await store.Choices("project");}
    public async Task<IActionResult> OnPostCreateAsync(string? employee,string? name,string? job,int? unit,string? password)
    {
        if(!User.IsInRole("users.manage"))return Forbid();
        employee=employee?.Trim().ToLowerInvariant();name=name?.Trim();job=job?.Trim()??"";
        if(!ModelState.IsValid||!Regex.IsMatch(employee??"",@"^[a-z0-9._-]{1,30}$")||string.IsNullOrWhiteSpace(name)||name.Length>100||job.Length>100||password is null||password.Length<7||password.Length>200)
            return await Invalid("Provjeri broj radnika, ime i lozinku (najmanje 7 znakova).");
        try{await store.Create(employee!,name,job,unit,password);}
        catch(SqlException ex) when(ex.Number is 2601 or 2627 or 547){return await Invalid("Broj radnika već postoji ili organizacijska jedinica nije valjana.");}
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostScopeAsync(string? kind,string? name)
    {
        if(!User.IsInRole("organization.manage"))return Forbid();
        name=name?.Trim();var table=kind switch {"unit"=>"OrganizationUnits","team"=>"Teams","project"=>"Projects",_=>null};
        if(table is null||string.IsNullOrWhiteSpace(name)||name.Length>100)return await Invalid("Unesi valjan naziv do 100 znakova.");
        try{await store.Execute($"INSERT dbo.{table}(Name) VALUES(@name)",P("@name",name));}
        catch(SqlException ex) when(ex.Number is 2601 or 2627){return await Invalid("Taj naziv već postoji.");}
        return RedirectToPage();
    }
    private async Task<IActionResult> Invalid(string message){ModelState.AddModelError("",message);await OnGetAsync();return Page();}
}

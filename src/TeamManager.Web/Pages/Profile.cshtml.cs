using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web.Pages;
public class ProfileModel(UserStore store,Permissions permissions):PageModel
{
    public IActionResult OnPost()=>BadRequest();
    public AppUser? Person {get;set;}
    public List<AccessRole> Roles {get;set;}=[];
    public List<string> GlobalRoles {get;set;}=[];
    public HashSet<int> ManageTeams {get;set;}=[];
    public HashSet<int> ManageProjects {get;set;}=[];
    public List<Team> Units {get;set;}=[];
    public List<Team> TeamChoices {get;set;}=[];
    public List<Team> ProjectChoices {get;set;}=[];
    public List<Membership> Teams {get;set;}=[];
    public List<Membership> Projects {get;set;}=[];
    public bool IsSelf=>Person?.Id.ToString()==User.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool CanEdit=>IsSelf||User.IsInRole("users.manage");
    public string UnitName=>Units.FirstOrDefault(x=>x.Id==Person?.OrganizationUnitId)?.Name??"Nije dodijeljena";
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        var selected=id??int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Person=(await store.Directory()).FirstOrDefault(x=>x.User.Id==selected)?.User;
        if(Person is null)return NotFound();
        Roles=await permissions.Roles();GlobalRoles=await permissions.GlobalRoles(selected);
        Units=await store.Choices("unit");TeamChoices=await store.Choices("team");ProjectChoices=await store.Choices("project");Teams=await store.Memberships(selected,true);Projects=await store.Memberships(selected,false);
        foreach(var t in TeamChoices)if(await permissions.CanManageMembers(User,"team",t.Id))ManageTeams.Add(t.Id);
        foreach(var p in ProjectChoices)if(await permissions.CanManageMembers(User,"project",p.Id))ManageProjects.Add(p.Id);
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(int id,string? name,string? job,int? unit)
    {
        await OnGetAsync(id);if(Person is null)return NotFound();if(!CanEdit)return Forbid();
        name=name?.Trim();job=job?.Trim()??"";
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(name)||name.Length>100||job.Length>100)return Invalid("Unesi ime i funkciju do 100 znakova.");
        if(!User.IsInRole("users.manage"))unit=Person.OrganizationUnitId;
        try{await store.Execute("UPDATE dbo.AppUsers SET DisplayName=@n,JobTitle=@j,OrganizationUnitId=@o WHERE Id=@id",P("@n",name),P("@j",job),P("@o",unit),P("@id",id));}
        catch(SqlException ex) when(ex.Number==547){return Invalid("Organizacijska jedinica nije valjana.");}
        return Success(id,"Podaci su spremljeni.");
    }
    public async Task<IActionResult> OnPostPasswordAsync(int id,string? currentPassword,string? newPassword,string? confirmPassword)
    {
        await OnGetAsync(id);if(Person is null)return NotFound();if(!IsSelf)return Forbid();
        if(newPassword is null||newPassword.Length<10||newPassword.Length>200||newPassword!=confirmPassword||string.IsNullOrEmpty(currentPassword)||currentPassword.Length>200)return Invalid("Nova lozinka mora imati najmanje 10 znakova, a potvrda mora biti jednaka.");
        if(await store.Authenticate(Person.EmployeeNumber,currentPassword) is null)return Invalid("Trenutna lozinka nije ispravna ili je račun privremeno zaključan.");
        await store.ChangePassword(Person,newPassword);return Success(id,"Lozinka je promijenjena.");
    }
    public async Task<IActionResult> OnPostMembershipAsync(int id,string? kind,int scopeId,string? role)
    {
        if(kind is not ("team" or "project")||!await permissions.CanManageMembers(User,kind,scopeId))return Forbid();await OnGetAsync(id);if(Person is null)return NotFound();
        if(!ModelState.IsValid||!Roles.Any(x=>x.Name==role)||scopeId<=0)return Invalid("Odaberi tim ili projekt i valjanu ulogu.");
        // Scope managers cannot delegate permissions they do not possess in that scope.
        var effective=await permissions.Effective(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),kind,scopeId);
        if(!effective.Contains("*")&&Roles.Single(x=>x.Name==role).Permissions.Any(x=>!effective.Contains(x)))return Forbid();
        var sql=kind=="team"?"SET XACT_ABORT ON; BEGIN TRANSACTION; IF EXISTS(SELECT 1 FROM dbo.Members WITH(UPDLOCK,HOLDLOCK) WHERE TeamId=@s AND UserId=@u) UPDATE dbo.Members SET Role=@r WHERE TeamId=@s AND UserId=@u; ELSE INSERT dbo.Members(TeamId,UserId,Name,Role) VALUES(@s,@u,@name,@r); COMMIT;":"SET XACT_ABORT ON; BEGIN TRANSACTION; IF EXISTS(SELECT 1 FROM dbo.ProjectMembers WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId=@s AND UserId=@u) UPDATE dbo.ProjectMembers SET Role=@r WHERE ProjectId=@s AND UserId=@u; ELSE INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@s,@u,@r); COMMIT;";
        try{await store.Execute(sql,P("@s",scopeId),P("@u",id),P("@r",role),P("@name",Person.EmployeeNumber));}
        catch(SqlException ex) when(ex.Number is 547 or 2601 or 2627){return Invalid("Članstvo nije spremljeno. Provjeri postoji li tim/projekt i ima li već člana s istim brojem radnika.");}
        return Success(id,"Članstvo i uloga su spremljeni.");
    }
    public async Task<IActionResult> OnPostRemoveAsync(int id,string? kind,int scopeId)
    {
        if(kind is not ("team" or "project")||!await permissions.CanManageMembers(User,kind,scopeId))return Forbid();await OnGetAsync(id);if(Person is null)return NotFound();
        if(kind is not ("team" or "project"))return BadRequest();
        try{await store.Execute(kind=="team"?"DELETE dbo.Members WHERE TeamId=@s AND UserId=@u":"DELETE dbo.ProjectMembers WHERE ProjectId=@s AND UserId=@u",P("@s",scopeId),P("@u",id));}
        catch(SqlException ex) when(ex.Number==547){return Invalid("Član ima dodijeljene zadatke. Najprije ih preraspodijeli.");}
        return Success(id,"Članstvo je uklonjeno.");
    }
    private IActionResult Invalid(string text){ModelState.AddModelError("",text);return Page();}
    public async Task<IActionResult> OnPostGlobalRoleAsync(int id,string role,bool remove)
    {
        if(!User.IsInRole("roles.manage"))return Forbid();await OnGetAsync(id);if(Person is null)return NotFound();
        if(!Roles.Any(x=>x.Name==role))return Invalid("Nepostojeća rola.");
        try{await permissions.Assign(id,role,remove);}
        catch(SqlException ex) when(ex.Number==51001){return Invalid("Mora ostati barem jedan puni administrator.");}
        return Success(id,"Globalne role su spremljene.");
    }
    private IActionResult Success(int id,string text){TempData["Notice"]=text;return RedirectToPage(new{id});}
}

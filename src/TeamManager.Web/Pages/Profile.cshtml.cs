using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web.Pages;

public class ProfileModel(UserStore store, Permissions permissions, Microsoft.AspNetCore.Identity.IPasswordHasher<AppUser> hasher, OracleWorkspace oracle) : PageModel
{
    public IActionResult OnPost() => BadRequest();
    public AppUser? Person { get; set; }
    public bool HasProfileDraft { get; private set; }
    public string? ProfileDraftName { get; private set; }
    public string? ProfileDraftJob { get; private set; }
    public string ProfileUnitValue { get; private set; } = "";
    public List<AccessRole> Roles { get; set; } = [];
    public List<string> GlobalRoles { get; set; } = [];
    public HashSet<int> ManageTeams { get; set; } = [];
    public HashSet<int> ManageProjects { get; set; } = [];
    public List<Team> Units { get; set; } = [];
    public List<Team> TeamChoices { get; set; } = [];
    public List<Team> ProjectChoices { get; set; } = [];
    public List<Membership> Teams { get; set; } = [];
    public List<Membership> Projects { get; set; } = [];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public bool IsSelf => Person?.Id.ToString() == User.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool CanEdit => IsSelf || User.IsInRole("users.manage");
    public string UnitName => Units.FirstOrDefault(x => x.Id == Person?.OrganizationUnitId)?.Name ?? "Nije dodijeljena";
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        var selected = id ?? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Person = (await store.Directory()).FirstOrDefault(x => x.User.Id == selected)?.User;
        if (Person is null) return NotFound();
        Roles = await permissions.Roles(); GlobalRoles = await permissions.GlobalRoles(selected);
        Units = await store.Choices("unit"); TeamChoices = await store.Choices("team"); ProjectChoices = await store.MembershipProjects(UserId); Teams = await store.Memberships(UserId, selected, true); Projects = await store.Memberships(UserId, selected, false);
        foreach (var t in TeamChoices) if (await permissions.CanManageMembers(User, "team", t.Id)) ManageTeams.Add(t.Id);
        ManageProjects=ProjectChoices.Select(p=>p.Id).ToHashSet();
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(int id, string? name, string? job, int? unit)
    {
        await OnGetAsync(id); if (Person is null) return NotFound(); if (!CanEdit) return Forbid();
        HasProfileDraft = true;
        ProfileDraftName = Request.Form["name"].ToString();
        ProfileDraftJob = Request.Form["job"].ToString();
        ProfileUnitValue = User.IsInRole("users.manage") ? Request.Form["unit"].ToString() : Person.OrganizationUnitId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        name = name?.Trim(); job = job?.Trim() ?? "";
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(name) || name.Length > 100 || job.Length > 100) return Invalid("Unesi ime i funkciju do 100 znakova.");
        if (!User.IsInRole("users.manage")) unit = Person.OrganizationUnitId;
        if (oracle.Enabled)
        {
            try
            {
                await oracle.WriteAccount(UserId, id, async w =>
                {
                    if (UserId != id && !(await OracleWorkspace.Right(w, UserId, "users.manage"))) throw new UnauthorizedAccessException();
                    if (unit is not null && (await w.Find("OrganizationUnits", unit.Value)) is null) throw new ArgumentException("Organizacijska jedinica nije valjana.");
                    var row = (await w.Find("AppUsers", id)) ?? throw new UnauthorizedAccessException();
                    if (unit != OracleWorkspace.Workspace.OptionalInt(row, "OrganizationUnitId") && !(await OracleWorkspace.Right(w, UserId, "users.manage"))) throw new UnauthorizedAccessException();
                    OracleWorkspace.Workspace.Set(row, "DisplayName", name);
                    OracleWorkspace.Workspace.Set(row, "JobTitle", job);
                    OracleWorkspace.Workspace.Set(row, "OrganizationUnitId", unit);
                    await OracleWorkspace.Audit(w, UserId, "account.update", Person.EmployeeNumber); return true;
                });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (ArgumentException ex) { return Invalid(ex.Message); }
            return Success(id, "Podaci su spremljeni.");
        }
        const string sql = "UPDATE dbo.AppUsers SET DisplayName=@n,JobTitle=@j,OrganizationUnitId=@o WHERE Id=@id";
        SqlParameter[] parameters = [P("@n", name), P("@j", job), P("@o", unit), P("@id", id)];
        try
        {
            if (!IsSelf || unit != Person.OrganizationUnitId)
                await store.ExecuteAudited(new(UserId, "account.update", Person.EmployeeNumber), sql, parameters);
            else
                await store.Execute(sql, parameters);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number == 547) { return Invalid("Organizacijska jedinica nije valjana."); }
        return Success(id, "Podaci su spremljeni.");
    }
    public async Task<IActionResult> OnPostPasswordAsync(int id, string? currentPassword, string? newPassword, string? confirmPassword)
    {
        await OnGetAsync(id); if (Person is null) return NotFound(); if (!IsSelf) return Forbid();
        if (newPassword is null || newPassword.Length < 10 || newPassword.Length > 200 || newPassword != confirmPassword || string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 200) return Invalid("Nova lozinka mora imati najmanje 10 znakova, a potvrda mora biti jednaka.");
        var authenticated = await store.Authenticate(Person.EmployeeNumber, currentPassword);
        if (authenticated is null || authenticated.SessionVersion != Person.SessionVersion) return Invalid("Trenutna lozinka nije ispravna ili je račun privremeno zaključan.");
        if (!await store.ChangePassword(authenticated, newPassword))
            return Invalid("Račun je u međuvremenu promijenjen. Ponovno se prijavi.");
        return Success(id, "Lozinka je promijenjena. Ponovno se prijavi.");
    }
    public async Task<IActionResult> OnPostMembershipAsync(int id, string? kind, int scopeId, string? role, bool removeRole = false, bool replacePrimary = false)
    {
        if (kind is not ("team" or "project") || !await permissions.CanManageMembers(User, kind, scopeId)) return Forbid(); await OnGetAsync(id); if (Person is null) return NotFound();
        if (!ModelState.IsValid || !Roles.Any(x => x.Name == role) || scopeId <= 0) return Invalid("Odaberi tim ili projekt i valjanu ulogu.");
        // Scope managers cannot delegate permissions they do not possess in that scope.
        var effective = await permissions.Effective(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), kind, scopeId);
        if (!effective.Contains("*") && Roles.Single(x => x.Name == role).Permissions.Any(x => !effective.Contains(x))) return Forbid();
        if (oracle.Enabled)
        {
            await oracle.Membership(UserId, id, kind, scopeId, role, removeRole, replacePrimary, false);
            return Success(id, "Članstvo i uloga su spremljeni.");
        }
        var sql = kind == "team"
            ? """
              SET XACT_ABORT ON;
              BEGIN TRANSACTION;
              DECLARE @member int, @primaryRole nvarchar(30);
              SELECT @member=Id,@primaryRole=Role FROM dbo.Members WITH(UPDLOCK,HOLDLOCK) WHERE TeamId=@s AND UserId=@u;
              IF @remove=1
                  DELETE dbo.TeamMemberRoles WHERE MemberId=@member AND RoleName=@r;
              ELSE IF @member IS NULL
                  INSERT dbo.Members(TeamId,UserId,Name,Role) VALUES(@s,@u,@name,@r);
              ELSE IF @replace=1
              BEGIN
                  DELETE dbo.TeamMemberRoles WHERE MemberId=@member AND RoleName=@r;
                  UPDATE dbo.Members SET Role=@r WHERE Id=@member;
              END
              ELSE IF @primaryRole<>@r AND NOT EXISTS(SELECT 1 FROM dbo.TeamMemberRoles WHERE MemberId=@member AND RoleName=@r)
                  INSERT dbo.TeamMemberRoles(MemberId,RoleName) VALUES(@member,@r);
              COMMIT;
              """
            : """
              SET XACT_ABORT ON;
              BEGIN TRANSACTION;
              DECLARE @primaryRole nvarchar(30);
              SELECT @primaryRole=Role FROM dbo.ProjectMembers WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId=@s AND UserId=@u;
              IF @remove=1
                  DELETE dbo.ProjectMemberRoles WHERE ProjectId=@s AND UserId=@u AND RoleName=@r;
              ELSE IF @primaryRole IS NULL
                  INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@s,@u,@r);
              ELSE IF @replace=1
              BEGIN
                  DELETE dbo.ProjectMemberRoles WHERE ProjectId=@s AND UserId=@u AND RoleName=@r;
                  UPDATE dbo.ProjectMembers SET Role=@r WHERE ProjectId=@s AND UserId=@u;
              END
              ELSE IF @primaryRole<>@r AND NOT EXISTS(SELECT 1 FROM dbo.ProjectMemberRoles WHERE ProjectId=@s AND UserId=@u AND RoleName=@r)
                  INSERT dbo.ProjectMemberRoles(ProjectId,UserId,RoleName) VALUES(@s,@u,@r);
              COMMIT;
              """;
        try { await store.ExecuteAudited(new(UserId, removeRole ? "membership.role.remove" : replacePrimary ? "membership.role.replace" : "membership.role.add", Person.EmployeeNumber, $"{kind} #{scopeId}: {role}"), sql, P("@s", scopeId), P("@u", id), P("@r", role), P("@name", Person.EmployeeNumber), P("@remove", removeRole), P("@replace", replacePrimary)); }
        catch (SqlException ex) when (ex.Number is 547 or 2601 or 2627) { return Invalid("Članstvo nije spremljeno. Provjeri postoji li tim/projekt i ima li već člana s istim brojem radnika."); }
        return Success(id, "Članstvo i uloga su spremljeni.");
    }
    public async Task<IActionResult> OnPostRemoveAsync(int id, string? kind, int scopeId)
    {
        if (kind is not ("team" or "project") || !await permissions.CanManageMembers(User, kind, scopeId)) return Forbid(); await OnGetAsync(id); if (Person is null) return NotFound();
        if (kind is not ("team" or "project")) return BadRequest();
        if (oracle.Enabled)
        {
            await oracle.Membership(UserId, id, kind, scopeId, null, false, false, true);
            return Success(id, "Članstvo je uklonjeno.");
        }
        try { await store.ExecuteAudited(new(UserId, "membership.remove", Person.EmployeeNumber, $"{kind} #{scopeId}"), kind == "team" ? "DELETE dbo.Members WHERE TeamId=@s AND UserId=@u" : "DELETE dbo.ProjectMembers WHERE ProjectId=@s AND UserId=@u", P("@s", scopeId), P("@u", id)); }
        catch (SqlException ex) when (ex.Number == 547) { return Invalid("Član ima dodijeljene zadatke. Najprije ih preraspodijeli."); }
        return Success(id, "Članstvo je uklonjeno.");
    }
    public async Task<IActionResult> OnPostAccountAsync(int id, string? accountAction, string? password)
    {
        var actorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!User.IsInRole("*") || actorId == id) return Forbid();
        await OnGetAsync(id);
        if (Person is null) return NotFound();

        if (!ModelState.IsValid || accountAction is not ("activate" or "deactivate" or "reset" or "revoke"))
            return Invalid("Odaberi valjanu radnju za račun.");
        if (accountAction == "reset" && (password is null || password.Length is < 10 or > 200))
            return Invalid("Nova lozinka mora imati od 10 do 200 znakova.");

        bool? active = accountAction switch { "activate" => true, "deactivate" => false, _ => null };
        var hash = accountAction == "reset" ? hasher.HashPassword(Person, password!) : null;
        try { await permissions.ManageAccount(actorId, id, active, hash); }
        catch (SqlException ex) when (ex.Number == 51002) { return Forbid(); }
        catch (SqlException ex) when (ex.Number == 51001) { return Invalid("Mora ostati barem jedan aktivan puni administrator."); }
        catch (InvalidOperationException ex) { return Invalid(ex.Message); }
        return Success(id, "Radnja je izvršena. Prethodne sesije su opozvane.");
    }

    private IActionResult Invalid(string text) { ModelState.AddModelError("", text); return Page(); }
    public async Task<IActionResult> OnPostGlobalRoleAsync(int id, string role, bool remove)
    {
        if(!ListFilterValidation.Valid(ModelState,"id","remove"))return BadRequest();
        if (!User.IsInRole("roles.manage")) return Forbid(); await OnGetAsync(id); if (Person is null) return NotFound();
        if (!Roles.Any(x => x.Name == role)) return Invalid("Nepostojeća rola.");
        try { await permissions.Assign(UserId, id, role, remove); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number == 51001) { return Invalid("Mora ostati barem jedan puni administrator."); }
        catch (InvalidOperationException ex) { return Invalid(ex.Message); }
        return Success(id, "Globalne role su spremljene.");
    }
    public async Task<IActionResult> OnPostLanguageAsync(string? language)
    {
        if (language is not ("hr" or "en")) return BadRequest();
        await store.SetLanguage(UserId, language);
        return RedirectToPage(new { id = UserId });
    }

    public async Task<IActionResult> OnPostThemeAsync(string? theme)
    {
        if (theme is not ("light" or "dark" or "system")) return BadRequest();
        await store.SetTheme(UserId, theme);
        return RedirectToPage(new { id = UserId });
    }

    private IActionResult Success(int id, string text) { TempData["Notice"] = text; return RedirectToPage(new { id }); }
}

using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web.Pages;

public class UsersModel(UserStore store, OracleWorkspace oracle) : PageModel
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public List<DirectoryEntry> Entries { get; set; } = [];
    public List<Team> Units { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<Team> Projects { get; set; } = [];
    public bool HasCreateDraft { get; private set; }
    public string? CreateEmployee { get; private set; }
    public string? CreateName { get; private set; }
    public string? CreateJob { get; private set; }
    public string CreateUnitValue { get; private set; } = "";
    public bool HasScopeDraft { get; private set; }
    public string? ScopeName { get; private set; }
    public string? ScopeKind { get; private set; }
    public async Task OnGetAsync() { Entries = await store.Directory(); Units = await store.Choices("unit"); Teams = await store.Choices("team"); Projects = await store.Choices("project"); }
    public async Task<IActionResult> OnPostCreateAsync(string? employee, string? name, string? job, int? unit, string? password)
    {
        if (!User.IsInRole("users.manage")) return Forbid();
        HasCreateDraft = true;
        CreateEmployee = Request.Form["employee"].ToString();
        CreateName = Request.Form["name"].ToString();
        CreateJob = Request.Form["job"].ToString();
        CreateUnitValue = Request.Form["unit"].ToString();
        employee = employee?.Trim().ToLowerInvariant(); name = name?.Trim(); job = job?.Trim() ?? "";
        if (!ModelState.IsValid || !Regex.IsMatch(employee ?? "", @"^[a-z0-9._-]{1,30}$") || string.IsNullOrWhiteSpace(name) || name.Length > 100 || job.Length > 100 || password is null || password.Length < 7 || password.Length > 200)
            return await Invalid("Provjeri broj radnika, ime i lozinku (najmanje 7 znakova).");
        try { await store.Create(employee!, name, job, unit, password, actorId: UserId); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547) { return await Invalid("Broj radnika već postoji ili organizacijska jedinica nije valjana."); }
        catch (ArgumentException) { return await Invalid("Broj radnika već postoji ili organizacijska jedinica nije valjana."); }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostScopeAsync(string? kind, string? name)
    {
        if (!User.IsInRole("organization.manage")) return Forbid();
        HasScopeDraft = true;
        ScopeKind = Request.Form["kind"].ToString();
        ScopeName = Request.Form["name"].ToString();
        name = name?.Trim(); var table = kind switch { "unit" => "OrganizationUnits", "team" => "Teams", "project" => "Projects", _ => null };
        if (table is null || string.IsNullOrWhiteSpace(name) || name.Length > 100) return await Invalid("Unesi valjan naziv do 100 znakova.");
        if (oracle.Enabled)
        {
            try
            {
                if (kind == "project")
                    await oracle.SaveProject(UserId, null, new ProjectInput { Name = name });
                else
                    await oracle.WriteNamedAdministration(UserId, $"organization-name:{kind}:{name.ToUpperInvariant()}", async w =>
                    {
                        if (!(await OracleWorkspace.Right(w, UserId, "organization.manage"))) throw new UnauthorizedAccessException();
                        if ((await w.Rows(table)).Any(r => OracleWorkspace.Workspace.Text(r, "Name").Equals(name, StringComparison.OrdinalIgnoreCase)))
                            throw new ArgumentException("Taj naziv već postoji.");
                        if (kind == "unit") w.Add(table, ("Id", (await w.Next(table))), ("Name", name));
                        else w.Add(table, ("Id", (await w.Next(table))), ("Name", name), ("CreatedAt", DateTime.UtcNow));
                        await OracleWorkspace.Audit(w, UserId, $"organization.{kind}.create", name);
                        return true;
                    });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (ArgumentException ex) { return await Invalid(ex.Message); }
            return RedirectToPage();
        }
        try { await store.ExecuteAudited(new(UserId, $"organization.{kind}.create", name), $"INSERT dbo.{table}(Name) VALUES(@name)", P("@name", name)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return await Invalid("Taj naziv već postoji."); }
        return RedirectToPage();
    }
    private async Task<IActionResult> Invalid(string message) { ModelState.AddModelError("", message); await OnGetAsync(); return Page(); }
}

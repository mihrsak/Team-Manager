using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed record GroupMemberDraft(int GroupId, string UserId);
public sealed record GroupRoleDraft(int GroupId, string Role);

public sealed class GroupsModel(GroupStore groups, UserStore users, Permissions permissions, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public GroupDirectory Directory { get; private set; } = new([], []);
    public List<DirectoryEntry> Users { get; private set; } = [];
    public List<AccessRole> Roles { get; private set; } = [];
    public bool HasCreateDraft { get; private set; }
    public string? CreateName { get; private set; }
    public GroupMemberDraft? MemberDraft { get; private set; }
    public GroupRoleDraft? RoleDraft { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("*")) return Forbid();
        Directory = await groups.List(UserId);
        Users = await users.Directory();
        Roles = await permissions.Roles();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name)
    {
        if (!User.IsInRole("*")) return Forbid();
        HasCreateDraft = true;
        CreateName = Request.Form["name"].ToString();
        name = name?.Trim();
        return await Save(name is { Length: > 0 and <= 100 } ? () => groups.Create(UserId, name) : null);
    }

    public async Task<IActionResult> OnPostMemberAsync(int groupId, int userId, bool remove)
    {
        if (!User.IsInRole("*")) return Forbid();
        MemberDraft = new(groupId, Request.Form["userId"].ToString());
        return await Save(groupId > 0 && userId > 0 ? () => groups.SetMember(UserId, groupId, userId, remove) : null);
    }

    public async Task<IActionResult> OnPostRoleAsync(int groupId, string? role, bool remove)
    {
        if (!User.IsInRole("*")) return Forbid();
        RoleDraft = new(groupId, Request.Form["role"].ToString());
        return await Save(groupId > 0 && role is { Length: > 0 and <= 30 } ? () => groups.SetRole(UserId, groupId, role, remove) : null);
    }

    private async Task<IActionResult> Save(Func<Task>? change)
    {
        if (!User.IsInRole("*")) return Forbid();
        if (ModelState.IsValid && change is not null)
        {
            try
            {
                await change();
                return RedirectToPage();
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (ArgumentException ex) { ModelState.AddModelError("", text[ex.Message]); }
            catch (InvalidOperationException ex) when (ex.Message == "Mora ostati barem jedan aktivan puni administrator.")
            {
                ModelState.AddModelError("", text[ex.Message]);
            }
            catch (SqlException ex) when (ex.Number is 547 or 2601 or 2627)
            {
                ModelState.AddModelError("", text["Naziv već postoji ili odabrana grupa, korisnik ili uloga više nisu dostupni."]);
            }
        }
        else
        {
            ModelState.AddModelError("", text["Provjeri unesene podatke."]);
        }
        return await OnGetAsync();
    }
}

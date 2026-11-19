using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class GroupsModel(GroupStore groups, UserStore users, Permissions permissions, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public GroupDirectory Directory { get; private set; } = new([], []);
    public List<DirectoryEntry> Users { get; private set; } = [];
    public List<AccessRole> Roles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("*")) return Forbid();
        Directory = await groups.List(UserId);
        Users = await users.Directory();
        Roles = await permissions.Roles();
        return Page();
    }

    public Task<IActionResult> OnPostCreateAsync(string? name)
    {
        name = name?.Trim();
        return Save(name is { Length: > 0 and <= 100 } ? () => groups.Create(UserId, name) : null);
    }

    public Task<IActionResult> OnPostMemberAsync(int groupId, int userId, bool remove)
        => Save(groupId > 0 && userId > 0 ? () => groups.SetMember(UserId, groupId, userId, remove) : null);

    public Task<IActionResult> OnPostRoleAsync(int groupId, string? role, bool remove)
        => Save(groupId > 0 && role is { Length: > 0 and <= 30 } ? () => groups.SetRole(UserId, groupId, role, remove) : null);

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
            catch (ArgumentException ex) { ModelState.AddModelError("", text[ex.Message]); }
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

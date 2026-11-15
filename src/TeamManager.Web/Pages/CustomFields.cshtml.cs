using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace TeamManager.Web.Pages;

public sealed class CustomFieldsModel(CustomFieldStore fields) : PageModel
{
    public string Scope { get; private set; } = "";
    public int? EntityId { get; private set; }
    public FieldTarget? Target { get; private set; }
    public List<CustomFieldValue> Fields { get; private set; } = [];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool ManagesUsers => User.IsInRole("users.manage");

    public async Task<IActionResult> OnGetAsync(string? scope, int? entityId)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!CustomFieldStore.ValidScope(scope) && (scope is not null || entityId is not null)) return NotFound();
        Scope = scope ?? "user";
        EntityId = entityId;
        Response.Headers.CacheControl = "private, no-store";
        if (entityId is int id)
        {
            Target = await fields.Target(UserId, ManagesUsers, Scope, id);
            if (Target is null) return NotFound();
        }
        else if (!User.IsInRole("*")) return Forbid();

        Fields = await fields.List(UserId, Scope, entityId, Target);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? scope, string? name)
    {
        if (!User.IsInRole("*")) return Forbid();
        var result = await OnGetAsync(scope, null);
        if (result is not PageResult) return result;
        name = name?.Trim();
        if (name is null or { Length: 0 or > 100 }) return Invalid("Unesi valjan naziv do 100 znakova.");

        try { await fields.Create(UserId, Scope, name); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return Invalid("Taj naziv već postoji."); }
        return RedirectToPage(new { scope = Scope });
    }

    public async Task<IActionResult> OnPostValueAsync(string? scope, int entityId, int fieldId, int? revision, string? fieldValue)
    {
        if (!ModelState.IsValid || !CustomFieldStore.ValidScope(scope) || entityId <= 0 ||
            fieldId <= 0 || revision is null or < 0) return BadRequest();

        try { await fields.Set(UserId, ManagesUsers, scope!, entityId, fieldId, revision.Value, fieldValue); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            var result = await OnGetAsync(scope, entityId);
            if (result is not PageResult) return result;
            return Invalid(ex.Message);
        }
        return RedirectToPage(new { scope, entityId });
    }

    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("", message);
        return Page();
    }
}

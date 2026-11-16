using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class ApiTokensModel(ApiTokenStore tokens) : PageModel
{
    public List<ApiTokenInfo> Tokens { get; private set; } = [];
    public string? NewToken { get; private set; }
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        Tokens = await tokens.List(UserId);
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name)
    {
        name = name?.Trim();
        if (!ModelState.IsValid || name is null or { Length: 0 or > 80 })
        {
            ModelState.AddModelError("", "Unesi naziv tokena do 80 znakova.");
            await OnGetAsync();
            return Page();
        }
        if (!int.TryParse(User.FindFirst("session_version")?.Value, out var version)) return Forbid();
        NewToken = await tokens.Create(UserId, version, name);
        if (NewToken is null) return Forbid();
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        if (id <= 0 || !await tokens.Revoke(UserId,id)) return NotFound();
        return RedirectToPage();
    }
}

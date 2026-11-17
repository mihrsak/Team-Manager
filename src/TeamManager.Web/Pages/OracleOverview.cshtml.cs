using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web.Pages;

public sealed class OracleOverviewModel(OracleOverviewStore store, IConfiguration configuration,
    ILogger<OracleOverviewModel> logger) : PageModel
{
    public IReadOnlyList<OracleOverviewTask> Tasks { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Statuses => OracleOverviewStore.StatusLabels;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!OracleOverviewStore.Enabled(configuration)) return NotFound();
        if (!User.IsInRole("roles.manage")) return Forbid();
        await Load(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, int revision, string? status, CancellationToken cancellationToken)
    {
        if (!OracleOverviewStore.Enabled(configuration)) return NotFound();
        if (!User.IsInRole("roles.manage")) return Forbid();
        if (!ModelState.IsValid || id <= 0 || revision <= 0 || status is null || !Statuses.ContainsKey(status))
            return BadRequest();
        try
        {
            if (await store.SetStatus(id, revision, status, cancellationToken)) return RedirectToPage();
            Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", "Zadatak je promijenjen ili više nije dostupan.");
            await Load(cancellationToken);
        }
        catch (Exception error) when (error is OracleException or InvalidOperationException)
        {
            ReportUnavailable(error);
        }
        return Page();
    }

    private async Task Load(CancellationToken cancellationToken)
    {
        try { Tasks = await store.List(cancellationToken); }
        catch (Exception error) when (error is OracleException or InvalidOperationException)
        {
            ReportUnavailable(error);
        }
    }

    private void ReportUnavailable(Exception error)
    {
        logger.LogWarning(error, "Oracle task overview is unavailable.");
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ModelState.AddModelError("", "Oracle pregled trenutačno nije dostupan.");
    }
}

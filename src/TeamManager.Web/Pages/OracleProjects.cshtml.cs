using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web.Pages;

public sealed class OracleProjectsModel(OracleProjectImport store, IConfiguration configuration,
    Permissions permissions, ILogger<OracleProjectsModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    public List<ImportedProject> Projects { get; private set; } = [];
    public List<ImportedIssue> Issues { get; private set; } = [];
    public bool Unavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken token)
    {
        if (!OracleProjectImport.Enabled(configuration)) return NotFound();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !(await permissions.Effective(userId)).Contains("*"))
            return Forbid();

        try
        {
            (Projects, Issues) = await store.List(ProjectId, token);
            if (ProjectId is not null && !Projects.Any(project => project.Id == ProjectId))
                return NotFound();
        }
        catch (Exception error) when (error is OracleException or InvalidOperationException)
        {
            logger.LogWarning(error, "Imported Oracle projects are unavailable.");
            Unavailable = true;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        return Page();
    }
}

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public class IndexModel(TeamStore store, ILogger<IndexModel> logger) : PageModel
{
    public int Teams { get; private set; }
    public int Members { get; private set; }
    public List<WorkItem> Tasks { get; private set; } = [];
    public string? Error { get; private set; }
    public async Task OnGetAsync()
    {
        try
        {
            var data = await store.Load();
            Teams = data.Teams.Count;
            Members = data.Members.Count;
            Tasks = data.Tasks;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database connectivity check failed");
            Error = "Veza s bazom nije uspjela. Provjerite SQL Server i pokrenite Initialize-Local.ps1.";
            Response.StatusCode = 503;
        }
    }
}

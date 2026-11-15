using System.Globalization;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class IssuesModel(IssueStore store,WorkflowStore workflow):PageModel
{
    [BindProperty(SupportsGet=true)]public int? ProjectId{get;set;}
    [BindProperty(SupportsGet=true)]public string? Status{get;set;}
    [BindProperty(SupportsGet=true)]public string? Query{get;set;}
    public List<Team> Projects{get;set;}=[];
    public List<string> Statuses{get;set;}=[];
    public HashSet<int> CanCreate{get;set;}=[];
    public List<IssueRow> Items{get;set;}=[];
    public HashSet<int> CanEdit {get;set;}=[];
    public List<string> Priorities {get;set;}=[];
    public async Task OnGetAsync(){Priorities=await workflow.Values("priority");Statuses=await workflow.Values("status");var user=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);Projects=await store.Projects(user);foreach(var p in Projects)if(await store.Can(user,p.Id,"issues.create"))CanCreate.Add(p.Id);foreach(var p in Projects)if(await store.Can(user,p.Id,"issues.edit"))CanEdit.Add(p.Id);Items=await store.List(user,ProjectId,Status,Query);}

    public async Task<IActionResult> OnPostBulkAsync(string[] selection, string? bulkStatus, string? bulkPriority)
    {
        var revisions = new Dictionary<int, int>();
        foreach (var item in selection)
        {
            var parts = item.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || id < 1 ||
                !int.TryParse(parts[1], out var revision) || revision < 0 || !revisions.TryAdd(id, revision))
                return BadRequest();
        }
        if (!ModelState.IsValid) return BadRequest();

        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await store.UpdateMany(user, revisions, bulkStatus, bulkPriority);
            TempData["Notice"] = $"Spremljene su promjene za {revisions.Count} zadataka.";
            return RedirectToPage(new { ProjectId, Status, Query });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("", ex.Message);
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        if (!ModelState.IsValid) return BadRequest();
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var rows = await store.List(user, ProjectId, Status, Query);
        var csv = new StringBuilder("\uFEFFId,Naslov,Projekt,Status,Prioritet,Nositelj,Rok\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',', row.Id.ToString(CultureInfo.InvariantCulture), CsvCell(row.Title),
                CsvCell(row.Project), CsvCell(row.Status), CsvCell(row.Priority), CsvCell(row.Assignee),
                row.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
            csv.Append("\r\n");
        }
        Response.Headers.CacheControl = "private, no-store";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "zadaci.csv");
    }

    private static string CsvCell(string? value)
    {
        if (value is null) return "\"\"";

        var trimmed = value.TrimStart();
        if ((value.Length > 0 && value[0] is '\t' or '\r' or '\n') ||
            (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'))
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

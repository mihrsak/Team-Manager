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
    public async Task OnGetAsync(){Statuses=await workflow.Values("status");var user=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);Projects=await store.Projects(user);foreach(var p in Projects)if(await store.Can(user,p.Id,"issues.create"))CanCreate.Add(p.Id);Items=await store.List(user,ProjectId,Status,Query);}

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

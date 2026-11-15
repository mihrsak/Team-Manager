using System.Globalization;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public class IssuesModel(IssueStore store,WorkflowStore workflow,SavedIssueQueryStore savedQueries,Microsoft.Extensions.Localization.IStringLocalizer<UiText> text):PageModel
{
    [BindProperty(SupportsGet=true)]public int? ProjectId{get;set;}
    [BindProperty(SupportsGet=true)]public string? Status{get;set;}
    [BindProperty(SupportsGet=true)]public string? Query{get;set;}
    [BindProperty(SupportsGet=true)] public string? SortOrder { get; set; }
    [BindProperty(SupportsGet=true)] public string? GroupBy { get; set; }
    [BindProperty(SupportsGet=true)] public string? Columns { get; set; }
    [BindProperty(SupportsGet=true)] public string[] Column { get; set; } = [];

    public static readonly Dictionary<string, string> ColumnLabels = new()
    {
        ["id"] = "#", ["project"] = "Projekt", ["status"] = "Status",
        ["priority"] = "Prioritet", ["assignee"] = "Nositelj", ["due"] = "Rok"
    };
    public static readonly Dictionary<string, string> SortLabels = new()
    {
        ["newest"] = "Najnoviji prvo", ["oldest"] = "Najstariji prvo",
        ["title"] = "Naslov A–Ž", ["due"] = "Najbliži rok"
    };
    public static readonly Dictionary<string, string> GroupLabels = new()
    {
        ["project"] = "Projekt", ["status"] = "Status", ["priority"] = "Prioritet", ["assignee"] = "Nositelj"
    };

    public HashSet<string> VisibleColumns { get; private set; } = [];
    public IEnumerable<IGrouping<string, IssueRow>> Groups => Items.GroupBy(GroupName);
    public Microsoft.AspNetCore.Routing.RouteValueDictionary IssueRoute => new()
    {
        ["ProjectId"] = ProjectId, ["Status"] = Status, ["Query"] = Query,
        ["SortOrder"] = SortOrder, ["GroupBy"] = GroupBy, ["Columns"] = Columns
    };

    public List<Team> Projects{get;set;}=[];
    public List<string> Statuses{get;set;}=[];
    public HashSet<int> CanCreate{get;set;}=[];
    public List<IssueRow> Items{get;set;}=[];
    public HashSet<int> CanEdit {get;set;}=[];
    public List<string> Priorities {get;set;}=[];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public List<SavedIssueQuery> SavedQueries { get; private set; } = [];
    public async Task OnGetAsync()
    {
        NormalizeLayout();
        SavedQueries = await savedQueries.List(UserId);
        Priorities = await workflow.Values("priority");
        Statuses = await workflow.Values("status");
        Projects = await store.Projects(UserId);
        foreach (var project in Projects)
        {
            if (await store.Can(UserId, project.Id, "issues.create")) CanCreate.Add(project.Id);
            if (await store.Can(UserId, project.Id, "issues.edit")) CanEdit.Add(project.Id);
        }
        Items = Sort(await store.List(UserId, ProjectId, Status, Query)).ToList();
    }

    private void NormalizeLayout()
    {
        SortOrder = SortOrder is not null && SortLabels.ContainsKey(SortOrder) ? SortOrder : null;
        GroupBy = GroupBy is not null && GroupLabels.ContainsKey(GroupBy) ? GroupBy : null;
        var specified = Column.Length > 0 ? Column : Columns?.Split(',');
        VisibleColumns = specified is null
            ? ColumnLabels.Keys.ToHashSet()
            : specified.Where(ColumnLabels.ContainsKey).ToHashSet();
        Columns = specified is null ? null : string.Join(",", new[] { "title" }.Concat(ColumnLabels.Keys.Where(VisibleColumns.Contains)));
    }

    private IEnumerable<IssueRow> Sort(List<IssueRow> rows) => SortOrder switch
    {
        "oldest" => rows.OrderBy(row => row.Id),
        "title" => rows.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
        "due" => rows.OrderBy(row => row.DueDate ?? DateTime.MaxValue),
        _ => rows
    };

    private string GroupName(IssueRow row) => GroupBy switch
    {
        "project" => row.Project,
        "status" => row.Status,
        "priority" => row.Priority,
        "assignee" => row.Assignee is null ? "Nije dodijeljen" : $"{row.Assignee} ({row.AssigneeEmployeeNumber})",
        _ => ""
    };

    public static string? ColumnText(IssueRow row, string column) => column switch
    {
        "id" => row.Id.ToString(CultureInfo.InvariantCulture),
        "project" => row.Project,
        "status" => row.Status,
        "priority" => row.Priority,
        "assignee" => row.Assignee ?? "Nije dodijeljen",
        "due" => row.DueDate?.ToString("dd.MM.yyyy"),
        _ => null
    };

    public async Task<IActionResult> OnGetSavedAsync(int id)
    {
        var saved = (await savedQueries.List(UserId)).Find(query => query.Id == id);
        if (saved is null) return NotFound();
        return RedirectToPage(new { saved.ProjectId, saved.Status, Query = saved.SearchText, saved.SortOrder, saved.GroupBy, saved.Columns });
    }

    public async Task<IActionResult> OnPostSaveQueryAsync(string? name)
    {
        name = name?.Trim();
        if (!ModelState.IsValid || name is null or { Length: 0 or > 80 } ||
            Query is { Length: > 200 } || Status is { Length: > 30 })
            return await InvalidQuery("Provjeri naziv i filtre upita.");
        if (ProjectId is int project && !await store.Can(UserId, project, "issues.view"))
            return NotFound();
        if (Status is not null && !(await workflow.Values("status")).Contains(Status))
            return await InvalidQuery("Odaberi valjan status.");

        NormalizeLayout();
        try { await savedQueries.Create(UserId, name, ProjectId, Status, Query, SortOrder, GroupBy, Columns); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547)
        {
            return await InvalidQuery("Upit s tim nazivom već postoji ili odabrani filtar više nije dostupan.");
        }
        TempData["Notice"] = "Upit je spremljen.";
        return RedirectToPage(IssueRoute);
    }

    public async Task<IActionResult> OnPostDeleteQueryAsync(int id)
    {
        if (!await savedQueries.Delete(UserId, id)) return NotFound();
        TempData["Notice"] = "Spremljeni upit je uklonjen.";
        return RedirectToPage(IssueRoute);
    }

    private async Task<IActionResult> InvalidQuery(string message)
    {
        ModelState.AddModelError("", message);
        await OnGetAsync();
        return Page();
    }

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
            TempData["Notice"] = text["Spremljene su promjene za {0} zadataka.", revisions.Count].Value;
            return RedirectToPage(IssueRoute);
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
        NormalizeLayout();
        foreach (var row in Sort(rows))
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

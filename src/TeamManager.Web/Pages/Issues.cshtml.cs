using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public class IssuesModel(IssueStore store,WorkflowStore workflow,SavedIssueQueryStore savedQueries,Microsoft.Extensions.Localization.IStringLocalizer<UiText> text):PageModel
{
    public bool HasBulkDraft {get;private set;}
    public string? BulkStatus {get;private set;}
    public string? BulkPriority {get;private set;}
    public List<(int Id,string Title,int Revision,int CurrentRevision,string Status,string Priority)> BulkDraftItems {get;}=[];
    public bool BulkConflict => BulkDraftItems.Any(i=>i.Revision!=i.CurrentRevision);
    [BindProperty(SupportsGet=true)]public int? ProjectId{get;set;}
    [BindProperty(SupportsGet=true), StringLength(30)]public string? Status{get;set;}
    [BindProperty(SupportsGet=true), StringLength(200)]public string? Query{get;set;}
    [BindProperty(SupportsGet=true)] public string? SortOrder { get; set; }
    [BindProperty(SupportsGet=true)] public string? GroupBy { get; set; }
    [BindProperty(SupportsGet=true)] public string? Columns { get; set; }
    [BindProperty(SupportsGet=true)] public string[] Column { get; set; } = [];
    [BindProperty(SupportsGet=true)] public int PageNumber {get;set;}=1;
    public int TotalCount {get;private set;}
    public int PageCount {get;private set;}=1;
    public string CurrentListUrl => Url.Page("/Issues",IssueRoute)!;
    public string PageUrl(int page) {var route=IssueRoute;route["PageNumber"]=page;return Url.Page("/Issues",route)!;}

    public static readonly Dictionary<string, string> ColumnLabels = new()
    {
        ["id"] = "#", ["project"] = "Projekt", ["status"] = "Status",
        ["priority"] = "Prioritet", ["assignee"] = "Nositelj", ["due"] = "Rok"
    };
    public static readonly Dictionary<string, string> SortLabels = new()
    {
        ["newest"] = "Najnoviji prvo", ["oldest"] = "Najstariji prvo",
        ["title"] = "Naslov A–Ž", ["due"] = "Najbliži rok", ["priority"]="Prioritet"
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
        ["SortOrder"] = SortOrder, ["GroupBy"] = GroupBy, ["Columns"] = Columns,["PageNumber"]=PageNumber
    };

    public List<Team> Projects{get;set;}=[];
    public List<string> Statuses{get;set;}=[];
    public HashSet<int> CanCreate{get;set;}=[];
    public List<IssueRow> Items{get;set;}=[];
    public HashSet<int> CanEdit {get;set;}=[];
    public List<string> Priorities {get;set;}=[];
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public List<SavedIssueQuery> SavedQueries { get; private set; } = [];
    public bool FiltersValid=>ListFilterValidation.Valid(ModelState,"ProjectId","Status","Query","PageNumber");
    public bool InvalidProjectFilter=>!ListFilterValidation.Valid(ModelState,"ProjectId");
    public bool InvalidStatusFilter=>!ListFilterValidation.Valid(ModelState,"Status");
    public string ProjectFilterValue=>ListFilterValidation.Value(ModelState,"ProjectId",ProjectId?.ToString(CultureInfo.InvariantCulture));
    public string StatusFilterValue=>ListFilterValidation.Value(ModelState,"Status",Status);

    private async Task<IActionResult?> LoadFilters()
    {
        NormalizeLayout();
        SavedQueries = await savedQueries.List(UserId);
        Priorities = await workflow.Values("priority");
        Statuses = await workflow.Values("status");
        Projects = await store.Projects(UserId);
        ListFilterValidation.SingleValues(Request,ModelState,"ProjectId","Status","Query","PageNumber");
        if(ProjectId is int selected && !Projects.Any(p=>p.Id==selected))return NotFound();
        if(Query?.Length>200)ModelState.TryAddModelError(nameof(Query),"Pretraga može imati najviše 200 znakova.");
        if(Status is {Length:>30})ModelState.TryAddModelError(nameof(Status),"Odaberi valjan status.");
        if(Status is not null && !Statuses.Contains(Status))ModelState.TryAddModelError(nameof(Status),"Odaberi valjan status.");
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var error=await LoadFilters();if(error is not null)return error;
        if(!FiltersValid)return Page();
        Items = Sort(await store.List(UserId, ProjectId, Status, Query)).ToList();
        TotalCount=Items.Count;PageCount=Math.Max(1,(TotalCount+49)/50);PageNumber=Math.Clamp(PageNumber,1,PageCount);
        Items=Items.Skip((PageNumber-1)*50).Take(50).ToList();
        foreach (var project in Projects.Where(p=>p.Id==ProjectId || Items.Any(i=>i.ProjectId==p.Id)))
        {
            if (await store.Can(UserId, project.Id, "issues.create")) CanCreate.Add(project.Id);
            if (await store.Can(UserId, project.Id, "issues.edit")) CanEdit.Add(project.Id);
        }
        return Page();
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
        "priority" => rows.OrderBy(row=>Priorities.IndexOf(row.Priority) is int index&&index>=0?index:int.MaxValue).ThenByDescending(row=>row.Id),
        _ => rows
    };

    private string GroupName(IssueRow row) => GroupBy switch
    {
        "project" => row.Project,
        "status" => row.Status,
        "priority" => row.Priority,
        "assignee" => row.Assignee is null ? text["Nije dodijeljen"].Value : $"{row.Assignee} ({row.AssigneeEmployeeNumber})",
        _ => ""
    };

    public static string? ColumnText(IssueRow row, string column) => column switch
    {
        "id" => row.Id.ToString(CultureInfo.InvariantCulture),
        "project" => row.Project,
        "status" => row.Status,
        "priority" => row.Priority,
        "assignee" => row.Assignee ?? "Nije dodijeljen",
        "due" => row.DueDate?.ToString("d"),
        _ => null
    };

    public async Task<IActionResult> OnGetSavedAsync(int id)
    {
        var saved = (await savedQueries.List(UserId)).Find(query => query.Id == id);
        if (saved is null) return NotFound();
        ProjectId=saved.ProjectId;Status=saved.Status;Query=saved.SearchText;SortOrder=saved.SortOrder;GroupBy=saved.GroupBy;Columns=saved.Columns;
        var error=await LoadFilters();if(error is not null)return error;
        if(!FiltersValid)return Page();
        return RedirectToPage(new { saved.ProjectId, saved.Status, Query = saved.SearchText, saved.SortOrder, saved.GroupBy, saved.Columns });
    }

    public async Task<IActionResult> OnPostSaveQueryAsync(string? name)
    {
        var filterError=await LoadFilters();if(filterError is not null)return filterError;
        if(!FiltersValid)return Page();
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
        catch (ArgumentException) { return await InvalidQuery("Upit s tim nazivom već postoji ili odabrani filtar više nije dostupan."); }
        TempData["Notice"] = "Upit je spremljen.";
        return RedirectToPage(IssueRoute);
    }

    public async Task<IActionResult> OnPostDeleteQueryAsync(int id)
    {
        var error=await LoadFilters();if(error is not null)return error;
        if(!FiltersValid)return Page();
        if (!await savedQueries.Delete(UserId, id)) return NotFound();
        TempData["Notice"] = "Spremljeni upit je uklonjen.";
        return RedirectToPage(IssueRoute);
    }

    private async Task<IActionResult> InvalidQuery(string message)
    {
        ModelState.AddModelError("", message);
        return await OnGetAsync();
    }

    public async Task<IActionResult> OnPostBulkAsync(string[] selection, string? bulkStatus, string? bulkPriority, bool resolveConflict=false, string[]? reviewedSelection=null)
    {
        var error=await LoadFilters();if(error is not null)return error;
        if(!FiltersValid)return Page();
        var revisions = new Dictionary<int, int>();
        foreach (var item in selection)
        {
            var parts = item.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || id < 1 ||
                !int.TryParse(parts[1], out var revision) || revision < 0 || !revisions.TryAdd(id, revision))
                return BadRequest();
        }
        if (!ModelState.IsValid) return BadRequest();
        if(revisions.Count>100)return BadRequest();
        if(resolveConflict)
        {
            var reviewed=new Dictionary<int,int>();
            foreach(var item in reviewedSelection??[])
            {
                var parts=item.Split(':',2);
                if(parts.Length!=2||!int.TryParse(parts[0],out var id)||!int.TryParse(parts[1],out var rev)||rev<0)return BadRequest();
                if(!revisions.ContainsKey(id))continue;
                if(!reviewed.TryAdd(id,rev))return BadRequest();
            }
            if(reviewed.Count!=revisions.Count)return BadRequest();
            revisions=reviewed;
        }

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
            var result=await OnGetAsync();
            if(result is not PageResult)return result;
            HasBulkDraft=true;BulkStatus=Request.Form["bulkStatus"].ToString();BulkPriority=Request.Form["bulkPriority"].ToString();
            foreach(var selected in revisions)
            {
                var issue=await store.Get(UserId,selected.Key);
                if(issue is null||!await store.Can(UserId,issue.ProjectId,"issues.edit"))continue;
                BulkDraftItems.Add((selected.Key,issue.Input.Title,selected.Value,issue.Input.Revision,issue.Input.Status,issue.Input.Priority));
            }
            return Page();
        }
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        var error=await LoadFilters();if(error is not null)return error;
        if (!ModelState.IsValid || !FiltersValid) return BadRequest();
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var rows = await store.List(user, ProjectId, Status, Query);
        Priorities=await workflow.Values("priority");
        var csv = new StringBuilder("\uFEFF");
        csv.AppendJoin(',',new[]{"Id","Naslov","Projekt","Status","Prioritet","Nositelj","Rok"}.Select(label=>text[label].Value));
        csv.Append("\r\n");
        NormalizeLayout();
        foreach (var row in Sort(rows))
        {
            csv.AppendJoin(',', row.Id.ToString(CultureInfo.InvariantCulture), CsvCell(row.Title),
                CsvCell(row.Project), CsvCell(row.Status), CsvCell(row.Priority), CsvCell(row.Assignee),
                row.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
            csv.Append("\r\n");
        }
        Response.Headers.CacheControl = "private, no-store";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"issues.csv":"zadaci.csv");
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

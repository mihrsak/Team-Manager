using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed record SearchHit(string Title, string Detail, string Url);

public sealed class SearchModel(ProjectStore projects, IssueStore issues, RoadmapStore roadmap, UserStore users,ProjectContentSearchStore content) : PageModel
{
    [BindProperty(SupportsGet = true), StringLength(200)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }
    [BindProperty(SupportsGet=true)] public int PageNumber {get;set;}=1;
    public HashSet<string> More {get;}=[];
    public static readonly Dictionary<string,string> ModuleLabels=new(){["projects"]="Projekti",["issues"]="Zadaci",["roadmap"]="Verzije i prekretnice",["users"]="Korisnici",["wiki"]="Wiki",["documents"]="Dokumenti",["news"]="Novosti",["forum"]="Forum"};
    public string ResultsUrl(string label,int page)=>Url.Page("/Search",new{Query,Module=ModuleLabels.Single(p=>p.Value==label).Key,PageNumber=page})!;
    private void Add(string label,IEnumerable<SearchHit> hits,bool paged=false)
    {
        var rows=(paged?hits:hits.Skip((PageNumber-1)*20)).Take(21).ToList();
        if(rows.Count>20)More.Add(label);Results[label]=rows.Take(20).ToList();
    }

    public Dictionary<string, List<SearchHit>> Results { get; } = new();
    public bool Searched { get; private set; }

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = Query?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(query)) return;
        PageNumber=Math.Clamp(PageNumber,1,1000);
        if (Module is not null && !ModuleLabels.ContainsKey(Module))
        {
            ModelState.AddModelError(nameof(Module), "Odaberi valjan modul.");
            return;
        }

        Searched = true;
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (Module is null or "projects" or "roadmap")
        {
            var visible = await projects.List(user);
            if (Module is null or "projects")
                Add("Projekti",visible.Where(project =>
                    Contains(project.Input.Name, query) || Contains(project.Input.Description, query))
                    .Select(project => new SearchHit(project.Input.Name, "", Url.Page("/Project", new { id = project.Id })!))
                    .ToList());

            if (Module is null or "roadmap")
                Add("Verzije i prekretnice",(await roadmap.Search(visible.Select(project => project.Id), query,PageNumber,21))
                    .Select(item => new SearchHit(item.Name, item.Project,
                        Url.Page("/Roadmap", new { projectId = item.ProjectId }) + $"#plan-{item.Id}"))
                    .ToList(),true);
        }

        if (Module is null or "issues")
            Add("Zadaci",(await issues.List(user, null, null, query))
                .Select(issue => new SearchHit(issue.Title, issue.Project, Url.Page("/Issue", new { id = issue.Id })!))
                .ToList());

        if (Module is null or "users")
            Add("Korisnici",(await users.Search(query,PageNumber,21))
                .Select(person => new SearchHit(person.Name, person.EmployeeNumber +
                    (person.JobTitle.Length == 0 ? "" : $" · {person.JobTitle}"), Url.Page("/Profile", new { id = person.Id })!))
                .ToList(),true);
        foreach(var kind in new[]{"wiki","documents","news","forum"}.Where(kind=>Module is null||Module==kind))
        {
            var hits=await content.Search(user,query,kind,(PageNumber-1)*20,21);
            var label=ModuleLabels[kind];if(hits.Count>20)More.Add(label);
            Results[label]=hits.Take(20).Select(hit=>new SearchHit(hit.Title,hit.Project,hit.Module switch{
                "wiki"=>Url.Page("/Wiki",new{projectId=hit.ProjectId,wikiId=hit.Id})!,
                "documents"=>Url.Page("/ProjectDocuments",new{projectId=hit.ProjectId})!,
                "news"=>Url.Page("/ProjectNews",new{projectId=hit.ProjectId,newsId=hit.Id})!,
                _=>Url.Page("/Forum",new{projectId=hit.ProjectId,topicId=hit.Id})!
            })).ToList();
        }
    }

    private static bool Contains(string value, string query)
        => CultureInfo.CurrentCulture.CompareInfo.IndexOf(value, query, CompareOptions.IgnoreCase) >= 0;
}

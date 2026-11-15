using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed record SearchHit(string Title, string Detail, string Url);

public sealed class SearchModel(ProjectStore projects, IssueStore issues, RoadmapStore roadmap, UserStore users) : PageModel
{
    [BindProperty(SupportsGet = true), StringLength(200)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }

    public Dictionary<string, List<SearchHit>> Results { get; } = new();
    public bool Searched { get; private set; }

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = Query?.Trim();
        if (!ModelState.IsValid || string.IsNullOrEmpty(query)) return;
        if (Module is not (null or "projects" or "issues" or "roadmap" or "users"))
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
                Results["Projekti"] = visible.Where(project =>
                    Contains(project.Input.Name, query) || Contains(project.Input.Description, query))
                    .Take(20)
                    .Select(project => new SearchHit(project.Input.Name, "", Url.Page("/Project", new { id = project.Id })!))
                    .ToList();

            if (Module is null or "roadmap")
                Results["Verzije i prekretnice"] = (await roadmap.Search(visible.Select(project => project.Id), query))
                    .Select(item => new SearchHit(item.Name, item.Project,
                        Url.Page("/Roadmap", new { projectId = item.ProjectId }) + $"#plan-{item.Id}"))
                    .ToList();
        }

        if (Module is null or "issues")
            Results["Zadaci"] = (await issues.List(user, null, null, query)).Take(20)
                .Select(issue => new SearchHit(issue.Title, issue.Project, Url.Page("/Issue", new { id = issue.Id })!))
                .ToList();

        if (Module is null or "users")
            Results["Korisnici"] = (await users.Search(query))
                .Select(person => new SearchHit(person.Name, person.EmployeeNumber +
                    (person.JobTitle.Length == 0 ? "" : $" · {person.JobTitle}"), Url.Page("/Profile", new { id = person.Id })!))
                .ToList();
    }

    private static bool Contains(string value, string query)
        => CultureInfo.CurrentCulture.CompareInfo.IndexOf(value, query, CompareOptions.IgnoreCase) >= 0;
}

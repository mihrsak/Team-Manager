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
}

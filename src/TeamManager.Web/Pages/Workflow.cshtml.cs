using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public class WorkflowModel(WorkflowStore store,Permissions permissions):PageModel
{
    public List<string> Types{get;set;}=[];public List<string> Statuses{get;set;}=[];public List<string> Priorities{get;set;}=[];
    public List<AccessRole> Roles{get;set;}=[];public List<Transition> Transitions{get;set;}=[];
    public async Task<IActionResult> OnGetAsync(){if(!User.IsInRole("workflow.manage"))return Forbid();Types=await store.Values("type");Statuses=await store.Values("status");Priorities=await store.Values("priority");Roles=await permissions.Roles();Transitions=await store.Transitions();return Page();}
    public async Task<IActionResult> OnPostValueAsync(string kind,string name,bool closed)
    {
        if(!User.IsInRole("workflow.manage"))return Forbid();
        try{await store.AddValue(kind,name,closed);return RedirectToPage();}catch(Exception ex) when(ex is ArgumentException||ex is SqlException {Number:2601 or 2627}){await OnGetAsync();ModelState.AddModelError("","Naziv nije valjan ili već postoji.");return Page();}
    }
    public async Task<IActionResult> OnPostTransitionAsync(string role,string type,string from,string to,bool remove)
    {
        if(!User.IsInRole("workflow.manage"))return Forbid();await OnGetAsync();
        if(!Roles.Any(r=>r.Name==role)||!Types.Contains(type)||!Statuses.Contains(from)||!Statuses.Contains(to)||from==to){ModelState.AddModelError("","Odaberi valjanu rolu, tip i različite statuse.");return Page();}
        try{await store.Set(new(role,type,from,to),remove);return RedirectToPage();}catch(SqlException ex) when(ex.Number is 2601 or 2627){ModelState.AddModelError("","Prijelaz već postoji.");return Page();}
    }
}

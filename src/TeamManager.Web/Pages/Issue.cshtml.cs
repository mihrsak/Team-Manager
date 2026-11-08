using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class IssueModel(IssueStore store,WorkflowStore workflow):PageModel
{
    [BindProperty]public IssueInput Input{get;set;}=new();
    public int? Id{get;set;}
    public int ProjectId{get;set;}
    public string ProjectName{get;set;}="";
    public List<Team> Assignees{get;set;}=[];
    public List<string> Types{get;set;}=[];public List<string> Statuses{get;set;}=[];public List<string> Priorities{get;set;}=[];
    public List<IssueEvent> Events{get;set;}=[];
    public List<TimeEntry> Time{get;set;}=[];
    public bool CanEdit{get;set;}
    public bool CanComment{get;set;}
    public bool CanLogTime{get;set;} public bool ShowTime{get;set;}
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int? id,int projectId)
    {
        Id=id;
        Types=await workflow.Values("type");Statuses=await workflow.Values("status");Priorities=await workflow.Values("priority");
        if(id is not null){var issue=await store.Get(UserId,id.Value);if(issue is null)return NotFound();ProjectId=issue.ProjectId;Input=issue.Input;var activity=await store.Activity(UserId,id.Value);Events=activity.Events;Time=activity.Time;}
        else ProjectId=projectId;
        var project=(await store.Projects(UserId)).FirstOrDefault(x=>x.Id==ProjectId);if(project is null)return NotFound();ProjectName=project.Name;Assignees=await store.Assignees(ProjectId);
        CanEdit=await store.Can(UserId,ProjectId,id is null?"issues.create":"issues.edit");CanComment=await store.Can(UserId,ProjectId,"issues.comment");ShowTime=await store.Can(UserId,ProjectId,"time.view");CanLogTime=await store.Can(UserId,ProjectId,"time.log");if(id is null&&!CanEdit)return Forbid();return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(int? id,int projectId)
    {
        var submitted=Input;var result=await OnGetAsync(id,projectId);if(result is not PageResult)return result;Input=submitted;
        if(!CanEdit)return Forbid();if(!ModelState.IsValid)return Page();
        try{var saved=await store.Save(UserId,ProjectId,id,Input);TempData["Notice"]="Zadatak je spremljen.";return RedirectToPage(new{id=saved});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostCommentAsync(int id,string? text)
    {
        ModelState.Clear();var result=await OnGetAsync(id,0);if(result is not PageResult)return result;
        try{await store.Comment(UserId,id,text??"");return RedirectToPage(new{id});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostTimeAsync(int id,DateTime? date,string? hours,string? note)
    {
        var parsed=decimal.TryParse(hours?.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value);
        var valid=date is not null&&parsed&&!ModelState.Any(x=>x.Key=="date" && x.Value!.Errors.Count>0);
        ModelState.Clear();var result=await OnGetAsync(id,0);if(result is not PageResult)return result;if(!valid){ModelState.AddModelError("","Unesi valjan datum i broj sati.");return Page();}
        try{await store.LogTime(UserId,id,date!.Value,value,note);return RedirectToPage(new{id});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public IActionResult OnPost()=>BadRequest();
}

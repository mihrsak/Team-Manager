using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class IssueModel(IssueStore store,WorkflowStore workflow,IssueLinkStore links,RoadmapStore roadmap):PageModel
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
    public List<RoadmapItem> RoadmapItems {get;set;}=[];
    public List<IssueLink> Links {get;set;}=[];
    public List<Team> CopyProjects {get;set;}=[];
    public List<Team> MoveProjects {get;set;}=[];
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int? id,int projectId)
    {
        Id=id;
        Types=await workflow.Values("type");Statuses=await workflow.Values("status");Priorities=await workflow.Values("priority");
        if(id is not null){var issue=await store.Get(UserId,id.Value);if(issue is null)return NotFound();ProjectId=issue.ProjectId;Input=issue.Input;var activity=await store.Activity(UserId,id.Value);Events=activity.Events;Time=activity.Time;}
        else ProjectId=projectId;
        var project=(await store.Projects(UserId)).FirstOrDefault(x=>x.Id==ProjectId);if(project is null)return NotFound();ProjectName=project.Name;Assignees=await store.Assignees(ProjectId);
        RoadmapItems = await roadmap.List(ProjectId);
        CanEdit=await store.Can(UserId,ProjectId,id is null?"issues.create":"issues.edit");CanComment=await store.Can(UserId,ProjectId,"issues.comment");ShowTime=await store.Can(UserId,ProjectId,"time.view");CanLogTime=await store.Can(UserId,ProjectId,"time.log");if(id is null&&!CanEdit)return Forbid();
        if (id is not null)
        {
            Links = await links.List(UserId, id.Value, ProjectId);
            var canMove = CanEdit && await store.CanMove(UserId, ProjectId);
            foreach (var target in await store.Projects(UserId, "issues.create"))
            {
                if (!await store.Can(UserId, target.Id, "issues.view")) continue;
                CopyProjects.Add(target);
                if (canMove && target.Id != ProjectId && await store.CanMove(UserId, target.Id))
                    MoveProjects.Add(target);
            }
        }
        return Page();
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
    public async Task<IActionResult> OnPostTimeAsync(int id,DateTime? date,string? hours,string? note,string? activity)
    {
        var parsed=decimal.TryParse(hours?.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value);
        var valid=date is not null&&parsed&&!ModelState.Any(x=>x.Key=="date" && x.Value!.Errors.Count>0);
        ModelState.Clear();var result=await OnGetAsync(id,0);if(result is not PageResult)return result;if(!valid){ModelState.AddModelError("","Unesi valjan datum i broj sati.");return Page();}
        try{await store.LogTime(UserId,id,date!.Value,value,note,activity);return RedirectToPage(new{id});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostTransferAsync(int id, int? revision, int destination, bool move)
    {
        if (id <= 0 || destination <= 0 || revision is null or < 0 ||
            ModelState.GetValidationState(nameof(move)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
            return BadRequest();
        try
        {
            var saved = await store.Transfer(UserId, id, revision.Value, destination, move);
            TempData["Notice"] = move ? "Zadatak je premješten." : "Zadatak je kopiran.";
            return RedirectToPage(new { id = saved });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            var result = await OnGetAsync(id, 0);
            if (result is not PageResult) return result;
            ModelState.AddModelError("", ex.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLinkAsync(int id, int otherId, string? relation, bool remove)
    {
        if (id <= 0 || otherId <= 0 ||
            ModelState.GetValidationState(nameof(remove)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
            return BadRequest();
        var (kind, outgoing) = relation switch
        {
            "blockedBy" => ("blocks", false),
            "subtaskOf" => ("parent", false),
            _ => (relation ?? "", true)
        };
        string error;
        try
        {
            await links.Set(UserId, id, otherId, kind, outgoing, remove);
            return RedirectToPage(new { id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51015 or 51016 or 51017)
        {
            error = ex.Number switch
            {
                51016 => "Podzadatak već ima nadređeni zadatak.",
                51017 => "Ova bi veza stvorila ciklus.",
                _ => "Veze se trenutačno uređuju. Pokušaj ponovno."
            };
        }

        ModelState.Clear();
        var result = await OnGetAsync(id, 0);
        if (result is not PageResult) return result;
        ModelState.AddModelError("", error);
        return Page();
    }

    public IActionResult OnPost()=>BadRequest();
}

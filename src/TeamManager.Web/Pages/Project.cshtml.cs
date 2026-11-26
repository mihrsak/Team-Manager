using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public sealed record ProjectModulesDraft(int Revision,bool IssuesEnabled,bool TimeEnabled);
public class ProjectModel(ProjectStore store):PageModel
{
    public ProjectInput Current {get;private set;}=new();
    public bool HasDraft {get;private set;}
    public bool HasConflict=>HasDraft&&Id is not null&&Input.Revision!=Current.Revision;
    public ProjectModulesDraft? ModulesDraft {get;private set;}
    public bool HasModuleConflict=>ModulesDraft is {} draft&&draft.Revision!=Current.Revision;
    [BindProperty] public ProjectInput Input {get;set;}=new();
    public int? Id {get;set;}
    public bool CanEdit {get;set;}
    public List<ProjectDetail> Parents {get;set;}=[];
    public HashSet<int> UnavailableParents {get;}=[];
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id=id;Parents=[];UnavailableParents.Clear();var all=await store.List(UserId);
        if(id is not null){var p=all.FirstOrDefault(x=>x.Id==id);if(p is null)return NotFound();Input=p.Input;}
        var managesAll=await store.HasGlobalAdministration(UserId);
        CanEdit=managesAll||await store.CanManage(UserId,id);if(id is null&&!CanEdit)return Forbid();
        foreach(var p in all.Where(x=>x.Id!=id))if(p.Id==Input.ParentId||managesAll||await store.CanManage(UserId,p.Id))Parents.Add(p);
        if(Input.ParentId is int parent&&!Parents.Any(x=>x.Id==parent)){Parents.Add(new(parent,new(){Name="Nedostupan projekt"}));UnavailableParents.Add(parent);}
        Current=Input;
        return Page();
    }
    public async Task<IActionResult> OnPostModulesAsync(int id,bool issuesEnabled,bool timeEnabled,int revision,bool resolveConflict=false,int? reviewedRevision=null)
    {
        if(id<=0||revision<0||!ListFilterValidation.Valid(ModelState,"id","revision","issuesEnabled","timeEnabled","resolveConflict","reviewedRevision")||resolveConflict&&reviewedRevision is null or <0)return BadRequest();
        ModelState.Clear();var result=await OnGetAsync(id);if(result is not PageResult)return result;
        if(!CanEdit)return Forbid();
        if(resolveConflict)revision=reviewedRevision!.Value;
        ModulesDraft=new(revision,issuesEnabled,timeEnabled);
        try{await store.SetModules(UserId,id,issuesEnabled,timeEnabled,revision);return RedirectToPage(new{id});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException ex){result=await OnGetAsync(id);if(result is not PageResult)return result;if(!CanEdit)return Forbid();ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostAsync(int? id,bool resolveConflict=false,int? reviewedRevision=null)
    {
        if(!ListFilterValidation.Valid(ModelState,"resolveConflict","reviewedRevision")||resolveConflict&&reviewedRevision is null or <0)return BadRequest();
        var submitted=Input;var result=await OnGetAsync(id);if(result is not PageResult)return result;Input=submitted;
        if(!CanEdit)return Forbid();HasDraft=true;
        if(resolveConflict){Input.Revision=reviewedRevision!.Value;ModelState.Remove("Input.Revision");}
        if(!ModelState.IsValid)return Page();
        try{var saved=await store.Save(UserId,id,Input);return RedirectToPage(new{id=saved});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){return await DraftError(id,submitted,ex.Message);}
        catch(SqlException ex) when(ex.Number is 2601 or 2627){return await DraftError(id,submitted,"Projekt s tim nazivom već postoji.");}
    }
    private async Task<IActionResult> DraftError(int? id,ProjectInput submitted,string message)
    {
        var result=await OnGetAsync(id);if(result is not PageResult)return result;
        if(!CanEdit)return Forbid();Input=submitted;ModelState.AddModelError("",message);return Page();
    }
}

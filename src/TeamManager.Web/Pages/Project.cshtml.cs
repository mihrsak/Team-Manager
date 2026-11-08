using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
namespace TeamManager.Web.Pages;
public class ProjectModel(ProjectStore store):PageModel
{
    [BindProperty] public ProjectInput Input {get;set;}=new();
    public int? Id {get;set;}
    public bool CanEdit {get;set;}
    public List<ProjectDetail> Parents {get;set;}=[];
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id=id;var all=await store.List(UserId);
        if(id is not null){var p=all.FirstOrDefault(x=>x.Id==id);if(p is null)return NotFound();Input=p.Input;}
        CanEdit=await store.CanManage(UserId,id);if(id is null&&!CanEdit)return Forbid();
        foreach(var p in all.Where(x=>x.Id!=id))if(p.Id==Input.ParentId||await store.CanManage(UserId,p.Id))Parents.Add(p);
        if(Input.ParentId is int parent&&!Parents.Any(x=>x.Id==parent))Parents.Add(new(parent,new(){Name="Nedostupan projekt"}));
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        var submitted=Input;var result=await OnGetAsync(id);if(result is not PageResult)return result;Input=submitted;
        if(!CanEdit)return Forbid();if(!ModelState.IsValid)return Page();
        try{var saved=await store.Save(UserId,id,Input);return RedirectToPage(new{id=saved});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){ModelState.AddModelError("",ex.Message);return Page();}
        catch(SqlException ex) when(ex.Number is 2601 or 2627){ModelState.AddModelError("","Projekt s tim nazivom već postoji.");return Page();}
    }
}

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
namespace TeamManager.Web.Pages;
public class ProjectsModel(ProjectStore store):PageModel
{
    public List<ProjectDetail> Items {get;set;}=[];
    public bool CanCreate {get;set;}
    [BindProperty(SupportsGet=true), StringLength(200)] public string? Query {get;set;}
    [BindProperty(SupportsGet=true)] public string? State {get;set;}
    [BindProperty(SupportsGet=true)] public int PageNumber {get;set;}=1;
    public int TotalCount {get;private set;}
    public int PageCount {get;private set;}=1;
    private Dictionary<int,string> names=[];
    public bool FiltersValid=>ListFilterValidation.Valid(ModelState,"Query","State","PageNumber");
    public bool InvalidStateFilter=>!ListFilterValidation.Valid(ModelState,"State");
    public string StateFilterValue=>ListFilterValidation.Value(ModelState,"State",State);
    public async Task OnGetAsync()
    {
        var user=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        CanCreate=await store.CanManage(user,null);
        ListFilterValidation.SingleValues(Request,ModelState,"Query","State","PageNumber");
        if(State is not (null or "Active" or "Closed" or "Archived"))ModelState.TryAddModelError(nameof(State),"Odaberi valjano stanje projekta.");
        if(!FiltersValid)return;
        var all=await store.List(user);names=all.ToDictionary(p=>p.Id,p=>p.Input.Name);
        Query=Query?.Trim();
        var filtered=all.Where(p=>(State is null||p.Input.State==State)&&(string.IsNullOrEmpty(Query)||p.Input.Name.Contains(Query,StringComparison.CurrentCultureIgnoreCase)||p.Input.Description.Contains(Query,StringComparison.CurrentCultureIgnoreCase))).ToList();
        TotalCount=filtered.Count;PageCount=Math.Max(1,(TotalCount+49)/50);PageNumber=Math.Clamp(PageNumber,1,PageCount);
        Items=filtered.Skip((PageNumber-1)*50).Take(50).ToList();
    }
    public string PageUrl(int page)=>Url.Page("/Projects",new{Query,State,PageNumber=page})!;
    public bool HasParent(int id)=>names.ContainsKey(id);
    public string ParentName(int? id)=>id is null?"—":names.GetValueOrDefault(id.Value,"Nedostupan projekt");
    public static string StateName(string state)=>state switch{"Active"=>"Aktivan","Closed"=>"Zatvoren",_=>"Arhiviran"};
}

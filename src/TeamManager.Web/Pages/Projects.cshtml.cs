using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class ProjectsModel(ProjectStore store):PageModel
{
    public List<ProjectDetail> Items {get;set;}=[];
    public bool CanCreate {get;set;}
    public async Task OnGetAsync(){var user=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);Items=await store.List(user);CanCreate=await store.CanManage(user,null);}
    public string ParentName(int? id)=>Items.FirstOrDefault(x=>x.Id==id)?.Input.Name??(id is null?"—":"Nedostupan projekt");
    public static string StateName(string state)=>state switch{"Active"=>"Aktivan","Closed"=>"Zatvoren",_=>"Arhiviran"};
}

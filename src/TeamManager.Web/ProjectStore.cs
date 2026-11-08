using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;

public sealed class ProjectInput
{
    [Required,StringLength(100)] public string Name {get;set;}="";
    [StringLength(4000)] public string Description {get;set;}="";
    public int? ParentId {get;set;}
    public bool IsPublic {get;set;}
    public string State {get;set;}="Active";
    public int Revision {get;set;}
}
public record ProjectDetail(int Id,ProjectInput Input);
public sealed class ProjectStore(IConfiguration config,Permissions permissions)
{
    private SqlConnection Connect()=>new(config.GetConnectionString("TeamManager"));
    public async Task<bool> CanManage(int user,int? id)
    {
        var rights=await permissions.Effective(user,id is null?null:"project",id);
        return rights.Contains("*")||rights.Contains(id is null?"organization.manage":"projects.manage");
    }
    public async Task<List<ProjectDetail>> List(int user)
    {
        var all=new List<ProjectDetail>();
        await using(var c=Connect()){
            await c.OpenAsync();await using var cmd=new SqlCommand("SELECT Id,Name,Description,ParentId,IsPublic,State,Revision FROM dbo.Projects ORDER BY Name",c);
            await using var r=await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync())all.Add(new(r.GetInt32(0),new(){Name=r.GetString(1),Description=r.GetString(2),ParentId=r.IsDBNull(3)?null:r.GetInt32(3),IsPublic=r.GetBoolean(4),State=r.GetString(5),Revision=r.GetInt32(6)}));
        }
        var result=new List<ProjectDetail>();
        foreach(var p in all){var rights=await permissions.Effective(user,"project",p.Id);if(p.Input.IsPublic||rights.Contains("*")||rights.Contains("issues.view")||rights.Contains("projects.manage"))result.Add(p);}
        return result;
    }
    public async Task<int> Save(int user,int? id,ProjectInput input)
    {
        if(!await CanManage(user,id))throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>100||(input.Description?.Length??0)>4000||!new[]{"Active","Closed","Archived"}.Contains(input.State))throw new ArgumentException("Provjeri naziv, opis i stanje projekta.");
        // Changing hierarchy requires authority over the destination parent as well.
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        await using(var mutex=new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='TeamManager.Projects',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51002,'Project changes are busy',1;",c,tx))await mutex.ExecuteNonQueryAsync();
        int? oldParent=null;
        if(id is not null){await using var old=new SqlCommand("SELECT ParentId FROM dbo.Projects WHERE Id=@id",c,tx);old.Parameters.Add(P("@id",id));var value=await old.ExecuteScalarAsync();oldParent=value is null or DBNull?null:Convert.ToInt32(value);}
        if(input.ParentId is not null&&input.ParentId!=oldParent&&!await CanManage(user,input.ParentId))throw new UnauthorizedAccessException();
        var visited=new HashSet<int>();if(id is not null)visited.Add(id.Value);
        var ancestor=input.ParentId;
        while(ancestor is not null){
            if(!visited.Add(ancestor.Value))throw new ArgumentException("Projekt ne može biti vlastiti nadređeni projekt niti potomak svojeg potprojekta.");
            await using var parent=new SqlCommand("SELECT ParentId FROM dbo.Projects WHERE Id=@id",c,tx);parent.Parameters.Add(P("@id",ancestor));var value=await parent.ExecuteScalarAsync();
            if(value is null)throw new ArgumentException("Nadređeni projekt ne postoji.");ancestor=value is DBNull?null:Convert.ToInt32(value);
        }
        var sql=id is null?"INSERT dbo.Projects(Name,Description,ParentId,IsPublic,State) OUTPUT inserted.Id VALUES(@n,@d,@p,@public,@state)":"UPDATE dbo.Projects SET Name=@n,Description=@d,ParentId=@p,IsPublic=@public,State=@state,Revision=Revision+1 OUTPUT inserted.Id WHERE Id=@id AND Revision=@rev";
        await using var cmd=new SqlCommand(sql,c,tx);cmd.Parameters.AddRange([P("@n",input.Name.Trim()),P("@d",input.Description??""),P("@p",input.ParentId),P("@public",input.IsPublic),P("@state",input.State),P("@id",id),P("@rev",input.Revision)]);
        var saved=await cmd.ExecuteScalarAsync();if(saved is null)throw new InvalidOperationException("Projekt je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
        var result=Convert.ToInt32(saved);
        if(id is null){await using var member=new SqlCommand("INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@p,@u,N'Voditelj')",c,tx);member.Parameters.AddRange([P("@p",result),P("@u",user)]);await member.ExecuteNonQueryAsync();}
        await tx.CommitAsync();return result;
    }
}

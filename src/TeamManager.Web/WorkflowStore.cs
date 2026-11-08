using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;
public record Transition(string Role,string Type,string From,string To);
public sealed class WorkflowStore(IConfiguration configuration)
{
    private SqlConnection Connect()=>new(configuration.GetConnectionString("TeamManager"));
    public async Task<List<string>> Values(string kind)
    {
        var table=kind switch{"type"=>"IssueTypes","status"=>"IssueStatuses","priority"=>"IssuePriorities",_=>throw new ArgumentException()};
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand($"SELECT Name FROM dbo.{table} ORDER BY Name",c);await using var r=await cmd.ExecuteReaderAsync();var result=new List<string>();while(await r.ReadAsync())result.Add(r.GetString(0));return result;
    }
    public async Task AddValue(string kind,string name,bool closed)
    {
        var table=kind switch{"type"=>"IssueTypes","status"=>"IssueStatuses","priority"=>"IssuePriorities",_=>throw new ArgumentException()};
        if(string.IsNullOrWhiteSpace(name)||name.Length>(kind=="priority"?20:30))throw new ArgumentException("Neispravan naziv.");
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand(kind=="status"?"INSERT dbo.IssueStatuses(Name,IsClosed) VALUES(@name,@closed)":$"INSERT dbo.{table}(Name) VALUES(@name)",c);cmd.Parameters.AddRange([P("@name",name.Trim()),P("@closed",closed)]);await cmd.ExecuteNonQueryAsync();
    }
    public async Task<List<Transition>> Transitions()
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT RoleName,Type,FromStatus,ToStatus FROM dbo.WorkflowTransitions ORDER BY RoleName,Type,FromStatus,ToStatus",c);await using var r=await cmd.ExecuteReaderAsync();var list=new List<Transition>();while(await r.ReadAsync())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));return list;
    }
    public async Task Set(Transition value,bool remove)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand(remove?"DELETE dbo.WorkflowTransitions WHERE RoleName=@r AND Type=@t AND FromStatus=@f AND ToStatus=@s":"INSERT dbo.WorkflowTransitions VALUES(@r,@t,@f,@s)",c);cmd.Parameters.AddRange([P("@r",value.Role),P("@t",value.Type),P("@f",value.From),P("@s",value.To)]);await cmd.ExecuteNonQueryAsync();
    }
    public static async Task<bool> Allowed(SqlConnection c,SqlTransaction tx,int user,int project,string type,string from,string to)
    {
        await using var cmd=new SqlCommand("SELECT COUNT(*) FROM dbo.WorkflowTransitions w WHERE w.Type=@type AND w.FromStatus=@from AND w.ToStatus=@to AND (EXISTS(SELECT 1 FROM dbo.UserRoles r WHERE r.UserId=@user AND r.RoleName=w.RoleName) OR EXISTS(SELECT 1 FROM dbo.ProjectMembers m WHERE m.UserId=@user AND m.ProjectId=@project AND m.Role=w.RoleName))",c,tx);cmd.Parameters.AddRange([P("@type",type),P("@from",from),P("@to",to),P("@user",user),P("@project",project)]);return Convert.ToInt32(await cmd.ExecuteScalarAsync())>0;
    }
}

using Microsoft.Data.SqlClient;
using System.Text.Json;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;
public sealed record TimeDetail(int Id,int IssueId,string Title,string Project,string Person,DateTime Date,decimal Hours,string Activity,string Note);
public sealed partial class IssueStore
{
    public async Task<List<TimeDetail>> TimeDetails(int user,IEnumerable<int> projects,DateTime? from,DateTime? to,string? person,string? activity,int page)
    {
        var offset=(Math.Clamp(page,1,10000)-1)*50;
        if(oracle.Enabled)return await oracle.TimeDetails(user,projects,from,to,person,activity,offset);
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("""
            SELECT t.Id,i.Id,i.Title,p.Name,u.DisplayName,t.WorkDate,t.Hours,t.Activity,t.Note
            FROM dbo.TimeEntries t JOIN dbo.Issues i ON i.Id=t.IssueId JOIN dbo.Projects p ON p.Id=i.ProjectId JOIN dbo.AppUsers u ON u.Id=t.UserId
            JOIN OPENJSON(@projects) WITH (Id int '$') allowed ON allowed.Id=p.Id
            WHERE p.IssuesEnabled=1 AND p.TimeEnabled=1 AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@user,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
              AND (@from IS NULL OR t.WorkDate>=@from) AND (@to IS NULL OR t.WorkDate<=@to)
              AND (@person='' OR CHARINDEX(@person,u.DisplayName)>0 OR CHARINDEX(@person,u.EmployeeNumber)>0)
              AND (@activity='' OR CHARINDEX(@activity,t.Activity)>0)
            ORDER BY t.WorkDate DESC,t.Id DESC OFFSET @offset ROWS FETCH NEXT 51 ROWS ONLY OPTION(MAXRECURSION 0)
            """,c);
        cmd.Parameters.AddRange([P("@user",user),P("@projects",JsonSerializer.Serialize(projects)),P("@from",from),P("@to",to),P("@person",person??""),P("@activity",activity??""),P("@offset",offset)]);
        var rows=new List<TimeDetail>();await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())rows.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDateTime(5),r.GetDecimal(6),r.GetString(7),r.GetString(8)));return rows;
    }
}

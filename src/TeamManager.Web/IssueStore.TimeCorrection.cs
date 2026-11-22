using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;

public sealed record EditableTime(int Id,int IssueId,int AuthorId,DateTime Date,decimal Hours,string Activity,string Note)
{
    public string Token=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Id,IssueId,AuthorId,Date=Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Hours=Hours.ToString("0.00",CultureInfo.InvariantCulture),Activity,Note}))));
}
public sealed partial class IssueStore
{
    public async Task<EditableTime?> TimeEntryFor(int user,int issue,int entry)
    {
        var detail=await Get(user,issue);if(detail is null||!await Can(user,detail.ProjectId,"time.view"))return null;
        if(oracle.Enabled)return await oracle.TimeEntryFor(user,issue,entry);
        await using var c=Connect();await c.OpenAsync();
        await using var cmd=new SqlCommand("SELECT Id,IssueId,UserId,WorkDate,Hours,Activity,Note FROM dbo.TimeEntries WHERE Id=@entry AND IssueId=@issue AND EXISTS(SELECT 1 FROM dbo.Issues i WHERE i.Id=@issue AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@user,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))) OPTION(MAXRECURSION 0)",c);
        cmd.Parameters.AddRange([P("@entry",entry),P("@issue",issue),P("@user",user)]);
        await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetDateTime(3),r.GetDecimal(4),r.GetString(5),r.GetString(6)):null;
    }
    internal static void ValidateTime(DateTime date,decimal hours,string? activity,string? note)
    {
        if(date.Year<1753||hours<=0||hours>24||decimal.Round(hours,2)!=hours||(note?.Length??0)>1000)throw new ArgumentException("Unesi datum i sate od 0,01 do 24 (najviše dvije decimale).");
        if(string.IsNullOrWhiteSpace(activity)||activity.Trim().Length>60)throw new ArgumentException("Aktivnost mora imati od 1 do 60 znakova.");
    }
    internal static string TimeCorrectionAudit(EditableTime before,DateTime date,decimal hours,string activity,string note)=>JsonSerializer.Serialize(new{TimeEntryId=before.Id,AuthorId=before.AuthorId,Before=new{before.Date,before.Hours,before.Activity,before.Note},After=new{Date=date.Date,Hours=hours,Activity=activity,Note=note}});
    public async Task CorrectTime(int user,int issue,int entry,string token,DateTime date,decimal hours,string activity,string? note)
    {
        ValidateTime(date,hours,activity,note);activity=activity.Trim();note??="";
        if(oracle.Enabled){await oracle.CorrectTime(user,issue,entry,token,date,hours,activity,note);return;}
        var detail=await Get(user,issue)??throw new UnauthorizedAccessException();
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
        await RequireActive(c,tx,detail.ProjectId,true);await RequireIssueProject(c,tx,issue,detail.ProjectId,user);
        await using var rights=new SqlCommand("SELECT DISTINCT p.Permission FROM (SELECT RoleName FROM dbo.EffectiveUserRoles WHERE UserId=@user UNION SELECT RoleName FROM dbo.EffectiveProjectRoles(@project,@user)) r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName OPTION(MAXRECURSION 0)",c,tx);
        rights.Parameters.AddRange([P("@user",user),P("@project",detail.ProjectId)]);var allowed=new HashSet<string>();await using(var r=await rights.ExecuteReaderAsync()){while(await r.ReadAsync())allowed.Add(r.GetString(0));}
        if(!allowed.Contains("*")&&!allowed.Contains("time.log"))throw new UnauthorizedAccessException();
        EditableTime before;
        await using(var read=new SqlCommand("SELECT Id,IssueId,UserId,WorkDate,Hours,Activity,Note FROM dbo.TimeEntries WITH(UPDLOCK,HOLDLOCK) WHERE Id=@entry AND IssueId=@issue",c,tx))
        {read.Parameters.AddRange([P("@entry",entry),P("@issue",issue)]);await using var r=await read.ExecuteReaderAsync();if(!await r.ReadAsync())throw new UnauthorizedAccessException();before=new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetDateTime(3),r.GetDecimal(4),r.GetString(5),r.GetString(6));}
        if(before.AuthorId!=user&&!allowed.Contains("*")&&!allowed.Contains("projects.manage"))throw new UnauthorizedAccessException();
        if(before.Token!=token)throw new InvalidOperationException("Unos vremena je promijenjen. Ponovno učitaj stranicu.");
        await using var update=new SqlCommand("UPDATE dbo.TimeEntries SET WorkDate=@date,Hours=@hours,Activity=@activity,Note=@note WHERE Id=@entry AND IssueId=@issue",c,tx);
        update.Parameters.AddRange([P("@date",date.Date),P("@hours",hours),P("@activity",activity),P("@note",note),P("@entry",entry),P("@issue",issue)]);await update.ExecuteNonQueryAsync();
        await Event(c,tx,issue,user,"time",TimeCorrectionAudit(before,date,hours,activity,note));await tx.CommitAsync();
    }
}

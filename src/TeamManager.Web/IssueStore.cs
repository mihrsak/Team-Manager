using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;

public sealed class IssueInput
{
    [Required,StringLength(200)] public string Title{get;set;}="";
    [StringLength(4000)] public string Description{get;set;}="";
    [Required] public string Type{get;set;}="Zadatak";
    [Required] public string Priority{get;set;}="Normalan";
    [Required] public string Status{get;set;}="Novo";
    public int? AssigneeId{get;set;}
    public DateTime? DueDate{get;set;}
    public int Revision{get;set;}
}
public record IssueRow(int Id,int ProjectId,string Project,string Title,string Status,string Priority,string? Assignee,DateTime? DueDate);
public record IssueDetail(int Id,int ProjectId,IssueInput Input);
public record IssueEvent(string Author,string Kind,string Body,DateTime At);
public record TimeEntry(string Author,DateTime Date,decimal Hours,string Note);
public sealed class IssueStore(IConfiguration config,Permissions permissions,WorkflowStore workflow)
{
    public static readonly string[] Types=["Zadatak","Greška","Zahtjev"];
    public static readonly string[] Priorities=["Nizak","Normalan","Visok","Hitno"];
    public static readonly string[] Statuses=["Novo","U tijeku","Na provjeri","Završeno"];
    private SqlConnection Connect()=>new(config.GetConnectionString("TeamManager"));
    public async Task<bool> Can(int user,int project,string right){var rights=await permissions.Effective(user,"project",project);return rights.Contains("*")||rights.Contains(right);}
    public async Task<List<Team>> Projects(int user,string right="issues.view")
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT Id,Name FROM dbo.Projects ORDER BY Name",c);await using var r=await cmd.ExecuteReaderAsync();var all=new List<Team>();while(await r.ReadAsync())all.Add(new(r.GetInt32(0),r.GetString(1)));
        var result=new List<Team>();foreach(var project in all)if(await Can(user,project.Id,right))result.Add(project);return result;
    }
    public async Task<List<IssueRow>> List(int user,int? project,string? status,string? query)
    {
        var projects=(await Projects(user)).Select(x=>x.Id).ToHashSet();
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT i.Id,i.ProjectId,p.Name,i.Title,i.Status,i.Priority,u.DisplayName,i.DueDate FROM dbo.Issues i JOIN dbo.Projects p ON p.Id=i.ProjectId LEFT JOIN dbo.AppUsers u ON u.Id=i.AssigneeId WHERE (@p IS NULL OR i.ProjectId=@p) AND (@s='' OR i.Status=@s) AND (@q='' OR CHARINDEX(@q,i.Title)>0 OR CHARINDEX(@q,i.Description)>0) ORDER BY i.Id DESC",c);
        cmd.Parameters.AddRange([P("@p",project),P("@s",status??""),P("@q",query??"")]);await using var r=await cmd.ExecuteReaderAsync();var result=new List<IssueRow>();while(await r.ReadAsync())if(projects.Contains(r.GetInt32(1)))result.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetDateTime(7)));return result;
    }
    public async Task<IssueDetail?> Get(int user,int id)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,Revision FROM dbo.Issues WHERE Id=@id",c);cmd.Parameters.Add(P("@id",id));await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return null;
        var project=r.GetInt32(0);if(!await Can(user,project,"issues.view"))return null;
        return new(id,project,new(){Title=r.GetString(1),Description=r.GetString(2),Type=r.GetString(3),Priority=r.GetString(4),Status=r.GetString(5),AssigneeId=r.IsDBNull(6)?null:r.GetInt32(6),DueDate=r.IsDBNull(7)?null:r.GetDateTime(7),Revision=r.GetInt32(8)});
    }
    public async Task<List<Team>> Assignees(int project)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT u.Id,u.DisplayName FROM dbo.AppUsers u JOIN dbo.ProjectMembers m ON m.UserId=u.Id WHERE m.ProjectId=@p ORDER BY u.DisplayName",c);cmd.Parameters.Add(P("@p",project));await using var r=await cmd.ExecuteReaderAsync();var list=new List<Team>();while(await r.ReadAsync())list.Add(new(r.GetInt32(0),r.GetString(1)));return list;
    }
    public async Task<int> Save(int user,int project,int? id,IssueInput input)
    {
        if(!await Can(user,project,id is null?"issues.create":"issues.edit")||!await Can(user,project,"issues.view"))throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>200||(input.Description?.Length??0)>4000||!(await workflow.Values("type")).Contains(input.Type)||!(await workflow.Values("priority")).Contains(input.Priority)||!(await workflow.Values("status")).Contains(input.Status)||input.DueDate?.Year<1753)throw new ArgumentException("Provjeri polja zadatka.");
        if(id is null&&input.Status!="Novo")throw new ArgumentException("Novi zadatak mora početi statusom Novo.");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        if(input.AssigneeId is not null){await using var check=new SqlCommand("SELECT COUNT(*) FROM dbo.ProjectMembers WITH(HOLDLOCK) WHERE ProjectId=@p AND UserId=@u",c,tx);check.Parameters.AddRange([P("@p",project),P("@u",input.AssigneeId)]);if(Convert.ToInt32(await check.ExecuteScalarAsync())==0)throw new ArgumentException("Nositelj mora biti član projekta.");}
        string? before=null;
        if(id is not null){await using var previous=new SqlCommand("SELECT Title,Description,Type,Priority,Status,AssigneeId,DueDate FROM dbo.Issues WITH(UPDLOCK) WHERE Id=@id AND ProjectId=@p FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",c,tx);previous.Parameters.AddRange([P("@id",id),P("@p",project)]);before=Convert.ToString(await previous.ExecuteScalarAsync());}
        if(id is not null){
            if(string.IsNullOrEmpty(before))throw new InvalidOperationException("Zadatak više ne postoji.");
            using var previous=JsonDocument.Parse(before);var oldStatus=previous.RootElement.GetProperty("Status").GetString()!;var oldType=previous.RootElement.GetProperty("Type").GetString()!;
            var rights=await permissions.Effective(user,"project",project);
            if(oldStatus!=input.Status&&!rights.Contains("*")&&!await WorkflowStore.Allowed(c,tx,user,project,oldType,oldStatus,input.Status))throw new ArgumentException("Tvoja rola ne dopušta taj prijelaz statusa za ovaj tip zadatka.");
        }
        var sql=id is null?"INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,CreatedBy) OUTPUT inserted.Id VALUES(@p,@t,@d,@type,@priority,@status,@a,@due,@user)":"UPDATE dbo.Issues SET Title=@t,Description=@d,Type=@type,Priority=@priority,Status=@status,AssigneeId=@a,DueDate=@due,Revision=Revision+1 OUTPUT inserted.Id WHERE Id=@id AND ProjectId=@p AND Revision=@revision";
        await using var cmd=new SqlCommand(sql,c,tx);cmd.Parameters.AddRange([P("@p",project),P("@t",input.Title.Trim()),P("@d",input.Description??""),P("@type",input.Type),P("@priority",input.Priority),P("@status",input.Status),P("@a",input.AssigneeId),P("@due",input.DueDate),P("@user",user),P("@id",id),P("@revision",input.Revision)]);
        var saved=await cmd.ExecuteScalarAsync();if(saved is null)throw new InvalidOperationException("Zadatak je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
        var issueId=Convert.ToInt32(saved);await Event(c,tx,issueId,user,id is null?"created":"updated",JsonSerializer.Serialize(new{Before=before,After=input}));await tx.CommitAsync();return issueId;
    }
    private static async Task Event(SqlConnection c,SqlTransaction tx,int id,int user,string kind,string text){await using var cmd=new SqlCommand("INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) VALUES(@id,@user,@kind,@text)",c,tx);cmd.Parameters.AddRange([P("@id",id),P("@user",user),P("@kind",kind),P("@text",text)]);await cmd.ExecuteNonQueryAsync();}
    public async Task Comment(int user,int id,string text)
    {
        var issue=await Get(user,id)??throw new UnauthorizedAccessException();if(!await Can(user,issue.ProjectId,"issues.comment"))throw new UnauthorizedAccessException();if(string.IsNullOrWhiteSpace(text)||text.Length>4000)throw new ArgumentException("Komentar mora imati 1–4000 znakova.");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();await Event(c,tx,id,user,"comment",text.Trim());await tx.CommitAsync();
    }
    public async Task LogTime(int user,int id,DateTime date,decimal hours,string? note)
    {
        var issue=await Get(user,id)??throw new UnauthorizedAccessException();if(!await Can(user,issue.ProjectId,"time.log"))throw new UnauthorizedAccessException();if(hours<=0||hours>24||decimal.Round(hours,2)!=hours||date.Year<1753||(note?.Length??0)>1000)throw new ArgumentException("Unesi datum i sate od 0,01 do 24 (najviše dvije decimale).");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();await using var cmd=new SqlCommand("INSERT dbo.TimeEntries(IssueId,UserId,WorkDate,Hours,Note) VALUES(@id,@user,@date,@hours,@note)",c,tx);cmd.Parameters.AddRange([P("@id",id),P("@user",user),P("@date",date.Date),P("@hours",hours),P("@note",note??"")]);await cmd.ExecuteNonQueryAsync();await Event(c,tx,id,user,"time",$"{date:yyyy-MM-dd}: {hours} h — {note}");await tx.CommitAsync();
    }
    public async Task<(List<IssueEvent> Events,List<TimeEntry> Time)> Activity(int user,int id)
    {
        if(await Get(user,id) is null)throw new UnauthorizedAccessException();await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT u.DisplayName,e.Kind,e.Body,e.CreatedAt FROM dbo.IssueEvents e JOIN dbo.AppUsers u ON u.Id=e.UserId WHERE e.IssueId=@id ORDER BY e.Id DESC; SELECT u.DisplayName,t.WorkDate,t.Hours,t.Note FROM dbo.TimeEntries t JOIN dbo.AppUsers u ON u.Id=t.UserId WHERE t.IssueId=@id ORDER BY t.WorkDate DESC",c);cmd.Parameters.Add(P("@id",id));await using var r=await cmd.ExecuteReaderAsync();var events=new List<IssueEvent>();var time=new List<TimeEntry>();while(await r.ReadAsync())events.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetDateTime(3)));await r.NextResultAsync();while(await r.ReadAsync())time.Add(new(r.GetString(0),r.GetDateTime(1),r.GetDecimal(2),r.GetString(3)));return(events,time);
    }
}

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
    public int? RoadmapItemId{get;set;}
    public DateTime? StartDate{get;set;}
    public DateTime? DueDate{get;set;}
    public int Revision{get;set;}
}
public record IssueRow(int Id,int ProjectId,string Project,string Title,string Status,string Priority,string? Assignee,DateTime? DueDate,int Revision,string? AssigneeEmployeeNumber,DateTime? StartDate);
public record IssueDetail(int Id,int ProjectId,IssueInput Input);
public record ActivityRow(int Id,int IssueId,string Title,string Project,string Author,string Kind,DateTime At);
public record IssueEvent(string Author,string Kind,string Body,DateTime At);
public record TimeEntry(string Author,DateTime Date,decimal Hours,string Note,string Activity);
public record TimeReportRow(string Project,string Person,string EmployeeNumber,string Activity,decimal Hours);
public sealed class IssueStore(IConfiguration config,Permissions permissions,WorkflowStore workflow,ProjectStore projects)
{
    public static readonly string[] Types=["Zadatak","Greška","Zahtjev"];
    public static readonly string[] Priorities=["Nizak","Normalan","Visok","Hitno"];
    public static readonly string[] Statuses=["Novo","U tijeku","Na provjeri","Završeno"];
    private SqlConnection Connect()=>new(config.GetConnectionString("TeamManager"));
    public async Task<bool> Can(int user,int project,string right)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT IsPublic,State,IssuesEnabled,TimeEnabled FROM dbo.Projects WHERE Id=@p",c);cmd.Parameters.Add(P("@p",project));await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return false;
        if(!r.GetBoolean(2)||(right.StartsWith("time.")&&!r.GetBoolean(3)))return false;
        if(right=="time.view")right="issues.view";
        if(right!="issues.view"&&r.GetString(1)!="Active")return false;
        if(right=="issues.view"&&r.GetBoolean(0))return true;
        var rights=await permissions.Effective(user,"project",project);return rights.Contains("*")||rights.Contains(right);
    }
    internal static async Task RequireActive(SqlConnection c,SqlTransaction tx,int project,bool time=false)
    {
        await using var cmd=new SqlCommand("SELECT State FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@p AND IssuesEnabled=1 AND (@time=0 OR TimeEnabled=1)",c,tx);cmd.Parameters.Add(P("@p",project));
        cmd.Parameters.Add(P("@time",time));
        if(Convert.ToString(await cmd.ExecuteScalarAsync())!="Active")throw new UnauthorizedAccessException("Projekt je zatvoren ili arhiviran.");
    }
    public async Task<List<Team>> Projects(int user,string right="issues.view")
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT Id,Name FROM dbo.Projects ORDER BY Name",c);await using var r=await cmd.ExecuteReaderAsync();var all=new List<Team>();while(await r.ReadAsync())all.Add(new(r.GetInt32(0),r.GetString(1)));
        var result=new List<Team>();foreach(var project in all)if(await Can(user,project.Id,right))result.Add(project);return result;
    }
    public async Task<List<IssueRow>> List(int user,int? project,string? status,string? query,DateTime? dueFrom=null,DateTime? dueTo=null,bool schedule=false,int? assigneeId=null,bool openOnly=false,bool watchedOnly=false)
    {
        var projects=(await Projects(user)).Select(x=>x.Id).ToHashSet();
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT i.Id,i.ProjectId,p.Name,i.Title,i.Status,i.Priority,u.DisplayName,i.DueDate,i.Revision,u.EmployeeNumber,i.StartDate FROM dbo.Issues i JOIN dbo.Projects p ON p.Id=i.ProjectId LEFT JOIN dbo.AppUsers u ON u.Id=i.AssigneeId WHERE (@p IS NULL OR i.ProjectId=@p) AND (@watcher IS NULL OR EXISTS(SELECT 1 FROM dbo.IssueWatchers w WHERE w.IssueId=i.Id AND w.UserId=@watcher)) AND (@assignee IS NULL OR i.AssigneeId=@assignee) AND (@open=0 OR EXISTS(SELECT 1 FROM dbo.IssueStatuses st WHERE st.Name=i.Status AND st.IsClosed=0)) AND (@s='' OR i.Status=@s) AND (@q='' OR CHARINDEX(@q,i.Title)>0 OR CHARINDEX(@q,i.Description)>0) AND (@from IS NULL OR (CASE WHEN @schedule=1 THEN COALESCE(i.DueDate,i.StartDate) ELSE i.DueDate END)>=@from) AND (@to IS NULL OR (CASE WHEN @schedule=1 THEN COALESCE(i.StartDate,i.DueDate) ELSE i.DueDate END)<=@to) ORDER BY i.Id DESC",c);
        cmd.Parameters.AddRange([P("@p",project),P("@s",status??""),P("@q",query??""),new SqlParameter("@from",System.Data.SqlDbType.Date){Value=(object?)dueFrom??DBNull.Value},new SqlParameter("@to",System.Data.SqlDbType.Date){Value=(object?)dueTo??DBNull.Value},P("@schedule",schedule),new SqlParameter("@assignee",System.Data.SqlDbType.Int){Value=(object?)assigneeId??DBNull.Value},P("@open",openOnly),new SqlParameter("@watcher",System.Data.SqlDbType.Int){Value=watchedOnly?user:DBNull.Value}]);await using var r=await cmd.ExecuteReaderAsync();var result=new List<IssueRow>();while(await r.ReadAsync())if(projects.Contains(r.GetInt32(1)))result.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetDateTime(7),r.GetInt32(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetDateTime(10)));return result;
    }
    public async Task<IssueDetail?> Get(int user,int id)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,Revision,StartDate,RoadmapItemId FROM dbo.Issues WHERE Id=@id",c);cmd.Parameters.Add(P("@id",id));await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return null;
        var project=r.GetInt32(0);if(!await Can(user,project,"issues.view"))return null;
        return new(id,project,new(){Title=r.GetString(1),Description=r.GetString(2),Type=r.GetString(3),Priority=r.GetString(4),Status=r.GetString(5),AssigneeId=r.IsDBNull(6)?null:r.GetInt32(6),DueDate=r.IsDBNull(7)?null:r.GetDateTime(7),Revision=r.GetInt32(8),StartDate=r.IsDBNull(9)?null:r.GetDateTime(9),RoadmapItemId=r.IsDBNull(10)?null:r.GetInt32(10)});
    }
    public async Task<bool> IsWatching(int user, int issue)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT 1 FROM dbo.IssueWatchers WHERE IssueId=@issue AND UserId=@user", connection);
        command.Parameters.AddRange([P("@issue", issue), P("@user", user)]);
        return await command.ExecuteScalarAsync() is not null;
    }

    public async Task SetWatching(int user, int id, bool watch)
    {
        var issue = await Get(user, id) ?? throw new UnauthorizedAccessException();
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var project = new SqlCommand(
            "SELECT 1 FROM dbo.Projects WITH (HOLDLOCK) WHERE Id=@project AND IssuesEnabled=1", connection, transaction);
        project.Parameters.Add(P("@project", issue.ProjectId));
        if (await project.ExecuteScalarAsync() is null) throw new UnauthorizedAccessException();
        await RequireIssueProject(connection, transaction, id, issue.ProjectId);

        await using var command = new SqlCommand(watch
            ? """
              INSERT dbo.IssueWatchers(IssueId,UserId)
              SELECT @issue,@user WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.IssueWatchers WITH (UPDLOCK,HOLDLOCK) WHERE IssueId=@issue AND UserId=@user)
              """
            : "DELETE dbo.IssueWatchers WHERE IssueId=@issue AND UserId=@user", connection, transaction);
        command.Parameters.AddRange([P("@issue", id), P("@user", user)]);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<List<Team>> Assignees(int project)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT u.Id,u.DisplayName FROM dbo.AppUsers u JOIN dbo.ProjectMembers m ON m.UserId=u.Id WHERE m.ProjectId=@p ORDER BY u.DisplayName",c);cmd.Parameters.Add(P("@p",project));await using var r=await cmd.ExecuteReaderAsync();var list=new List<Team>();while(await r.ReadAsync())list.Add(new(r.GetInt32(0),r.GetString(1)));return list;
    }
    public async Task<int> Save(int user,int project,int? id,IssueInput input)
    {
        if(!await Can(user,project,id is null?"issues.create":"issues.edit")||!await Can(user,project,"issues.view"))throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>200||(input.Description?.Length??0)>4000||!(await workflow.Values("type")).Contains(input.Type)||!(await workflow.Values("priority")).Contains(input.Priority)||!(await workflow.Values("status")).Contains(input.Status)||input.DueDate?.Year<1753)throw new ArgumentException("Provjeri polja zadatka.");
        if (input.StartDate?.Year < 1753 || input.StartDate?.Date > input.DueDate?.Date)
            throw new ArgumentException("Početak mora biti valjan datum i ne može biti poslije roka.");
        if(id is null&&input.Status!="Novo")throw new ArgumentException("Novi zadatak mora početi statusom Novo.");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        var issueId = await SaveInTransaction(c, tx, user, project, id, input);
        await tx.CommitAsync();
        return issueId;
    }

    private async Task<int> SaveInTransaction(SqlConnection c, SqlTransaction tx, int user, int project, int? id, IssueInput input)
    {
        await RequireActive(c,tx,project);
        if(input.AssigneeId is not null){await using var check=new SqlCommand("SELECT COUNT(*) FROM dbo.ProjectMembers WITH(HOLDLOCK) WHERE ProjectId=@p AND UserId=@u",c,tx);check.Parameters.AddRange([P("@p",project),P("@u",input.AssigneeId)]);if(Convert.ToInt32(await check.ExecuteScalarAsync())==0)throw new ArgumentException("Nositelj mora biti član projekta.");}
        if (input.RoadmapItemId is int itemId)
        {
            await using var target = new SqlCommand(
                "SELECT 1 FROM dbo.ProjectMilestones WITH (HOLDLOCK) WHERE ProjectId=@project AND Id=@id", c, tx);
            target.Parameters.AddRange([P("@project", project), P("@id", itemId)]);
            if (await target.ExecuteScalarAsync() is null)
                throw new ArgumentException("Odaberi verziju ili prekretnicu ovog projekta.");
        }
        string? before=null;
        if(id is not null){await using var previous=new SqlCommand("SELECT Title,Description,Type,Priority,Status,AssigneeId,DueDate,StartDate,RoadmapItemId FROM dbo.Issues WITH(UPDLOCK) WHERE Id=@id AND ProjectId=@p FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",c,tx);previous.Parameters.AddRange([P("@id",id),P("@p",project)]);before=Convert.ToString(await previous.ExecuteScalarAsync());}
        if(id is not null){
            if(string.IsNullOrEmpty(before))throw new InvalidOperationException("Zadatak više ne postoji.");
            using var previous=JsonDocument.Parse(before);var oldStatus=previous.RootElement.GetProperty("Status").GetString()!;var oldType=previous.RootElement.GetProperty("Type").GetString()!;
            var rights=await permissions.Effective(user,"project",project);
            if(oldStatus!=input.Status&&!rights.Contains("*")&&!await WorkflowStore.Allowed(c,tx,user,project,oldType,oldStatus,input.Status))throw new ArgumentException("Tvoja rola ne dopušta taj prijelaz statusa za ovaj tip zadatka.");
        }
        var sql=id is null?"INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,StartDate,RoadmapItemId,CreatedBy) OUTPUT inserted.Id VALUES(@p,@t,@d,@type,@priority,@status,@a,@due,@start,@roadmap,@user)":"UPDATE dbo.Issues SET Title=@t,Description=@d,Type=@type,Priority=@priority,Status=@status,AssigneeId=@a,DueDate=@due,StartDate=@start,RoadmapItemId=@roadmap,Revision=Revision+1 OUTPUT inserted.Id WHERE Id=@id AND ProjectId=@p AND Revision=@revision";
        await using var cmd=new SqlCommand(sql,c,tx);cmd.Parameters.AddRange([P("@p",project),P("@t",input.Title.Trim()),P("@d",input.Description??""),P("@type",input.Type),P("@priority",input.Priority),P("@status",input.Status),P("@a",input.AssigneeId),P("@due",input.DueDate),new SqlParameter("@start",System.Data.SqlDbType.Date){Value=(object?)input.StartDate??DBNull.Value},new SqlParameter("@roadmap",System.Data.SqlDbType.Int){Value=(object?)input.RoadmapItemId??DBNull.Value},P("@user",user),P("@id",id),P("@revision",input.Revision)]);
        var saved=await cmd.ExecuteScalarAsync();if(saved is null)throw new InvalidOperationException("Zadatak je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
        var issueId=Convert.ToInt32(saved);await Event(c,tx,issueId,user,id is null?"created":"updated",JsonSerializer.Serialize(new{Before=before,After=input}));return issueId;
    }
    public async Task UpdateMany(int user, IReadOnlyDictionary<int, int> revisions, string? status, string? priority)
    {
        if (revisions.Count is < 1 or > 100 || (status is null && priority is null))
            throw new ArgumentException("Odaberi od 1 do 100 zadataka i barem jednu promjenu.");
        if ((status is not null && !(await workflow.Values("status")).Contains(status)) ||
            (priority is not null && !(await workflow.Values("priority")).Contains(priority)))
            throw new ArgumentException("Odaberi valjan status ili prioritet.");

        var batch = new List<IssueDetail>();
        foreach (var (id, revision) in revisions.OrderBy(item => item.Key))
        {
            var issue = await Get(user, id);
            if (issue is null || !await Can(user, issue.ProjectId, "issues.edit"))
                throw new UnauthorizedAccessException();
            if (issue.Input.Revision != revision)
                throw new InvalidOperationException("Neki su zadaci u međuvremenu izmijenjeni. Osvježi popis.");
            issue.Input.Status = status ?? issue.Input.Status;
            issue.Input.Priority = priority ?? issue.Input.Priority;
            batch.Add(issue);
        }

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        foreach (var issue in batch)
            await SaveInTransaction(connection, transaction, user, issue.ProjectId, issue.Id, issue.Input);
        await transaction.CommitAsync();
    }

    public Task<bool> CanMove(int user, int project)
        => projects.CanManage(user, project);

    public async Task<int> Transfer(int user, int id, int revision, int destination, bool move)
    {
        var issue = await Get(user, id) ?? throw new UnauthorizedAccessException();
        var source = issue.ProjectId;
        if (!await Can(user, destination, "issues.view") || !await Can(user, destination, "issues.create") ||
            (move && (!await Can(user, source, "issues.edit") || !await CanMove(user, source) ||
                      !await CanMove(user, destination))))
            throw new UnauthorizedAccessException();
        if (move && source == destination)
            throw new ArgumentException("Odaberi drugi projekt za premještanje.");
        if (issue.Input.Revision != revision)
            throw new InvalidOperationException("Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        if (move) await RequireActive(connection, transaction, source);
        await RequireActive(connection, transaction, destination);

        await using var check = new SqlCommand("""
            SELECT 1 FROM dbo.Issues WITH (UPDLOCK)
            WHERE Id=@id AND ProjectId=@source AND Revision=@revision
            """, connection, transaction);
        check.Parameters.AddRange([P("@id",id), P("@source",source), P("@revision",revision)]);
        if (await check.ExecuteScalarAsync() is null)
            throw new InvalidOperationException("Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");

        if (!move)
        {
            issue.Input.Status = "Novo";
            issue.Input.AssigneeId = null;
            if (source != destination) issue.Input.RoadmapItemId = null;
            issue.Input.Revision = 0;
            var copy = await SaveInTransaction(connection, transaction, user, destination, null, issue.Input);
            await transaction.CommitAsync();
            return copy;
        }

        await using var command = new SqlCommand("""
            UPDATE dbo.Issues SET ProjectId=@destination, RoadmapItemId=NULL, Revision=Revision+1,
                AssigneeId=CASE WHEN EXISTS (SELECT 1 FROM dbo.ProjectMembers WITH (HOLDLOCK)
                    WHERE ProjectId=@destination AND UserId=dbo.Issues.AssigneeId) THEN AssigneeId END
            WHERE Id=@id
            """, connection, transaction);
        command.Parameters.AddRange([P("@id",id), P("@destination",destination)]);
        await command.ExecuteNonQueryAsync();
        await Event(connection, transaction, id, user, "moved",
            JsonSerializer.Serialize(new { FromProjectId = source, ToProjectId = destination, PreviousRoadmapItemId = issue.Input.RoadmapItemId }));
        await transaction.CommitAsync();
        return id;
    }

    internal static async Task RequireIssueProject(SqlConnection connection, SqlTransaction transaction, int id, int project)
    {
        await using var command = new SqlCommand(
            "SELECT 1 FROM dbo.Issues WITH (HOLDLOCK) WHERE Id=@id AND ProjectId=@project", connection, transaction);
        command.Parameters.AddRange([P("@id", id), P("@project", project)]);
        if (await command.ExecuteScalarAsync() is null) throw new UnauthorizedAccessException();
    }

    private static async Task Event(SqlConnection c,SqlTransaction tx,int id,int user,string kind,string text){await using var cmd=new SqlCommand("""
        INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) VALUES(@id,@user,@kind,@text);
        DECLARE @eventId int=CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.IssueNotifications(EventId,IssueId,UserId)
        SELECT @eventId,@id,w.UserId
        FROM dbo.IssueWatchers w WITH (HOLDLOCK)
        WHERE w.IssueId=@id AND w.UserId<>@user;
        """,c,tx);cmd.Parameters.AddRange([P("@id",id),P("@user",user),P("@kind",kind),P("@text",text)]);await cmd.ExecuteNonQueryAsync();}
    public async Task Comment(int user,int id,string text)
    {
        var issue=await Get(user,id)??throw new UnauthorizedAccessException();if(!await Can(user,issue.ProjectId,"issues.comment"))throw new UnauthorizedAccessException();if(string.IsNullOrWhiteSpace(text)||text.Length>4000)throw new ArgumentException("Komentar mora imati 1–4000 znakova.");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();await RequireActive(c,tx,issue.ProjectId);await RequireIssueProject(c,tx,id,issue.ProjectId);await Event(c,tx,id,user,"comment",text.Trim());await tx.CommitAsync();
    }
    public async Task LogTime(int user,int id,DateTime date,decimal hours,string? note,string? activity=null)
    {
        var issue=await Get(user,id)??throw new UnauthorizedAccessException();if(!await Can(user,issue.ProjectId,"time.log"))throw new UnauthorizedAccessException();if(hours<=0||hours>24||decimal.Round(hours,2)!=hours||date.Year<1753||(note?.Length??0)>1000)throw new ArgumentException("Unesi datum i sate od 0,01 do 24 (najviše dvije decimale).");
        activity = string.IsNullOrWhiteSpace(activity) ? "Općenito" : activity.Trim();
        if (activity.Length > 60) throw new ArgumentException("Aktivnost može imati najviše 60 znakova.");
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();await RequireActive(c,tx,issue.ProjectId,true);await RequireIssueProject(c,tx,id,issue.ProjectId);await using var cmd=new SqlCommand("INSERT dbo.TimeEntries(IssueId,UserId,WorkDate,Hours,Note,Activity) VALUES(@id,@user,@date,@hours,@note,@activity)",c,tx);cmd.Parameters.AddRange([P("@id",id),P("@user",user),P("@date",date.Date),P("@hours",hours),P("@note",note??""),P("@activity",activity)]);await cmd.ExecuteNonQueryAsync();await Event(c,tx,id,user,"time",$"{date:yyyy-MM-dd}: {hours} h — {note}");await tx.CommitAsync();
    }
    public async Task<(List<IssueEvent> Events,List<TimeEntry> Time)> Activity(int user,int id)
    {
        var issue=await Get(user,id)??throw new UnauthorizedAccessException();var showTime=await Can(user,issue.ProjectId,"time.view");await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT u.DisplayName,e.Kind,e.Body,e.CreatedAt FROM dbo.IssueEvents e JOIN dbo.AppUsers u ON u.Id=e.UserId WHERE e.IssueId=@id AND EXISTS(SELECT 1 FROM dbo.Issues i WHERE i.Id=@id AND i.ProjectId=@project) AND (@showTime=1 OR e.Kind<>'time') ORDER BY e.Id DESC; SELECT u.DisplayName,t.WorkDate,t.Hours,t.Note,t.Activity FROM dbo.TimeEntries t JOIN dbo.AppUsers u ON u.Id=t.UserId WHERE t.IssueId=@id AND EXISTS(SELECT 1 FROM dbo.Issues i WHERE i.Id=@id AND i.ProjectId=@project) AND @showTime=1 ORDER BY t.WorkDate DESC",c);cmd.Parameters.Add(P("@id",id));cmd.Parameters.Add(P("@showTime",showTime));cmd.Parameters.Add(P("@project",issue.ProjectId));await using var r=await cmd.ExecuteReaderAsync();var events=new List<IssueEvent>();var time=new List<TimeEntry>();while(await r.ReadAsync())events.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetDateTime(3)));await r.NextResultAsync();while(await r.ReadAsync())time.Add(new(r.GetString(0),r.GetDateTime(1),r.GetDecimal(2),r.GetString(3),r.GetString(4)));return(events,time);
    }

    public async Task<List<TimeReportRow>> TimeReport(
        IEnumerable<int> projectIds, DateTime? from, DateTime? to, string? person, string? activity)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT p.Name,u.DisplayName,u.EmployeeNumber,t.Activity,SUM(t.Hours)
            FROM dbo.TimeEntries t
            JOIN dbo.Issues i ON i.Id=t.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            JOIN dbo.AppUsers u ON u.Id=t.UserId
            JOIN OPENJSON(@projects) WITH (Id int '$') permitted ON permitted.Id=p.Id
            WHERE p.IssuesEnabled=1 AND p.TimeEnabled=1
              AND (@from IS NULL OR t.WorkDate>=@from) AND (@to IS NULL OR t.WorkDate<=@to)
              AND (@person='' OR CHARINDEX(@person,u.DisplayName)>0 OR CHARINDEX(@person,u.EmployeeNumber)>0)
              AND (@activity='' OR CHARINDEX(@activity,t.Activity)>0)
            GROUP BY p.Name,u.Id,u.DisplayName,u.EmployeeNumber,t.Activity
            ORDER BY p.Name,u.DisplayName,u.EmployeeNumber,t.Activity
            """, connection);
        command.Parameters.Add(new SqlParameter("@projects", System.Data.SqlDbType.NVarChar, -1)
            { Value = JsonSerializer.Serialize(projectIds) });
        command.Parameters.Add(new SqlParameter("@from", System.Data.SqlDbType.Date)
            { Value = (object?)from ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@to", System.Data.SqlDbType.Date)
            { Value = (object?)to ?? DBNull.Value });
        command.Parameters.Add(P("@person", person ?? ""));
        command.Parameters.Add(P("@activity", activity ?? ""));

        var rows = new List<TimeReportRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDecimal(4)));
        return rows;
    }

    public async Task<List<ActivityRow>> RecentActivity(IEnumerable<int> projectIds)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP (50) e.Id,i.Id,i.Title,p.Name,u.DisplayName,e.Kind,e.CreatedAt
            FROM dbo.IssueEvents e
            JOIN dbo.Issues i ON i.Id=e.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            JOIN dbo.AppUsers u ON u.Id=e.UserId
            JOIN OPENJSON(@projects) WITH (Id int '$') allowed ON allowed.Id=p.Id
            WHERE p.IssuesEnabled=1 AND (e.Kind<>'time' OR p.TimeEnabled=1)
            ORDER BY e.CreatedAt DESC,e.Id DESC
            """, connection);
        command.Parameters.Add(new SqlParameter("@projects", System.Data.SqlDbType.NVarChar, -1)
        {
            Value = JsonSerializer.Serialize(projectIds)
        });
        var rows = new List<ActivityRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));
        return rows;
    }
}

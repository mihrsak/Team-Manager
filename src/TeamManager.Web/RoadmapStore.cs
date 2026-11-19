using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record RoadmapItem(int Id, string Kind, string Name, string Description,
    DateTime? TargetDate, bool IsComplete, int Revision);

public sealed record RoadmapMatch(int ProjectId, string Project, int Id, string Kind, string Name);

public sealed record RoadmapStatusCount(int ItemId, string Status, bool IsClosed, int Count);

public sealed class RoadmapStore(IConfiguration configuration, ProjectStore projects,OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<RoadmapItem>> List(int project)
    {
        if(oracle.Enabled)return await oracle.Roadmap(project);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id,Kind,Name,Description,TargetDate,IsComplete,Revision
            FROM dbo.ProjectMilestones WHERE ProjectId=@project
            ORDER BY IsComplete, CASE WHEN TargetDate IS NULL THEN 1 ELSE 0 END, TargetDate, Name, Id
            """, connection);
        command.Parameters.Add(P("@project", project));
        var items = new List<RoadmapItem>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4), reader.GetBoolean(5), reader.GetInt32(6)));
        return items;
    }

    public async Task<List<RoadmapMatch>> Search(IEnumerable<int> visibleProjects, string query)
    {
        if(oracle.Enabled)return await oracle.RoadmapSearchSql(visibleProjects,query);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP (20) m.ProjectId,p.Name,m.Id,m.Kind,m.Name
            FROM dbo.ProjectMilestones m
            JOIN dbo.Projects p ON p.Id=m.ProjectId
            JOIN OPENJSON(@projects) WITH (Id int '$') allowed ON allowed.Id=p.Id
            WHERE CHARINDEX(@query,m.Name)>0 OR CHARINDEX(@query,m.Description)>0
            ORDER BY m.Name,p.Name,m.Id
            """, connection);
        command.Parameters.Add(new SqlParameter("@projects", System.Data.SqlDbType.NVarChar, -1)
        {
            Value = System.Text.Json.JsonSerializer.Serialize(visibleProjects)
        });
        command.Parameters.Add(P("@query", query));
        var matches = new List<RoadmapMatch>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            matches.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
        return matches;
    }

    public async Task<ILookup<int, RoadmapStatusCount>> Progress(int user, int project)
    {
        if(oracle.Enabled)return await oracle.RoadmapProgressSql(user,project);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT i.RoadmapItemId,i.Status,s.IsClosed,COUNT(*)
            FROM dbo.Issues i
            JOIN dbo.IssueStatuses s ON s.Name=i.Status
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            WHERE i.ProjectId=@project AND p.IssuesEnabled=1 AND i.RoadmapItemId IS NOT NULL
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
            GROUP BY i.RoadmapItemId,i.Status,s.IsClosed
            ORDER BY s.IsClosed,i.Status OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.Add(P("@project", project));
        command.Parameters.Add(P("@viewer",user));
        var counts = new List<RoadmapStatusCount>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            counts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3)));
        return counts.ToLookup(count => count.ItemId);
    }

    public async Task Save(int user, int project, RoadmapItem item)
    {
        if(oracle.Enabled){await oracle.WriteProjectData(user,project,"roadmap",w=>{
            if(!OracleWorkspace.Manage(w,user,project))throw new UnauthorizedAccessException();
            if(w.Find("Projects",project)is not {} p||OracleWorkspace.Workspace.Text(p,"State")!="Active")
                throw new InvalidOperationException("Plan je u međuvremenu izmijenjen ili projekt više nije aktivan.");
            var row=item.Id==0?null:w.Find("ProjectMilestones",item.Id);
            if(item.Id!=0&&(row is null||OracleWorkspace.Workspace.Int(row,"ProjectId")!=project||
                OracleWorkspace.Workspace.Int(row,"Revision")!=item.Revision))throw new InvalidOperationException("Plan je u međuvremenu izmijenjen ili projekt više nije aktivan.");
            if(w.Rows("ProjectMilestones").Any(m=>OracleWorkspace.Workspace.Id(m)!=item.Id&&OracleWorkspace.Workspace.Int(m,"ProjectId")==project&&
                OracleWorkspace.Workspace.Text(m,"Kind")==item.Kind&&OracleWorkspace.Workspace.Text(m,"Name")==item.Name))
                throw new ArgumentException("Taj naziv već postoji za odabranu vrstu.");
            row??=w.Add("ProjectMilestones",("Id",w.Next("ProjectMilestones")),("ProjectId",project),("Revision",-1));
            OracleWorkspace.Workspace.Set(row,"Kind",item.Kind);OracleWorkspace.Workspace.Set(row,"Name",item.Name);
            OracleWorkspace.Workspace.Set(row,"Description",item.Description);OracleWorkspace.Workspace.Set(row,"TargetDate",item.TargetDate);
            OracleWorkspace.Workspace.Set(row,"IsComplete",item.IsComplete);OracleWorkspace.Workspace.Revise(row);
            OracleWorkspace.Audit(w,user,item.Id==0?"roadmap.create":"roadmap.update",item.Name);return true;
        });return;}
        if (!await projects.CanManage(user, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        var sql = item.Id == 0
            ? """
              INSERT dbo.ProjectMilestones(ProjectId,Kind,Name,Description,TargetDate,IsComplete)
              SELECT Id,@kind,@name,@description,@date,@complete FROM dbo.Projects
              WHERE Id=@project AND State='Active'
              """
            : """
              UPDATE m SET Kind=@kind, Name=@name, Description=@description, TargetDate=@date,
                  IsComplete=@complete, Revision=m.Revision+1
              FROM dbo.ProjectMilestones m JOIN dbo.Projects p ON p.Id=m.ProjectId
              WHERE m.ProjectId=@project AND m.Id=@id AND m.Revision=@revision AND p.State='Active'
              """;

        var rows = await AdministrationAuditStore.Execute(connection,
            new(user,item.Id==0?"roadmap.create":"roadmap.update",item.Name,$"Projekt #{project}; {item.Kind}"),sql,
            P("@project", project), P("@id", item.Id), P("@revision", item.Revision),
            P("@kind", item.Kind), P("@name", item.Name), P("@description", item.Description), P("@complete", item.IsComplete),
            new SqlParameter("@date", System.Data.SqlDbType.Date) { Value = (object?)item.TargetDate ?? DBNull.Value });
        if (rows != 1)
            throw new InvalidOperationException("Plan je u međuvremenu izmijenjen ili projekt više nije aktivan.");
    }
}

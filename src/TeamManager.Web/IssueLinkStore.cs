using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record IssueLink(int OtherId, string Title, string Kind, bool Outgoing, bool CanEdit);

public sealed class IssueLinkStore(IConfiguration config, IssueStore issues,OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<List<IssueLink>> List(int user, int id, int project)
    {
        if(oracle.Enabled)return await oracle.Links(user,id,project);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT other.Id,other.Title,other.ProjectId,l.Kind,
                CAST(CASE WHEN l.SourceId=@id THEN 1 ELSE 0 END AS bit)
            FROM dbo.IssueLinks l
            JOIN dbo.Issues other ON other.Id=CASE WHEN l.SourceId=@id THEN l.TargetId ELSE l.SourceId END
            WHERE (l.SourceId=@id OR l.TargetId=@id)
              AND EXISTS(SELECT 1 FROM dbo.Issues currentIssue WHERE currentIssue.Id=@id AND currentIssue.ProjectId=@project
                AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,currentIssue.ProjectId,currentIssue.IsPrivate,currentIssue.CreatedBy,currentIssue.AssigneeId)))
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,other.ProjectId,other.IsPrivate,other.CreatedBy,other.AssigneeId))
            ORDER BY l.Kind,other.Id OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@id", id), P("@project", project), P("@viewer",user)]);

        var candidates = new List<(int Id, string Title, int Project, string Kind, bool Outgoing)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                candidates.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetBoolean(4)));

        var rights = new Dictionary<int, (bool View, bool Edit)>();
        var result = new List<IssueLink>();
        foreach (var candidate in candidates)
        {
            if (!rights.TryGetValue(candidate.Project, out var access))
            {
                access = (await issues.Can(user, candidate.Project, "issues.view"), await issues.Can(user, candidate.Project, "issues.edit"));
                rights.Add(candidate.Project, access);
            }
            if (access.View)
                result.Add(new(candidate.Id, candidate.Title, candidate.Kind, candidate.Outgoing, access.Edit));
        }
        return result;
    }

    public async Task<ILookup<int, int>> Dependencies(int user, IEnumerable<int> visibleIds)
    {
        if(oracle.Enabled)return await oracle.Read(w=>{
            var ids=visibleIds.ToHashSet();
            return w.Rows("IssueLinks").Where(l=>OracleWorkspace.Workspace.Text(l,"Kind")=="blocks"&&ids.Contains(OracleWorkspace.Workspace.Int(l,"SourceId"))&&
                ids.Contains(OracleWorkspace.Workspace.Int(l,"TargetId"))&&w.Find("Issues",OracleWorkspace.Workspace.Int(l,"SourceId"))is {} source&&OracleWorkspace.Visible(w,user,source)&&
                w.Find("Issues",OracleWorkspace.Workspace.Int(l,"TargetId"))is {} target&&OracleWorkspace.Visible(w,user,target))
                .ToLookup(l=>OracleWorkspace.Workspace.Int(l,"TargetId"),l=>OracleWorkspace.Workspace.Int(l,"SourceId"));
        });
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            WITH Visible AS (SELECT Id FROM OPENJSON(@issues) WITH (Id int '$'))
            SELECT l.TargetId,l.SourceId FROM dbo.IssueLinks l
            JOIN Visible s ON s.Id=l.SourceId JOIN Visible t ON t.Id=l.TargetId
            JOIN dbo.Issues source ON source.Id=l.SourceId JOIN dbo.Issues target ON target.Id=l.TargetId
            WHERE l.Kind='blocks'
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,source.ProjectId,source.IsPrivate,source.CreatedBy,source.AssigneeId))
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,target.ProjectId,target.IsPrivate,target.CreatedBy,target.AssigneeId))
            ORDER BY l.SourceId OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.Add(P("@viewer",user));
        command.Parameters.Add(new SqlParameter("@issues", System.Data.SqlDbType.NVarChar, -1)
        {
            Value = System.Text.Json.JsonSerializer.Serialize(visibleIds)
        });
        await using var reader = await command.ExecuteReaderAsync();
        var dependencies = new List<(int Target, int Source)>();
        while (await reader.ReadAsync())
            dependencies.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return dependencies.ToLookup(link => link.Target, link => link.Source);
    }

    public async Task Set(int user, int id, int otherId, string kind, bool outgoing, bool remove)
    {
        if(oracle.Enabled){await oracle.SetLink(user,id,otherId,kind,outgoing,remove);return;}
        if (id == otherId || kind is not ("related" or "duplicate" or "blocks" or "parent"))
            throw new ArgumentException("Odaberi drugi zadatak i valjanu vrstu veze.");

        var first = await issues.Get(user, id);
        var second = await issues.Get(user, otherId);
        if (first is null || second is null ||
            !await issues.Can(user, first.ProjectId, "issues.edit") ||
            !await issues.Can(user, second.ProjectId, "issues.edit"))
            throw new UnauthorizedAccessException();

        var source = outgoing ? id : otherId;
        var target = outgoing ? otherId : id;
        if (kind is "related" or "duplicate")
            (source, target) = (Math.Min(id, otherId), Math.Max(id, otherId));

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var mutex = new SqlCommand("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource='TeamManager.IssueLinks',
                @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            IF @result<0 THROW 51015,'Issue links are busy',1;
            """, connection, transaction);
        await mutex.ExecuteNonQueryAsync();

        foreach (var project in new[] { first.ProjectId, second.ProjectId }.Distinct().Order())
            await IssueStore.RequireActive(connection, transaction, project);
        await IssueStore.RequireIssueProject(connection, transaction, first.Id, first.ProjectId, user);
        await IssueStore.RequireIssueProject(connection, transaction, second.Id, second.ProjectId, user);

        await using var command = new SqlCommand(remove
            ? "DELETE dbo.IssueLinks WHERE SourceId=@source AND TargetId=@target AND Kind=@kind"
            : """
              IF EXISTS(SELECT 1 FROM dbo.IssueLinks WHERE SourceId=@source AND TargetId=@target AND Kind=@kind)
                  RETURN;
              IF @kind='parent' AND EXISTS(SELECT 1 FROM dbo.IssueLinks WHERE TargetId=@target AND Kind='parent')
                  THROW 51016,'Issue already has a parent',1;
              IF @kind IN ('parent','blocks')
              BEGIN
                  DECLARE @visited TABLE(Id int PRIMARY KEY, Expanded bit NOT NULL DEFAULT 0);
                  INSERT @visited(Id) VALUES(@target);
                  DECLARE @node int;
                  WHILE EXISTS(SELECT 1 FROM @visited WHERE Expanded=0)
                  BEGIN
                      SELECT TOP (1) @node=Id FROM @visited WHERE Expanded=0;
                      IF @node=@source THROW 51017,'Issue link would create a cycle',1;
                      UPDATE @visited SET Expanded=1 WHERE Id=@node;
                      INSERT @visited(Id)
                      SELECT l.TargetId FROM dbo.IssueLinks l WHERE l.SourceId=@node AND l.Kind=@kind
                          AND NOT EXISTS(SELECT 1 FROM @visited v WHERE v.Id=l.TargetId);
                  END
              END
              INSERT dbo.IssueLinks(SourceId,TargetId,Kind) VALUES(@source,@target,@kind);
              """, connection, transaction);
        command.Parameters.AddRange([P("@source", source), P("@target", target), P("@kind", kind)]);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}

using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record RoadmapItem(int Id, string Kind, string Name, string Description,
    DateTime? TargetDate, bool IsComplete, int Revision);

public sealed class RoadmapStore(IConfiguration configuration, ProjectStore projects)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<RoadmapItem>> List(int project)
    {
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

    public async Task Save(int user, int project, RoadmapItem item)
    {
        if (!await projects.CanManage(user, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        await connection.OpenAsync();
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

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([P("@project", project), P("@id", item.Id), P("@revision", item.Revision),
            P("@kind", item.Kind), P("@name", item.Name), P("@description", item.Description), P("@complete", item.IsComplete),
            new SqlParameter("@date", System.Data.SqlDbType.Date) { Value = (object?)item.TargetDate ?? DBNull.Value }]);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Plan je u međuvremenu izmijenjen ili projekt više nije aktivan.");
    }
}

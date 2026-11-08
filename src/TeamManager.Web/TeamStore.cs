using Microsoft.Data.SqlClient;

namespace TeamManager.Web;

public record Team(int Id, string Name);
public record Member(int Id, int TeamId, string Name);
public record WorkItem(int Id, string Title, string Team, string? Assignee, string Status, DateTime? DueDate);

public sealed class TeamStore(IConfiguration configuration)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager")
        ?? throw new InvalidOperationException("Nije postavljena veza s bazom."));

    public async Task<(List<Team> Teams, List<Member> Members, List<WorkItem> Tasks)> Load()
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Id, Name FROM dbo.Teams ORDER BY Name; SELECT Id, TeamId, Name FROM dbo.Members ORDER BY Name; SELECT w.Id,w.Title,t.Name,m.Name,w.Status,w.DueDate FROM dbo.WorkItems w JOIN dbo.Teams t ON t.Id=w.TeamId LEFT JOIN dbo.Members m ON m.Id=w.AssigneeId ORDER BY w.Id DESC;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var teams = new List<Team>();
        var members = new List<Member>();
        var tasks = new List<WorkItem>();
        while (await reader.ReadAsync()) teams.Add(new(reader.GetInt32(0), reader.GetString(1)));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) members.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) tasks.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5)));
        return (teams, members, tasks);
    }

    public async Task Execute(string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}

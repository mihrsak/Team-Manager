using System.Text.Json;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record PrivateIssueNote(string Body, DateTime CreatedAt);
public sealed record IssueNotification(int EventId, int IssueId, string Title, string Project, string Kind, DateTime At, bool IsRead);

public sealed class IssuePersonalStore(IConfiguration config, IssueStore issues)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<List<PrivateIssueNote>> Notes(int user, int issue, int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT n.Body,n.CreatedAt FROM dbo.IssuePrivateNotes n
            JOIN dbo.Issues i ON i.Id=n.IssueId
            WHERE n.UserId=@user AND n.IssueId=@issue AND i.ProjectId=@project
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@user,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
            ORDER BY n.Id DESC OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@user", user), P("@issue", issue), P("@project", project)]);
        var notes = new List<PrivateIssueNote>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            notes.Add(new(reader.GetString(0), reader.GetDateTime(1)));
        return notes;
    }

    public async Task<bool> CanAddNote(int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT 1 FROM dbo.Projects WHERE Id=@project AND State='Active' AND IssuesEnabled=1", connection);
        command.Parameters.Add(P("@project", project));
        return await command.ExecuteScalarAsync() is not null;
    }

    public async Task AddNote(int user, int id, string? text)
    {
        var issue = await issues.Get(user, id) ?? throw new UnauthorizedAccessException();
        text = text?.Trim();
        if (text is null or { Length: 0 or > 4000 })
            throw new ArgumentException("Bilješka mora imati od 1 do 4000 znakova.");
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await IssueStore.RequireActive(connection, transaction, issue.ProjectId);
        await IssueStore.RequireIssueProject(connection, transaction, id, issue.ProjectId, user);
        await using var command = new SqlCommand("""
            INSERT dbo.IssuePrivateNotes(IssueId,UserId,Body) VALUES(@issue,@user,@body)
            """, connection, transaction);
        command.Parameters.AddRange([P("@issue", id), P("@user", user), P("@body", text)]);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<List<IssueNotification>> Notifications(int user)
    {
        var allowed = await issues.Projects(user);
        if (allowed.Count == 0) return [];
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP (50) n.EventId,i.Id,i.Title,p.Name,e.Kind,e.CreatedAt,n.IsRead
            FROM dbo.IssueNotifications n
            JOIN dbo.IssueEvents e ON e.Id=n.EventId
            JOIN dbo.Issues i ON i.Id=n.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            JOIN OPENJSON(@projects) WITH (Id int '$') allowed ON allowed.Id=p.Id
            WHERE n.UserId=@user AND p.IssuesEnabled=1 AND (e.Kind<>'time' OR p.TimeEnabled=1)
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@user,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
            ORDER BY n.IsRead,e.CreatedAt DESC,n.EventId DESC OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.Add(P("@user", user));
        command.Parameters.Add(ProjectFilter(allowed));
        var notifications = new List<IssueNotification>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            notifications.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDateTime(5), reader.GetBoolean(6)));
        return notifications;
    }

    public async Task<bool> MarkRead(int user, int eventId)
    {
        var allowed = await issues.Projects(user);
        if (allowed.Count == 0) return false;
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE n SET IsRead=1
            FROM dbo.IssueNotifications n
            JOIN dbo.IssueEvents e ON e.Id=n.EventId
            JOIN dbo.Issues i ON i.Id=n.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            JOIN OPENJSON(@projects) WITH (Id int '$') allowed ON allowed.Id=p.Id
            WHERE n.UserId=@user AND n.EventId=@event AND p.IssuesEnabled=1
              AND (e.Kind<>'time' OR p.TimeEnabled=1)
              AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@user,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
            OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@user", user), P("@event", eventId), ProjectFilter(allowed)]);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static SqlParameter ProjectFilter(IEnumerable<Team> projects)
        => new("@projects", System.Data.SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(projects.Select(project => project.Id)) };
}

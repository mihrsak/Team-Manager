using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record ForumTopic(int Id, string Title, string Body, string Author, DateTime At, bool IsLocked);
public sealed record ForumReply(string Body, string Author, DateTime At);

public sealed class ProjectForumStore(IConfiguration config, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<List<ForumTopic>> Topics(int user, int project, int? topic)
    {
        if (oracle.Enabled) return await oracle.ForumTopicsRead(user, project, topic);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT t.Id,t.Title,CASE WHEN @topic IS NULL THEN N'' ELSE t.Body END,u.DisplayName,t.CreatedAt,t.IsLocked
            FROM dbo.ForumTopics t JOIN dbo.AppUsers u ON u.Id=t.AuthorId
            WHERE t.ProjectId=@project AND (@topic IS NULL OR t.Id=@topic)
            ORDER BY t.Id DESC
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@topic", topic)]);
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<ForumTopic>();
        while (await reader.ReadAsync())
            items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4), reader.GetBoolean(5)));
        return items;
    }

    public async Task<List<ForumReply>> Replies(int user, int project, int topic)
    {
        if (oracle.Enabled) return await oracle.ForumRepliesRead(user, project, topic);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT r.Body,u.DisplayName,r.CreatedAt
            FROM dbo.ForumReplies r JOIN dbo.ForumTopics t ON t.Id=r.TopicId
            JOIN dbo.AppUsers u ON u.Id=r.AuthorId
            WHERE t.Id=@topic AND t.ProjectId=@project ORDER BY r.Id
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@topic", topic)]);
        await using var reader = await command.ExecuteReaderAsync();
        var replies = new List<ForumReply>();
        while (await reader.ReadAsync()) replies.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        return replies;
    }

    public async Task<int?> Create(int actor, int project, string title, string body)
    {
        if (oracle.Enabled) return await oracle.CreateForumTopic(actor, project, title, body);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.ForumTopics(ProjectId,AuthorId,Title,Body) OUTPUT inserted.Id
            SELECT Id,@actor,@title,@body FROM dbo.Projects WITH(HOLDLOCK)
            WHERE Id=@project AND State='Active'
            """, connection);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@title", title), P("@body", body)]);
        return (int?)await command.ExecuteScalarAsync();
    }

    public Task<bool> Reply(int actor, int project, int topic, string body)
        => oracle.Enabled ? oracle.ReplyForum(actor, project, topic, body) : Change("""
            INSERT dbo.ForumReplies(TopicId,AuthorId,Body)
            SELECT t.Id,@actor,@body FROM dbo.ForumTopics t WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Projects p WITH(HOLDLOCK) ON p.Id=t.ProjectId
            WHERE t.Id=@topic AND t.ProjectId=@project AND t.IsLocked=0 AND p.State='Active'
            """, P("@actor", actor), P("@project", project), P("@topic", topic), P("@body", body));

    public Task<bool> SetLocked(int actor, int project, int topic, bool locked)
        => oracle.Enabled ? oracle.LockForum(actor, project, topic, locked) : Change("""
            UPDATE t SET IsLocked=@locked FROM dbo.ForumTopics t
            JOIN dbo.Projects p WITH(HOLDLOCK) ON p.Id=t.ProjectId
            WHERE t.Id=@topic AND t.ProjectId=@project AND p.State='Active'
            """, P("@project", project), P("@topic", topic), P("@locked", locked));

    private async Task<bool> Change(string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync() == 1;
    }
}

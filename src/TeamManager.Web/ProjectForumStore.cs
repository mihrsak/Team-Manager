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
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using (var target = new SqlCommand("SELECT Id FROM dbo.Projects WITH(UPDLOCK,HOLDLOCK) WHERE Id=@project", connection, transaction))
        {
            target.Parameters.Add(P("@project", project));
            if (await target.ExecuteScalarAsync() is null) return null;
        }
        if (!await CanWrite(connection, transaction, actor, project, "forum.post")) return null;
        await using var command = new SqlCommand("""
            INSERT dbo.ForumTopics(ProjectId,AuthorId,Title,Body) OUTPUT inserted.Id
            VALUES(@project,@actor,@title,@body)
            """, connection, transaction);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@title", title), P("@body", body)]);
        var id = (int?)await command.ExecuteScalarAsync();
        await transaction.CommitAsync();
        return id;
    }

    public Task<bool> Reply(int actor, int project, int topic, string body)
        => oracle.Enabled ? oracle.ReplyForum(actor, project, topic, body) : Change(actor, project, topic, "forum.post", true,
            "INSERT dbo.ForumReplies(TopicId,AuthorId,Body) VALUES(@topic,@actor,@body)", P("@body", body));

    public Task<bool> SetLocked(int actor, int project, int topic, bool locked)
        => oracle.Enabled ? oracle.LockForum(actor, project, topic, locked) : Change(actor, project, topic, "forum.moderate", false,
            "UPDATE dbo.ForumTopics SET IsLocked=@locked WHERE Id=@topic AND ProjectId=@project", P("@locked", locked));

    private async Task<bool> Change(int actor, int project, int topic, string permission, bool requireUnlocked, string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using (var target = new SqlCommand("SELECT IsLocked FROM dbo.ForumTopics WITH(UPDLOCK,HOLDLOCK) WHERE Id=@topic AND ProjectId=@project", connection, transaction))
        {
            target.Parameters.AddRange([P("@topic", topic), P("@project", project)]);
            var locked = await target.ExecuteScalarAsync();
            if (locked is null || requireUnlocked && Convert.ToBoolean(locked)) return false;
        }
        if (!await CanWrite(connection, transaction, actor, project, permission)) return false;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@topic", topic)]);
        command.Parameters.AddRange(parameters);
        var changed = await command.ExecuteNonQueryAsync() == 1;
        await transaction.CommitAsync();
        return changed;
    }

    private static async Task<bool> CanWrite(SqlConnection connection, SqlTransaction transaction, int actor, int project, string permission)
    {
        // Check after acquiring the target lock and retain the authorization reads
        // through commit, including inherited and group-derived project rights.
        await using var command = new SqlCommand("""
            WITH rights AS (
                SELECT p.Permission FROM dbo.EffectiveUserRoles r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName WHERE r.UserId=@actor
                UNION
                SELECT p.Permission FROM dbo.EffectiveProjectRoles(@project,@actor) r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName
            )
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.AppUsers WHERE Id=@actor AND IsActive=1)
                AND EXISTS(SELECT 1 FROM dbo.Projects WHERE Id=@project AND State='Active'
                    AND (IsPublic=1 OR EXISTS(SELECT 1 FROM rights WHERE Permission IN ('*','issues.view','projects.manage'))))
                AND EXISTS(SELECT 1 FROM rights WHERE Permission IN ('*',@permission))
                THEN 1 ELSE 0 END OPTION(MAXRECURSION 0)
            """, connection, transaction);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@permission", permission)]);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}

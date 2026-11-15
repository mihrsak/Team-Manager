using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record ProjectNewsItem(int Id, string Title, string Body, string Author, DateTime At);
public sealed record ProjectNewsComment(string Body, string Author, DateTime At);

public sealed class ProjectNewsStore(IConfiguration configuration)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<ProjectNewsItem>> List(int project, int? newsId)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT n.Id,n.Title,CASE WHEN @news IS NULL THEN N'' ELSE n.Body END,u.DisplayName,n.CreatedAt
            FROM dbo.ProjectNews n JOIN dbo.AppUsers u ON u.Id=n.AuthorId
            WHERE n.ProjectId=@project AND (@news IS NULL OR n.Id=@news)
            ORDER BY n.Id DESC
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@news", newsId)]);
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<ProjectNewsItem>();
        while (await reader.ReadAsync())
            items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4)));
        return items;
    }

    public async Task<List<ProjectNewsComment>> Comments(int project, int newsId)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT c.Body,u.DisplayName,c.CreatedAt FROM dbo.ProjectNewsComments c
            JOIN dbo.ProjectNews n ON n.Id=c.NewsId JOIN dbo.AppUsers u ON u.Id=c.AuthorId
            WHERE n.ProjectId=@project AND n.Id=@news ORDER BY c.Id
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@news", newsId)]);
        await using var reader = await command.ExecuteReaderAsync();
        var comments = new List<ProjectNewsComment>();
        while (await reader.ReadAsync())
            comments.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        return comments;
    }

    public async Task<int?> Publish(int actor, int project, string title, string body)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.ProjectNews(ProjectId,AuthorId,Title,Body) OUTPUT inserted.Id
            SELECT Id,@actor,@title,@body FROM dbo.Projects WITH (HOLDLOCK)
            WHERE Id=@project AND State='Active'
            """, connection);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@title", title), P("@body", body)]);
        return (int?)await command.ExecuteScalarAsync();
    }

    public async Task<bool> Comment(int actor, int project, int newsId, string body)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.ProjectNewsComments(NewsId,AuthorId,Body)
            SELECT n.Id,@actor,@body FROM dbo.ProjectNews n
            JOIN dbo.Projects p WITH (HOLDLOCK) ON p.Id=n.ProjectId
            WHERE n.Id=@news AND p.Id=@project AND p.State='Active'
            """, connection);
        command.Parameters.AddRange([P("@actor", actor), P("@project", project), P("@news", newsId), P("@body", body)]);
        return await command.ExecuteNonQueryAsync() == 1;
    }
}

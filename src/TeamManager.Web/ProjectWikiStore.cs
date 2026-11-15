using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record WikiPage(int Id, string Title, int Revision);
public sealed record WikiRevision(int Number, string Title, string Body, string Author, DateTime At);
public sealed record WikiRevisionInfo(int Number, string Author, DateTime At);

public sealed class ProjectWikiStore(IConfiguration config)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<List<WikiPage>> Pages(int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Id,Title,Revision FROM dbo.ProjectWikiPages WHERE ProjectId=@project ORDER BY Title", connection);
        command.Parameters.Add(P("@project", project));
        await using var reader = await command.ExecuteReaderAsync();
        var pages = new List<WikiPage>();
        while (await reader.ReadAsync()) pages.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return pages;
    }

    public async Task<List<WikiRevisionInfo>> History(int project, int page)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT r.Revision,u.DisplayName,r.CreatedAt FROM dbo.ProjectWikiRevisions r
            JOIN dbo.ProjectWikiPages p ON p.Id=r.PageId JOIN dbo.AppUsers u ON u.Id=r.AuthorId
            WHERE p.ProjectId=@project AND p.Id=@page ORDER BY r.Revision DESC
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@page", page)]);
        await using var reader = await command.ExecuteReaderAsync();
        var revisions = new List<WikiRevisionInfo>();
        while (await reader.ReadAsync()) revisions.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2)));
        return revisions;
    }

    public async Task<WikiRevision?> Revision(int project, int page, int? revision)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT r.Revision,r.Title,r.Body,u.DisplayName,r.CreatedAt FROM dbo.ProjectWikiRevisions r
            JOIN dbo.ProjectWikiPages p ON p.Id=r.PageId JOIN dbo.AppUsers u ON u.Id=r.AuthorId
            WHERE p.ProjectId=@project AND p.Id=@page AND r.Revision=COALESCE(@revision,p.Revision)
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@page", page),
            new SqlParameter("@revision",System.Data.SqlDbType.Int){Value=(object?)revision??DBNull.Value}]);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDateTime(4))
            : null;
    }

    public async Task<int?> Save(int user, int project, int? page, int revision, string title, string body)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var state = new SqlCommand(
            "SELECT 1 FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@project AND State='Active'", connection, transaction);
        state.Parameters.Add(P("@project", project));
        if (await state.ExecuteScalarAsync() is null) throw new UnauthorizedAccessException();

        await using var update = new SqlCommand(page is null
            ? "INSERT dbo.ProjectWikiPages(ProjectId,Title) OUTPUT inserted.Id VALUES(@project,@title)"
            : """
              UPDATE dbo.ProjectWikiPages SET Title=@title,Revision=Revision+1 OUTPUT inserted.Id
              WHERE ProjectId=@project AND Id=@page AND Revision=@revision
              """, connection, transaction);
        update.Parameters.AddRange([P("@project", project), P("@page", page), P("@revision", revision), P("@title", title)]);
        if (await update.ExecuteScalarAsync() is not int id) return null;

        await using var insert = new SqlCommand("""
            INSERT dbo.ProjectWikiRevisions(PageId,Revision,Title,Body,AuthorId)
            VALUES(@page,@revision,@title,@body,@user)
            """, connection, transaction);
        insert.Parameters.AddRange([P("@page", id), P("@revision", page is null ? 1 : revision+1),
            P("@title", title), new SqlParameter("@body",System.Data.SqlDbType.NVarChar,-1){Value=body}, P("@user", user)]);
        await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return id;
    }
}

using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record ProjectDocument(int Id, string Title, string Description, string FileName, long Size, string Author, DateTime At);

public sealed class ProjectDocumentStore(IConfiguration config, ProjectStore projects)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<List<ProjectDocument>> List(int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT d.Id,d.Title,d.Description,d.FileName,DATALENGTH(d.Content),u.DisplayName,d.CreatedAt
            FROM dbo.ProjectDocuments d JOIN dbo.AppUsers u ON u.Id=d.AuthorId
            WHERE d.ProjectId=@project ORDER BY d.Id DESC
            """, connection);
        command.Parameters.Add(P("@project", project));
        await using var reader = await command.ExecuteReaderAsync();
        var documents = new List<ProjectDocument>();
        while (await reader.ReadAsync())
            documents.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                reader.GetInt64(4),reader.GetString(5),reader.GetDateTime(6)));
        return documents;
    }

    public async Task<AttachmentDownload?> Download(int project, int document)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT FileName,Content FROM dbo.ProjectDocuments WHERE ProjectId=@project AND Id=@document
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@document", document)]);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.GetString(0), reader.GetFieldValue<byte[]>(1)) : null;
    }

    public async Task Add(int actor, int project, string title, string description, IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await projects.CanManage(actor, project)) throw new UnauthorizedAccessException();
        var upload = await UploadedFile.Read(file, cancellationToken);
        var affected = await Mutate("""
            INSERT dbo.ProjectDocuments(ProjectId,AuthorId,Title,Description,FileName,Content)
            SELECT Id,@actor,@title,@description,@name,@content FROM dbo.Projects WITH(HOLDLOCK)
            WHERE Id=@project AND State='Active'
            """, P("@project", project), P("@actor", actor), P("@title", title), P("@description", description),
            P("@name", upload.Name), new SqlParameter("@content", System.Data.SqlDbType.VarBinary, -1) { Value = upload.Content });
        if (affected != 1) throw new UnauthorizedAccessException();
    }

    public async Task<bool> Remove(int actor, int project, int document)
    {
        if (!await projects.CanManage(actor, project)) throw new UnauthorizedAccessException();
        return await Mutate("""
            DELETE d FROM dbo.ProjectDocuments d JOIN dbo.Projects p WITH(HOLDLOCK) ON p.Id=d.ProjectId
            WHERE d.Id=@document AND p.Id=@project AND p.State='Active'
            """, P("@document", document), P("@project", project)) == 1;
    }

    private async Task<int> Mutate(string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }
}

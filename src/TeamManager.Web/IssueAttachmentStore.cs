using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record AttachmentInfo(int Id, string Name, long Size);
public sealed record AttachmentDownload(string Name, byte[] Content);

public sealed class IssueAttachmentStore(IConfiguration configuration, IssueStore issues)
{
    public const int MaxSize = 10 * 1024 * 1024;
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<AttachmentInfo>> List(int issue, int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT a.Id,a.FileName,DATALENGTH(a.Content)
            FROM dbo.IssueAttachments a JOIN dbo.Issues i ON i.Id=a.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            WHERE i.Id=@issue AND i.ProjectId=@project AND p.IssuesEnabled=1
            ORDER BY a.Id DESC
            """, connection);
        command.Parameters.AddRange([P("@issue", issue), P("@project", project)]);
        var files = new List<AttachmentInfo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            files.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2)));
        return files;
    }

    public async Task<AttachmentDownload?> Download(int user, int issue, int attachment)
    {
        var visible = await issues.Get(user, issue);
        if (visible is null) return null;
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT a.FileName,a.Content FROM dbo.IssueAttachments a JOIN dbo.Issues i ON i.Id=a.IssueId
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            WHERE a.Id=@attachment AND i.Id=@issue AND i.ProjectId=@project AND p.IssuesEnabled=1
            """, connection);
        command.Parameters.AddRange([P("@attachment", attachment), P("@issue", issue), P("@project", visible.ProjectId)]);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.GetString(0), reader.GetFieldValue<byte[]>(1)) : null;
    }

    public async Task Add(int user, int issue, IFormFile? file, CancellationToken cancellationToken)
    {
        var project = await EditableProject(user, issue);
        if (file is null || file.Length is < 1 or > MaxSize)
            throw new ArgumentException("Odaberi datoteku veličine od 1 bajta do 10 MiB.");

        var name = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        if (name.Length is < 1 or > 200 || name is "." or ".." || name.Any(char.IsControl))
            throw new ArgumentException("Naziv datoteke nije valjan.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != file.Length)
            throw new ArgumentException("Datoteka nije učitana u cijelosti.");
        await Mutate(project, issue, """
            INSERT dbo.IssueAttachments(IssueId,AuthorId,FileName,Content) VALUES(@issue,@user,@name,@content)
            """, P("@user", user), P("@name", name),
            new SqlParameter("@content", System.Data.SqlDbType.VarBinary, -1) { Value = buffer.ToArray() });
    }

    public async Task<bool> Remove(int user, int issue, int attachment)
    {
        var project = await EditableProject(user, issue);
        return await Mutate(project, issue,
            "DELETE dbo.IssueAttachments WHERE Id=@attachment AND IssueId=@issue", P("@attachment", attachment)) == 1;
    }

    private async Task<int> EditableProject(int user, int issue)
    {
        var current = await issues.Get(user, issue);
        if (current is null || !await issues.Can(user, current.ProjectId, "issues.edit"))
            throw new UnauthorizedAccessException();
        return current.ProjectId;
    }

    private async Task<int> Mutate(int project, int issue, string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await IssueStore.RequireActive(connection, transaction, project);
        await IssueStore.RequireIssueProject(connection, transaction, issue, project);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(P("@issue", issue));
        command.Parameters.AddRange(parameters);
        var affected = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return affected;
    }
}

using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record IssueField(int Id, string Name, string? Value);

public sealed class IssueFieldStore(IConfiguration configuration, IssueStore issues, ProjectStore projects)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<IssueField>> List(int project, int? issue = null)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT f.Id,f.Name,v.Value FROM dbo.ProjectIssueFields f
            LEFT JOIN dbo.IssueFieldValues v ON v.FieldId=f.Id AND v.IssueId=@issue
            WHERE f.ProjectId=@project
              AND (@issue IS NULL OR EXISTS(SELECT 1 FROM dbo.Issues i WHERE i.Id=@issue AND i.ProjectId=@project))
            ORDER BY f.Name
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@issue", issue)]);
        await using var reader = await command.ExecuteReaderAsync();
        var fields = new List<IssueField>();
        while (await reader.ReadAsync())
            fields.Add(new(reader.GetInt32(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2)));
        return fields;
    }

    public async Task Create(int user, int project, string name)
    {
        if (!await projects.CanManage(user, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        var affected = await AdministrationAuditStore.Execute(connection,
            new(user,"project.issue-field.create",name,$"#{project}"),"""
            INSERT dbo.ProjectIssueFields(ProjectId,Name)
            SELECT Id,@name FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@project AND State='Active'
            """, P("@project", project), P("@name", name));
        if (affected != 1) throw new UnauthorizedAccessException();
    }

    public async Task SetValue(int user, int id, int field, int revision, string? value)
    {
        var issue = await issues.Get(user, id);
        if (issue is null || !await issues.Can(user, issue.ProjectId, "issues.edit"))
            throw new UnauthorizedAccessException();
        value = value?.Trim();
        if (value is { Length: > 500 }) throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await IssueStore.RequireActive(connection, transaction, issue.ProjectId);
        await using var check = new SqlCommand("""
            SELECT 1 FROM dbo.ProjectIssueFields WHERE Id=@field AND ProjectId=@project
            """, connection, transaction);
        check.Parameters.AddRange([P("@field", field), P("@project", issue.ProjectId)]);
        if (await check.ExecuteScalarAsync() is null)
            throw new ArgumentException("Odaberi polje ovog projekta.");

        await using var update = new SqlCommand("""
            UPDATE dbo.Issues SET Revision=Revision+1 WHERE Id=@issue AND ProjectId=@project AND Revision=@revision
            """, connection, transaction);
        update.Parameters.AddRange([P("@issue", id), P("@project", issue.ProjectId), P("@revision", revision)]);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Zadatak je u međuvremenu izmijenjen. Osvježi stranicu.");

        await using var save = new SqlCommand(string.IsNullOrEmpty(value)
            ? "DELETE dbo.IssueFieldValues WHERE IssueId=@issue AND FieldId=@field"
            : """
              UPDATE dbo.IssueFieldValues SET Value=@value WHERE IssueId=@issue AND FieldId=@field;
              IF @@ROWCOUNT=0 INSERT dbo.IssueFieldValues(IssueId,FieldId,Value) VALUES(@issue,@field,@value);
              """, connection, transaction);
        save.Parameters.AddRange([P("@issue",id), P("@field",field), P("@value",value)]);
        await save.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}

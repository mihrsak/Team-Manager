using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record IssueField(int Id, string Name, string? Value, bool ManagersOnly);

public sealed class IssueFieldStore(IConfiguration configuration, IssueStore issues, ProjectStore projects, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public Task<List<IssueField>> Definitions(int project) => List(null, project, null, true);

    public async Task<List<IssueField>> ForIssue(int user, int project, int issue)
        => await List(user, project, issue, await projects.CanManage(user, project));

    private async Task<List<IssueField>> List(int? viewer, int project, int? issue, bool canSeeRestricted)
    {
        if (oracle.Enabled) return await oracle.IssueFields(viewer, project, issue, canSeeRestricted);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT f.Id,f.Name,v.Value,f.ManagersOnly FROM dbo.ProjectIssueFields f
            LEFT JOIN dbo.IssueFieldValues v ON v.FieldId=f.Id AND v.IssueId=@issue
            WHERE f.ProjectId=@project AND (f.ManagersOnly=0 OR @canSeeRestricted=1)
              AND (@issue IS NULL OR EXISTS(SELECT 1 FROM dbo.Issues i WHERE i.Id=@issue AND i.ProjectId=@project
                AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))))
            ORDER BY f.Name OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@project", project), P("@issue", issue), P("@canSeeRestricted", canSeeRestricted), P("@viewer", viewer)]);
        await using var reader = await command.ExecuteReaderAsync();
        var fields = new List<IssueField>();
        while (await reader.ReadAsync())
            fields.Add(new(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3)));
        return fields;
    }

    public async Task Create(int user, int project, string name, bool managersOnly)
    {
        if (oracle.Enabled)
        {
            await oracle.WriteProjectData(user, project, "issue-field-name:" + name.Trim().ToUpperInvariant(), async w =>
            {
                if (!(await OracleWorkspace.Manage(w, user, project)) || (await w.Find("Projects", project)) is not { } p || OracleWorkspace.Workspace.Text(p, "State") != "Active") throw new UnauthorizedAccessException();
                if ((await w.Rows("ProjectIssueFields")).Any(f => OracleWorkspace.Workspace.Int(f, "ProjectId") == project && OracleWorkspace.Workspace.Text(f, "Name") == name)) throw new ArgumentException("Taj naziv već postoji.");
                w.Add("ProjectIssueFields", ("Id", (await w.Next("ProjectIssueFields"))), ("ProjectId", project), ("Name", name), ("ManagersOnly", managersOnly));
                await OracleWorkspace.Audit(w, user, "project.issue-field.create", name); return true;
            }); return;
        }
        if (!await projects.CanManage(user, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        var affected = await AdministrationAuditStore.Execute(connection,
            new(user, "project.issue-field.create", name, $"#{project}"), """
            INSERT dbo.ProjectIssueFields(ProjectId,Name,ManagersOnly)
            SELECT Id,@name,@restricted FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@project AND State='Active'
            """, P("@project", project), P("@name", name), P("@restricted", managersOnly));
        if (affected != 1) throw new UnauthorizedAccessException();
    }

    public async Task<bool> SetVisibility(int user, int project, int field, bool managersOnly)
    {
        if (oracle.Enabled) return await oracle.WriteProjectMetadata(user, project, null, null, async w =>
        {
            if (!(await OracleWorkspace.Manage(w, user, project)) || (await w.Find("Projects", project)) is not { } p || OracleWorkspace.Workspace.Text(p, "State") != "Active") throw new UnauthorizedAccessException();
            var f = (await w.Find("ProjectIssueFields", field));
            if (f is null || OracleWorkspace.Workspace.Int(f, "ProjectId") != project) return false;
            OracleWorkspace.Workspace.Set(f, "ManagersOnly", managersOnly);
            await OracleWorkspace.Audit(w, user, managersOnly ? "project.issue-field.restrict" : "project.issue-field.share", $"#{field}"); return true;
        });
        if (!await projects.CanManage(user, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        var action = managersOnly ? "project.issue-field.restrict" : "project.issue-field.share";
        var affected = await AdministrationAuditStore.Execute(connection,
            new(user, action, $"#{field}", $"#{project}"), """
            UPDATE f SET ManagersOnly=@restricted
            FROM dbo.ProjectIssueFields f JOIN dbo.Projects p WITH(HOLDLOCK) ON p.Id=f.ProjectId
            WHERE f.Id=@field AND f.ProjectId=@project AND p.State='Active'
            """, P("@field", field), P("@project", project), P("@restricted", managersOnly));
        return affected == 1;
    }

    public async Task SetValue(int user, int id, int field, int revision, string? value)
    {
        if (oracle.Enabled) { await oracle.SetIssueField(user, id, field, revision, value); return; }
        var issue = await issues.Get(user, id);
        if (issue is null || !await issues.Can(user, issue.ProjectId, "issues.edit"))
            throw new UnauthorizedAccessException();
        value = value?.Trim();
        if (value is { Length: > 500 }) throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await IssueStore.RequireActive(connection, transaction, issue.ProjectId);
        await IssueStore.RequireIssueProject(connection, transaction, id, issue.ProjectId, user);
        await using var check = new SqlCommand("""
            SELECT ManagersOnly FROM dbo.ProjectIssueFields WITH(HOLDLOCK) WHERE Id=@field AND ProjectId=@project
            """, connection, transaction);
        check.Parameters.AddRange([P("@field", field), P("@project", issue.ProjectId)]);
        var visibility = await check.ExecuteScalarAsync();
        if (visibility is not false && !await projects.CanManage(user, issue.ProjectId))
            throw new UnauthorizedAccessException();
        if (visibility is null)
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
        save.Parameters.AddRange([P("@issue", id), P("@field", field), P("@value", value)]);
        await save.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}

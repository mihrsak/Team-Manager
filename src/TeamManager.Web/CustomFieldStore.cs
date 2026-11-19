using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record CustomFieldValue(int Id, string Name, string? Value, int Revision);
public sealed record FieldTarget(string Title, int? ProjectId, int? IssueId, bool CanEdit);

public sealed class CustomFieldStore(IConfiguration config, ProjectStore projects, IssueStore issues,OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public static bool ValidScope(string? scope) => scope is "user" or "project" or "time";

    private static string ValueTable(string scope) => scope switch
    {
        "user" => "UserFieldValues",
        "project" => "ProjectFieldValues",
        "time" => "TimeFieldValues",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public async Task<FieldTarget?> Target(int actor, bool managesUsers, string scope, int id)
    {
        if(oracle.Enabled)return await oracle.Read(w=>OracleWorkspace.FieldTarget(w,actor,scope,id));
        if (scope == "project")
        {
            var project = (await projects.List(actor)).Find(item => item.Id == id);
            return project is null ? null : new(project.Input.Name, id, null,
                project.Input.State == "Active" && await projects.CanManage(actor, id));
        }

        if (scope == "user" && actor != id && !managesUsers) return null;
        await using var connection = Connect();
        await connection.OpenAsync();
        if (scope == "user")
        {
            await using var user = new SqlCommand("SELECT DisplayName FROM dbo.AppUsers WHERE Id=@id", connection);
            user.Parameters.Add(P("@id", id));
            return await user.ExecuteScalarAsync() is string name ? new(name, null, null, true) : null;
        }

        await using var command = new SqlCommand("""
            SELECT t.UserId,i.Id,i.ProjectId,i.Title FROM dbo.TimeEntries t JOIN dbo.Issues i ON i.Id=t.IssueId
            WHERE t.Id=@id AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId))
            OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@id", id),P("@viewer",actor)]);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var owner = reader.GetInt32(0);
        var issue = reader.GetInt32(1);
        var projectId = reader.GetInt32(2);
        var title = reader.GetString(3);
        if (!await issues.Can(actor, projectId, "time.view")) return null;
        return new(title, projectId, issue, actor == owner && await issues.Can(actor, projectId, "time.log"));
    }

    public async Task<List<CustomFieldValue>> List(int actor, string scope, int? id, FieldTarget? target)
    {
        if(oracle.Enabled)return await oracle.Fields(actor,scope,id);
        await using var connection = Connect();
        await connection.OpenAsync();
        var timeGuard = scope == "time" && id is not null
            ? """
              AND EXISTS (SELECT 1 FROM dbo.TimeEntries t JOIN dbo.Issues i ON i.Id=t.IssueId
              JOIN dbo.Projects p ON p.Id=i.ProjectId
              WHERE t.Id=@entity AND i.Id=@issue AND p.Id=@project AND p.IssuesEnabled=1 AND p.TimeEnabled=1
                AND EXISTS(SELECT 1 FROM dbo.IssueVisibility(@viewer,i.ProjectId,i.IsPrivate,i.CreatedBy,i.AssigneeId)))
              """
            : "";
        await using var command = new SqlCommand($"""
            SELECT f.Id,f.Name,v.Value,COALESCE(v.Revision,0)
            FROM dbo.CustomFields f LEFT JOIN dbo.{ValueTable(scope)} v ON v.FieldId=f.Id AND v.EntityId=@entity
            WHERE f.Scope=@scope {timeGuard} ORDER BY f.Name OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.AddRange([P("@scope",scope), P("@entity",id), P("@issue",target?.IssueId), P("@project",target?.ProjectId),P("@viewer",actor)]);
        var fields = new List<CustomFieldValue>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            fields.Add(new(reader.GetInt32(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetInt32(3)));
        return fields;
    }

    public async Task Create(int actor, string scope, string name)
    {
        if(oracle.Enabled){await oracle.Write(w=>{
            if(!OracleWorkspace.Rights(w,actor).Contains("*"))throw new UnauthorizedAccessException();
            if(w.Rows("CustomFields").Any(r=>OracleWorkspace.Workspace.Text(r,"Scope")==scope&&OracleWorkspace.Workspace.Text(r,"Name")==name))
                throw new ArgumentException("Taj naziv već postoji.");
            w.Add("CustomFields",("Id",w.Next("CustomFields")),("Scope",scope),("Name",name));
            OracleWorkspace.Audit(w,actor,"custom-field.create",name,scope);return true;
        });return;}
        await using var connection = Connect();
        await AdministrationAuditStore.Execute(connection,new(actor,"custom-field.create",name,scope),
            "INSERT dbo.CustomFields(Scope,Name) VALUES(@scope,@name)",P("@scope",scope),P("@name",name));
    }

    public async Task Set(int actor, bool managesUsers, string scope, int entity, int field, int revision, string? value)
    {
        if(oracle.Enabled){await oracle.SetField(actor,scope,entity,field,revision,value);return;}
        var target = await Target(actor, managesUsers, scope, entity);
        if (target?.CanEdit != true) throw new UnauthorizedAccessException();
        value = value?.Trim();
        if (value is { Length: > 500 }) throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");
        var table = ValueTable(scope);

        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        if (scope == "time")
        {
            await IssueStore.RequireActive(connection,transaction,target.ProjectId!.Value,true);
            await IssueStore.RequireIssueProject(connection,transaction,target.IssueId!.Value,target.ProjectId.Value,actor);
        }
        else if (scope == "project")
        {
            await using var active = new SqlCommand(
                "SELECT 1 FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@entity AND State='Active'",connection,transaction);
            active.Parameters.Add(P("@entity",entity));
            if (await active.ExecuteScalarAsync() is null) throw new UnauthorizedAccessException();
        }

        await using var command = new SqlCommand($"""
            IF NOT EXISTS (SELECT 1 FROM dbo.CustomFields WHERE Id=@field AND Scope=@scope)
                SELECT -1;
            ELSE IF EXISTS (SELECT 1 FROM dbo.{table} WITH(UPDLOCK,HOLDLOCK) WHERE EntityId=@entity AND FieldId=@field)
            BEGIN
                UPDATE dbo.{table} SET Value=@value,Revision=Revision+1
                WHERE EntityId=@entity AND FieldId=@field AND Revision=@revision;
                SELECT @@ROWCOUNT;
            END
            ELSE IF @revision=0
            BEGIN
                INSERT dbo.{table}(EntityId,FieldId,Value,Revision) VALUES(@entity,@field,@value,1);
                SELECT 1;
            END
            ELSE SELECT 0;
            """,connection,transaction);
        command.Parameters.AddRange([P("@scope",scope),P("@entity",entity),P("@field",field),P("@revision",revision),
            new SqlParameter("@value",System.Data.SqlDbType.NVarChar,500){Value=string.IsNullOrEmpty(value)?DBNull.Value:value}]);
        var affected = (int)(await command.ExecuteScalarAsync())!;
        if (affected == -1) throw new ArgumentException("Odaberi polje te vrste.");
        if (affected == 0) throw new InvalidOperationException("Vrijednost je u međuvremenu izmijenjena. Osvježi stranicu.");
        await transaction.CommitAsync();
    }
}

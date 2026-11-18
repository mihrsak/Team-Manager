using System.Data;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public record ImportedProject(int Id, string Name);
public record ImportedIssue(int Id, string Title, string Type, string Status, string Priority, bool IsPrivate);

public sealed class OracleProjectImport(IConfiguration configuration)
{
    internal static bool Enabled(IConfiguration config) => config.GetValue<bool>("OracleProjects:Enabled");

    private OracleConnection Connect() => new(configuration.GetConnectionString("TeamManagerOracle")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:TeamManagerOracle first."));

    public async Task<(int Projects, int Issues)> Import()
    {
        var sourceString = configuration.GetConnectionString("TeamManager")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:TeamManager first.");
        await using var target = Connect();
        await target.OpenAsync();
        foreach (var migration in new[] { "002-imported-projects.sql", "003-imported-issues.sql" })
        {
            await using var command = new OracleCommand(await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "database", "oracle", migration)), target);
            await command.ExecuteNonQueryAsync();
        }

        await using var source = new SqlConnection(sourceString);
        await source.OpenAsync();
        await using var sourceTransaction = (SqlTransaction)await source.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var targetTransaction = target.BeginTransaction();
        foreach (var sql in new[] {
            "LOCK TABLE TM_IMPORTED_PROJECTS IN EXCLUSIVE MODE",
            "DELETE FROM TM_IMPORTED_ISSUES", "DELETE FROM TM_IMPORTED_PROJECTS" })
        {
            await using var command = new OracleCommand(sql, target) { Transaction = targetTransaction };
            await command.ExecuteNonQueryAsync();
        }

        var projects = await Copy(source, sourceTransaction, target, targetTransaction,
            "SELECT Id,Name,Description,ParentId,IsPublic,State,Revision,IssuesEnabled,TimeEnabled,InheritParentRoles FROM dbo.Projects",
            """
            INSERT INTO TM_IMPORTED_PROJECTS
            (ID,NAME,DESCRIPTION,PARENT_ID,IS_PUBLIC,STATE,REVISION,ISSUES_ENABLED,TIME_ENABLED,INHERIT_PARENT_ROLES)
            VALUES (:p0,:p1,:p2,:p3,:p4,:p5,:p6,:p7,:p8,:p9)
            """,
            [OracleDbType.Int32, OracleDbType.NVarchar2, OracleDbType.NClob, OracleDbType.Int32,
             OracleDbType.Int32, OracleDbType.Varchar2, OracleDbType.Int32, OracleDbType.Int32, OracleDbType.Int32, OracleDbType.Int32]);
        var issues = await Copy(source, sourceTransaction, target, targetTransaction,
            """
            SELECT Id,ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,CreatedBy,CreatedAt,
                   Revision,StartDate,RoadmapItemId,IsPrivate FROM dbo.Issues
            """,
            """
            INSERT INTO TM_IMPORTED_ISSUES
            (ID,PROJECT_ID,TITLE,DESCRIPTION,ISSUE_TYPE,PRIORITY,STATUS,ASSIGNEE_ID,DUE_DATE,CREATED_BY,CREATED_AT,
             REVISION,START_DATE,ROADMAP_ITEM_ID,IS_PRIVATE)
            VALUES (:p0,:p1,:p2,:p3,:p4,:p5,:p6,:p7,:p8,:p9,:p10,:p11,:p12,:p13,:p14)
            """,
            [OracleDbType.Int32, OracleDbType.Int32, OracleDbType.NVarchar2, OracleDbType.NClob, OracleDbType.NVarchar2,
             OracleDbType.NVarchar2, OracleDbType.NVarchar2, OracleDbType.Int32, OracleDbType.Date, OracleDbType.Int32,
             OracleDbType.TimeStamp, OracleDbType.Int32, OracleDbType.Date, OracleDbType.Int32, OracleDbType.Int32]);
        await sourceTransaction.CommitAsync();
        await targetTransaction.CommitAsync();
        return (projects, issues);
    }

    private static async Task<int> Copy(SqlConnection source, SqlTransaction sourceTransaction, OracleConnection target,
        OracleTransaction targetTransaction, string selectSql, string insertSql, OracleDbType[] types)
    {
        await using var select = new SqlCommand(selectSql, source, sourceTransaction);
        await using var rows = await select.ExecuteReaderAsync();
        await using var insert = new OracleCommand(insertSql, target) { BindByName = true, Transaction = targetTransaction };
        for (var i = 0; i < types.Length; i++) insert.Parameters.Add($"p{i}", types[i]);
        var count = 0;
        while (await rows.ReadAsync())
        {
            for (var i = 0; i < types.Length; i++)
            {
                var value = rows.GetValue(i);
                insert.Parameters[i].Value = value is bool flag ? (flag ? 1 : 0) : value;
            }
            count += await insert.ExecuteNonQueryAsync();
        }
        return count;
    }

    public async Task<(List<ImportedProject> Projects, List<ImportedIssue> Issues)> List(int? projectId, CancellationToken token)
    {
        await using var connection = Connect();
        await connection.OpenAsync(token);
        var projects = new List<ImportedProject>();
        await using (var command = new OracleCommand("SELECT ID,NAME FROM TM_IMPORTED_PROJECTS ORDER BY NAME,ID", connection))
        await using (var rows = await command.ExecuteReaderAsync(token))
            while (await rows.ReadAsync(token)) projects.Add(new(rows.GetInt32(0), rows.GetString(1)));

        var issues = new List<ImportedIssue>();
        if (projectId is not null)
        {
            await using var command = new OracleCommand(
                "SELECT ID,TITLE,ISSUE_TYPE,STATUS,PRIORITY,IS_PRIVATE FROM TM_IMPORTED_ISSUES WHERE PROJECT_ID=:project ORDER BY ID DESC",
                connection) { BindByName = true, CommandTimeout = 10 };
            command.Parameters.Add("project", OracleDbType.Int32).Value = projectId.Value;
            await using var rows = await command.ExecuteReaderAsync(token);
            while (await rows.ReadAsync(token))
                issues.Add(new(rows.GetInt32(0), rows.GetString(1), rows.GetString(2), rows.GetString(3),
                    rows.GetString(4), rows.GetInt32(5) != 0));
        }
        return (projects, issues);
    }
}

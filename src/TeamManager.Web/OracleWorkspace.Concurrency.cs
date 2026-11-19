using System.Data;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    public async Task InstallConcurrency()
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using (var state = new OracleCommand("SELECT SCHEMA_VERSION FROM TMR_WORKSPACE WHERE ID=1", connection))
        {
            var version = Convert.ToInt32(await state.ExecuteScalarAsync());
            if (version is not (6 or 7 or 8)) throw new InvalidOperationException("Migrate the relational Oracle workspace first.");
        }
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database/oracle/007-concurrency-and-reports.sql"));
        foreach (var statement in Regex.Split(sql, @"(?m)^/\s*$").Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var command = new OracleCommand(statement.Trim(), connection);
            await command.ExecuteNonQueryAsync();
        }
        foreach (var table in Schema.Value.Tables.Where(t => t.Columns.Any(c => c.Name == "Id" && c.Type is "int" or "bigint")))
        {
            var sequence = "TMR_SEQ_" + table.Name;
            await using var exists = new OracleCommand("SELECT COUNT(*) FROM USER_SEQUENCES WHERE SEQUENCE_NAME=:name", connection);
            exists.Parameters.Add("name", OracleDbType.Varchar2).Value = sequence;
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) != 0) continue;
            await using var highWater = new OracleCommand($"SELECT GREATEST(COALESCE((SELECT MAX(\"Id\") FROM {table.SqlName}),0),COALESCE((SELECT LAST_VALUE FROM TMR_COUNTERS WHERE TABLE_NAME=:tableName),0)) FROM DUAL", connection);
            highWater.Parameters.Add("tableName", OracleDbType.Varchar2).Value = table.Name;
            var start = checked(Convert.ToInt64(await highWater.ExecuteScalarAsync()) + 1);
            var maximum = table.Columns.Single(c => c.Name == "Id").Type == "bigint" ? long.MaxValue : int.MaxValue;
            await using var create = new OracleCommand($"CREATE SEQUENCE {Quote(sequence)} START WITH {start} MAXVALUE {maximum} NOCYCLE CACHE 100", connection);
            await create.ExecuteNonQueryAsync();
        }
        await using var transaction = connection.BeginTransaction();
        // Validate the package grant before publishing the new runtime version.
        await AcquireWriteLock(connection, transaction, "policy", 6);
        await using (var feature = RelationalCommand(connection, transaction, "MERGE INTO TMR_RUNTIME_FEATURES t USING (SELECT 'fine-writes' NAME FROM DUAL) s ON(t.NAME=s.NAME) WHEN MATCHED THEN UPDATE SET VERSION=2 WHEN NOT MATCHED THEN INSERT(NAME,VERSION) VALUES(s.NAME,2)"))
            await feature.ExecuteNonQueryAsync();
        await using (var state = RelationalCommand(connection, transaction, "UPDATE TMR_WORKSPACE SET SCHEMA_VERSION=8 WHERE ID=1"))
            await state.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        Console.WriteLine("PASS: Oracle sequences, scoped write locks and report indexes installed; existing sequences were not reset.");
    }

    private static async Task AcquireWriteLock(OracleConnection connection, OracleTransaction transaction, string resource, int mode)
    {
        await using var command = RelationalCommand(connection, transaction, """
            DECLARE lock_id PLS_INTEGER;
            BEGIN
                lock_id:=DBMS_UTILITY.GET_HASH_VALUE(SYS_CONTEXT('USERENV','CURRENT_SCHEMA')||':TeamManager:'||:resource,0,1073741823);
                :result:=DBMS_LOCK.REQUEST(lock_id,:mode,10,TRUE);
                IF :result=4 THEN :result:=DBMS_LOCK.CONVERT(lock_id,:mode,10); END IF;
            END;
            """);
        command.Parameters.Add("resource", OracleDbType.Varchar2).Value = resource;
        command.Parameters.Add("mode", OracleDbType.Int32).Value = mode;
        var result = command.Parameters.Add("result", OracleDbType.Int32);
        result.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();
        if (int.Parse(result.Value.ToString()!, System.Globalization.CultureInfo.InvariantCulture) != 0)
            throw new OracleWriteBusyException();
    }

    private sealed record WriteScope(int? Actor, int[] Issues, int[] Readers, int[] Writers, string[] Entities, string? Employee = null);
    internal Task<T> WriteIssues<T>(int actor, IEnumerable<int> ids, Func<Workspace, T> action, params int[] relatedUsers)
        => Run(action, true, new(actor, ids.Distinct().Order().ToArray(), [actor, .. relatedUsers], [], []));
    internal Task<T> WriteUser<T>(int user, Func<Workspace, T> action)
        => Run(action, true, new(user, [], [], [user], []));
    internal Task<T> WriteAccount<T>(int actor, int target, Func<Workspace, T> action)
        => Run(action, true, new(actor, [], [actor], [target], []));
    private Task<T> WriteLogin<T>(string employee, Func<Workspace, T> action)
        => Run(action, true, new(null, [], [], [], [], employee));
    internal Task<T> WriteProjectData<T>(int actor, int project, string area, Func<Workspace, T> action)
        => Run(action, true, new(actor, [], [actor], [], [$"project-data:{project}:{area}"]));

    internal Task<T> WriteNamedAdministration<T>(int actor, string entity, Func<Workspace, T> action)
        => Run(action, true, new(actor, [], [actor], [], [entity]));

    private static async Task AcquireScope(OracleConnection connection, OracleTransaction transaction, WriteScope? scope)
    {
        await AcquireWriteLock(connection, transaction, "policy", scope is null ? 6 : 4);
        if (scope is null) return;
        // All writers follow the same phases: policy, issue IDs, identity IDs,
        // then administrative entities. All actor, assignee and watcher dependencies are known before identity locking.
        foreach (var id in scope.Issues.Order()) await AcquireWriteLock(connection, transaction, "issue:" + id, 6);
        var readers = scope.Readers.ToHashSet();
        var writers = scope.Writers.ToHashSet();
        foreach (var id in scope.Issues)
        {
            await using var assigned = RelationalCommand(connection, transaction, "SELECT \"AssigneeId\" FROM \"TMR_Issues\" WHERE \"Id\"=:id");
            assigned.Parameters.Add("id", OracleDbType.Int32).Value = id;
            var value = await assigned.ExecuteScalarAsync();
            if (value is not null and not DBNull) readers.Add(Convert.ToInt32(value));
            await using var watched = RelationalCommand(connection, transaction, "SELECT \"UserId\" FROM \"TMR_IssueWatchers\" WHERE \"IssueId\"=:id");
            watched.Parameters.Add("id", OracleDbType.Int32).Value = id;
            await using var watchers = await watched.ExecuteReaderAsync();
            while (await watchers.ReadAsync()) readers.Add(watchers.GetInt32(0));
        }
        if (scope.Employee is { } employee)
        {
            await using var account = RelationalCommand(connection, transaction, "SELECT \"Id\" FROM \"TMR_AppUsers\" WHERE UPPER(\"EmployeeNumber\")=UPPER(:employee)");
            account.Parameters.Add("employee", OracleDbType.NVarchar2).Value = employee;
            var value = await account.ExecuteScalarAsync();
            if (value is not null and not DBNull) writers.Add(Convert.ToInt32(value));
        }
        foreach (var user in readers.Union(writers).Order())
            await AcquireWriteLock(connection, transaction, "identity:" + user, writers.Contains(user) ? 6 : 4);
        foreach (var entity in scope.Entities.Order(StringComparer.Ordinal)) await AcquireWriteLock(connection, transaction, entity, 6);
    }
}

internal sealed class OracleWriteBusyException() : InvalidOperationException("Spremanje je trenutačno zauzeto. Pokušaj ponovno.");

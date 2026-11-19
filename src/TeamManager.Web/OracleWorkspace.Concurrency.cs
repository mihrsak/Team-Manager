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
            if (version is not (6 or 7)) throw new InvalidOperationException("Migrate the relational Oracle workspace first.");
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
        await using (var feature = RelationalCommand(connection, transaction, "MERGE INTO TMR_RUNTIME_FEATURES t USING (SELECT 'fine-writes' NAME FROM DUAL) s ON(t.NAME=s.NAME) WHEN MATCHED THEN UPDATE SET VERSION=1 WHEN NOT MATCHED THEN INSERT(NAME,VERSION) VALUES(s.NAME,1)"))
            await feature.ExecuteNonQueryAsync();
        await using (var state = RelationalCommand(connection, transaction, "UPDATE TMR_WORKSPACE SET SCHEMA_VERSION=7 WHERE ID=1"))
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

    internal Task<T> WriteIssues<T>(IEnumerable<int> ids, Func<Workspace, T> action)
        => Run(action, true, ids.Distinct().Order().Select(id => "issue:" + id).ToArray());
    internal Task<T> WriteUser<T>(int user, Func<Workspace, T> action)
        => Run(action, true, ["user:" + user]);
}

internal sealed class OracleWriteBusyException() : InvalidOperationException("Spremanje je trenutačno zauzeto. Pokušaj ponovno.");

using System.Data;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    public async Task MigrateRelational()
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        try
        {
            await using var marker = new OracleCommand("SELECT SCHEMA_VERSION FROM TMR_WORKSPACE WHERE ID=1", connection);
            var installed = await marker.ExecuteScalarAsync();
            if (installed is not null and not DBNull)
            {
                if (Convert.ToInt32(installed) is not (6 or 7 or 8 or 9 or 10))
                    throw new InvalidOperationException("Unexpected relational Oracle schema version.");
                await InstallConcurrency();
                Console.WriteLine("Relational Oracle migration already applied; active data was not overwritten.");
                return;
            }
        }
        catch (OracleException error) when (error.Number == 942) { /* First installation. */ }
        // Oracle DDL commits implicitly. Install an empty schema first; copy data and
        // publish the version marker only in the subsequent, single DML transaction.
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "oracle", "006-relational-workspace.sql"));
        foreach (var statement in Regex.Split(sql, @"(?m)^/\s*$").Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var constraint = Regex.Match(statement, "ADD CONSTRAINT \"([^\"]+)\"");
            if (constraint.Success)
            {
                await using var exists = new OracleCommand("SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME=:name", connection);
                exists.Parameters.Add("name", OracleDbType.Varchar2).Value = constraint.Groups[1].Value;
                if (Convert.ToInt32(await exists.ExecuteScalarAsync()) != 0) continue;
            }
            await using var command = new OracleCommand(statement.Trim(), connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
        await using var transaction = (OracleTransaction)await connection.BeginTransactionAsync();
        await using var source = RelationalCommand(connection, transaction,
            "SELECT CONTENT,REVISION FROM TM_ACTIVE_WORKSPACE WHERE ID=1 FOR UPDATE WAIT 30");
        JsonObject legacy;
        long revision;
        await using (var reader = await source.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Import the Oracle workspace first.");
            legacy = JsonNode.Parse(reader.GetString(0))!.AsObject();
            revision = reader.GetInt64(1);
        }
        await using (var marker = RelationalCommand(connection, transaction, "SELECT COUNT(*) FROM TMR_WORKSPACE WHERE ID=1"))
        {
            if (Convert.ToInt32(await marker.ExecuteScalarAsync()) != 0)
            {
                await transaction.RollbackAsync();
                await InstallConcurrency();
                Console.WriteLine("Relational Oracle migration already applied; active data was not overwritten.");
                return;
            }
        }
        foreach (var table in Schema.Value.Tables)
        {
            await using var count = RelationalCommand(connection, transaction, $"SELECT COUNT(*) FROM {table.SqlName}");
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException($"Refusing to overwrite nonempty relational table {table.Name}.");
        }
        var expected = NormalizeDocument(legacy);
        await PersistChanges(connection, transaction, null, expected);
        var actual = await LoadRelational(connection, transaction);
        var total = AssertEquivalent(expected, actual);
        await using (var constraints = RelationalCommand(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE"))
            await constraints.ExecuteNonQueryAsync();
        await using var publish = RelationalCommand(connection, transaction, """
            INSERT INTO TMR_WORKSPACE (ID,SCHEMA_VERSION,REVISION,SOURCE_REVISION,MIGRATED_AT)
            VALUES (1,:version,:revision,:revision,SYS_EXTRACT_UTC(SYSTIMESTAMP))
            """);
        publish.Parameters.Add("version", OracleDbType.Int32).Value = Schema.Value.Version;
        publish.Parameters.Add("revision", OracleDbType.Int64).Value = revision;
        await publish.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        await InstallConcurrency();
        Console.WriteLine($"PASS: migrated and compared {total} records across {Schema.Value.Tables.Length} relational Oracle tables, including binary data and counters. Legacy JSON is archived and frozen.");
    }

    private static int AssertEquivalent(JsonObject expected, JsonObject actual)
    {
        var total = 0;
        foreach (var table in Schema.Value.Tables)
        {
            var wanted = expected[table.Name]!.AsArray().OfType<JsonObject>().ToDictionary(r => RowKey(table, r));
            var found = actual[table.Name]!.AsArray().OfType<JsonObject>().ToDictionary(r => RowKey(table, r));
            if (wanted.Count != found.Count || wanted.Any(p => !found.TryGetValue(p.Key, out var value) || !JsonNode.DeepEquals(p.Value, value)))
                throw new InvalidOperationException($"Relational migration verification failed for {table.Name}.");
            total += wanted.Count;
        }
        if (!JsonNode.DeepEquals(expected["_Sequences"], actual["_Sequences"]))
            throw new InvalidOperationException("Relational migration verification failed for counters.");
        return total;
    }
}

using System.Data;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IBM.Data.Db2;
using Oracle.ManagedDataAccess.Client;
using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private bool IsDb2 => configuration["Database:Provider"]?.Equals("Db2", StringComparison.OrdinalIgnoreCase) == true;

    public async Task ExportRelational(string file)
    {
        if (IsDb2) throw new InvalidOperationException("This migration export requires an Oracle source workspace.");
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var snapshot = await LoadRelational(connection, transaction);
        await using var sequences = RelationalCommand(connection, transaction, "SELECT SEQUENCE_NAME,LAST_NUMBER FROM USER_SEQUENCES");
        await using var reader = await sequences.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (name.StartsWith("TMR_SEQ_", StringComparison.Ordinal)) snapshot["_Sequences"]![name[8..]] = Math.Max(snapshot["_Sequences"]?[name[8..]]?.GetValue<long>() ?? 0, reader.GetInt64(1));
        }
        await reader.DisposeAsync();
        await transaction.CommitAsync();
        await File.WriteAllTextAsync(file, snapshot.ToJsonString());
        Console.WriteLine("Relational snapshot exported; keep it outside source control because it contains account hashes and private data.");
    }

    public async Task ImportDb2(string file)
    {
        if (!IsDb2) throw new InvalidOperationException("Select Database:Provider=Db2 before importing.");
        var expected = NormalizeDocument(JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject());
        await using var connection = Connect();
        await connection.OpenAsync();
        var exists = false;
        await using (var check = new OracleCommand("SELECT COUNT(*) FROM SYSCAT.TABLES WHERE TABSCHEMA=CURRENT SCHEMA AND TABNAME='TMR_WORKSPACE'", connection)) exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;
        if (exists)
        {
            await using var marker = new OracleCommand("SELECT COUNT(*) FROM TMR_WORKSPACE WHERE ID=1", connection);
            if (Convert.ToInt32(await marker.ExecuteScalarAsync()) > 0) throw new InvalidOperationException("Refusing to overwrite an installed Db2 workspace.");
        }
        await using (var archive = new OracleCommand("CREATE TABLE IF NOT EXISTS TM_ACTIVE_WORKSPACE (ID NUMBER(1) PRIMARY KEY CHECK(ID=1),REVISION NUMBER(18) NOT NULL,CONTENT CLOB(1G) NOT NULL)", connection)) await archive.ExecuteNonQueryAsync();
        var ddl = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database/oracle/006-relational-workspace.sql"));
        var statements = Regex.Split(ddl, @"(?m)^/\s*$").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var foreignKeys = statements.Where(s => Regex.IsMatch(s, @"ALTER TABLE.*FOREIGN KEY", RegexOptions.Singleline)).ToArray();
        var frozen = statements.Single(s => s.Contains("TRIGGER TM_ACTIVE_WORKSPACE_FROZEN"));
        // Bootstrap retries may replace indexes only while their target tables are empty.
        foreach (var statement in statements.Where(s => s.Contains("CREATE UNIQUE INDEX IF NOT EXISTS") && s.Contains("CASE WHEN")))
        {
            var match = Regex.Match(statement, "INDEX IF NOT EXISTS (\"[^\"]+\") ON (\"[^\"]+\")");
            var name = match.Groups[1].Value;
            await using var index = new OracleCommand("SELECT COUNT(*) FROM SYSCAT.INDEXES WHERE INDSCHEMA=CURRENT SCHEMA AND INDNAME='" + name.Trim('"') + "'", connection);
            if (Convert.ToInt32(await index.ExecuteScalarAsync()) == 0) continue;
            await using var count = new OracleCommand("SELECT COUNT(*) FROM " + match.Groups[2].Value, connection);
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("Refusing to replace indexes on nonempty Db2 tables.");
            await using var drop = new OracleCommand("DROP INDEX " + name, connection);
            await drop.ExecuteNonQueryAsync();
        }
        foreach (var statement in statements.Except(foreignKeys).Where(s => s != frozen))
        {
            var indexes = new List<string>();
            var tableName = Regex.Match(statement, "CREATE TABLE IF NOT EXISTS (\"[^\"]+\")").Groups[1].Value;
            var source = tableName == "\"TMR_CustomFields\"" ? statement.Replace("\"Scope\" VARCHAR2(8 CHAR)", "\"Scope\" VARCHAR2(8 CHAR) NOT NULL") : statement;
            var prepared = Regex.Replace(source, @",?\s*CONSTRAINT ""([^""]+)"" UNIQUE \(([^)]+)\) DEFERRABLE INITIALLY DEFERRED", match =>
            {
                var columns = match.Groups[2].Value.Split(',').Select(c => c.Trim()).ToArray();
                if (columns.All(column => Regex.IsMatch(source, @"(?m)^\s*" + Regex.Escape(column) + @"\s+(?:NUMBER|NVARCHAR2|VARCHAR2|RAW)\([^)]*\)\s+NOT NULL"))) return match.Value;
                indexes.Add($"CREATE UNIQUE INDEX IF NOT EXISTS \"{match.Groups[1].Value}\" ON {tableName} ({match.Groups[2].Value}) EXCLUDE NULL KEYS");
                return "";
            });
            await using var command = new OracleCommand(prepared.Trim(), connection) { CommandTimeout = 60 };
            try { await command.ExecuteNonQueryAsync(); }
            catch (DB2Exception error) { throw new InvalidOperationException("Db2 schema statement failed: " + statement.Trim()[..Math.Min(statement.Trim().Length, 100)] + " [" + error.Errors[0].SQLState + "]", error); }
            foreach (var sql in indexes)
            {
                await using var index = new OracleCommand(sql, connection);
                await index.ExecuteNonQueryAsync();
            }
        }
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        foreach (var table in Schema.Value.Tables)
        {
            await using var count = RelationalCommand(connection, transaction, $"SELECT COUNT(*) FROM {table.SqlName}");
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("Refusing to overwrite nonempty Db2 table " + table.Name);
        }
        await PersistChanges(connection, transaction, null, expected);
        foreach (var statement in foreignKeys)
        {
            var name = Regex.Match(statement, "ADD CONSTRAINT \"([^\"]+)\"").Groups[1].Value;
            await using var check = RelationalCommand(connection, transaction, "SELECT COUNT(*) FROM SYSCAT.REFERENCES WHERE TABSCHEMA=CURRENT SCHEMA AND CONSTNAME=:name");
            AddQueryParameter(check, "name", name);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) != 0) continue;
            await using var command = RelationalCommand(connection, transaction, statement.Trim());
            await command.ExecuteNonQueryAsync();
        }
        var actual = await LoadRelational(connection, transaction);
        var total = AssertEquivalent(expected, actual);
        await using (var archive = RelationalCommand(connection, transaction, "INSERT INTO TM_ACTIVE_WORKSPACE (ID,REVISION,CONTENT) VALUES(1,1,:content)"))
        {
            archive.Parameters.Add("content", OracleDbType.Clob).Value = expected.ToJsonString();
            await archive.ExecuteNonQueryAsync();
        }
        await using (var freeze = RelationalCommand(connection, transaction, frozen.Trim().Replace("ON TM_ACTIVE_WORKSPACE", "ON TM_ACTIVE_WORKSPACE FOR EACH ROW"))) await freeze.ExecuteNonQueryAsync();
        await using (var marker = RelationalCommand(connection, transaction, "INSERT INTO TMR_WORKSPACE(ID,SCHEMA_VERSION,REVISION,SOURCE_REVISION,MIGRATED_AT) VALUES(1,11,1,1,SYS_EXTRACT_UTC(SYSTIMESTAMP))")) await marker.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        await InstallConcurrency();
        Console.WriteLine($"PASS: imported and independently compared {total} records into relational Db2; foreign keys validated and snapshot frozen.");
    }

    private static RelationalTable[] DependencyOrder()
    {
        var result = new List<RelationalTable>();
        var visiting = new HashSet<string>();
        void Visit(RelationalTable table)
        {
            if (result.Contains(table)) return;
            if (!visiting.Add(table.Name)) throw new InvalidOperationException("Cyclic table dependency in relational manifest.");
            foreach (var parent in table.ForeignKeys.Select(k => k.ReferencedTable).Where(n => n != table.Name).Distinct()) Visit(Schema.Value.Tables.Single(t => t.Name == parent));
            visiting.Remove(table.Name); result.Add(table);
        }
        foreach (var table in Schema.Value.Tables) Visit(table);
        return result.ToArray();
    }
}

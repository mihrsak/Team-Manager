using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;
// Relational Oracle tables persist typed rows, including binary attachments.
// Oracle owns the write lock and commit boundary, including authorization and history.
public sealed partial class OracleWorkspace(IConfiguration configuration)
{
    public bool Enabled => configuration["Database:Provider"]?.Equals("Oracle", StringComparison.OrdinalIgnoreCase) == true;

    private OracleConnection Connect() => new(configuration.GetConnectionString("TeamManagerOracle") ?? throw new InvalidOperationException("Configure ConnectionStrings:TeamManagerOracle."));
    public async Task Import()
    {
        await using var target = Connect();
        await target.OpenAsync();
        var schemaSql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "oracle", "005-active-workspace.sql"));
        await using (var schema = new OracleCommand(schemaSql, target))
            await schema.ExecuteNonQueryAsync();
        await using var source = new SqlConnection(configuration.GetConnectionString("TeamManager"));
        await source.OpenAsync();
        await using var snapshot = (SqlTransaction)await source.BeginTransactionAsync(IsolationLevel.Serializable);
        var tables = new List<string>();
        await using (var select = new SqlCommand("SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') ORDER BY name", source, snapshot))
        await using (var rows = await select.ExecuteReaderAsync())
            while (await rows.ReadAsync())
                tables.Add(rows.GetString(0));
        var document = new JsonObject();
        foreach (var table in tables)
        {
            await using var select = new SqlCommand($"SELECT * FROM dbo.[{table.Replace("]", "]]")}]", source, snapshot);
            await using var rows = await select.ExecuteReaderAsync();
            var items = new JsonArray();
            while (await rows.ReadAsync())
            {
                var row = new JsonObject();
                for (var i = 0; i < rows.FieldCount; i++)
                    row[rows.GetName(i)] = rows.IsDBNull(i) ? null : JsonSerializer.SerializeToNode(rows.GetValue(i));
                items.Add(row);
            }

            document[table] = items;
        }

        var sequences = new JsonObject();
        await using (var select = new SqlCommand("""
            SELECT OBJECT_NAME(object_id),CONVERT(bigint,COALESCE(last_value,0))
            FROM sys.identity_columns WHERE OBJECT_SCHEMA_NAME(object_id)='dbo'
            """, source, snapshot))
        await using (var rows = await select.ExecuteReaderAsync())
            while (await rows.ReadAsync()) sequences[rows.GetString(0)] = rows.GetInt64(1);
        document["_Sequences"] = sequences;
        await snapshot.CommitAsync();
        await using var transaction = (OracleTransaction)await target.BeginTransactionAsync();
        await using var insert = new OracleCommand("INSERT INTO TM_ACTIVE_WORKSPACE (ID,REVISION,CONTENT) VALUES (1,1,:content)", target)
        {
            Transaction = transaction,
            BindByName = true
        };
        insert.Parameters.Add("content", OracleDbType.Clob).Value = document.ToJsonString();
        // Import is deliberately insert-only. A second import cannot overwrite active Oracle changes.
        await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        await MigrateRelational();
    }

    internal async Task<T> Read<T>(Func<Workspace, Task<T>> action) => await Run(action, false);
    private async Task<T> Run<T>(Func<Workspace, Task<T>> action, bool write, WriteScope? scope = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await RunOnce(action, write, scope); }
            catch (OracleScopeChangedException) when (attempt < 2) { }
            catch (OracleScopeChangedException) { throw new OracleWriteBusyException(); }
        }
        throw new OracleWriteBusyException();
    }
    private async Task<T> RunOnce<T>(Func<Workspace, Task<T>> action, bool write, WriteScope? scope)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (OracleTransaction)await connection.BeginTransactionAsync(write ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);
        await using var state = RelationalCommand(connection, transaction,
            "SELECT SCHEMA_VERSION FROM TMR_WORKSPACE WHERE ID=1 AND EXISTS (SELECT 1 FROM TMR_RUNTIME_FEATURES WHERE NAME='fine-writes' AND VERSION=4)");
        var version = await state.ExecuteScalarAsync();
        if (version is null or DBNull || Convert.ToInt32(version) != Schema.Value.Version)
            throw new InvalidOperationException("Apply --migrate-oracle-workspace before starting the Oracle application.");
        if (write) await AcquireScope(connection, transaction, scope);
        var before = new JsonObject { ["_Sequences"] = new JsonObject() };
        var document = new JsonObject { ["_Sequences"] = new JsonObject() };
        async Task<JsonArray> Fetch(string table, string predicate, (string Name, object? Value)[] parameters)
        {
            var rows = await SelectRows(connection, transaction, table, predicate, parameters);
            var baseline = before[table] as JsonArray;
            if (baseline is null) before[table] = baseline = new JsonArray();
            var definition = Schema.Value.Tables.Single(t => t.Name == table);
            var keys = baseline.OfType<JsonObject>().Select(r => RowKey(definition, r)).ToHashSet();
            foreach (var row in rows.OfType<JsonObject>())
                if (keys.Add(RowKey(definition, row))) baseline.Add(row.DeepClone());
            return rows;
        }
        var usedReservedProject = false;
        async Task<long> Counter(string table)
        {
            if (table == "Projects" && !usedReservedProject && scope?.ReservedProject is int reserved)
            {
                usedReservedProject = true;
                return reserved;
            }
            if (!Schema.Value.Tables.Any(t => t.Name == table && t.Columns.Any(c => c.Name == "Id"))) throw new InvalidOperationException("Unknown Oracle identity table.");
            await using var command = RelationalCommand(connection, transaction, $"SELECT {Quote("TMR_SEQ_" + table)}.NEXTVAL FROM DUAL");
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        var workspace = new Workspace(document, Fetch, Counter);
        if (scope?.Actor is int actor && ((await workspace.Find("AppUsers", actor)) is not { } active || !Workspace.Flag(active, "IsActive")))
            throw new UnauthorizedAccessException();
        var result = await action(workspace);
        if (write)
        {
            var after = NormalizeDocument(document, partial: true);
            if (workspace.PreserveAdministrator) await AcquireWriteLock(connection, transaction, "administrators", 6);
            await PersistChanges(connection, transaction, before, after, persistCounters: false);
            if (workspace.PreserveAdministrator) await RequireAdministrator(connection, transaction);
            await using var constraints = RelationalCommand(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
            await constraints.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return result;
    }

    internal sealed class Workspace(JsonObject document,
        Func<string, string, (string Name, object? Value)[], Task<JsonArray>>? fetch = null, Func<string, Task<long>>? counter = null)
    {
        internal JsonObject Document { get; } = document;
        internal bool PreserveAdministrator { get; set; }

        private readonly HashSet<string> loadedAll = [];
        private readonly Dictionary<string, HashSet<string>> queries = [];
        private readonly Dictionary<string, HashSet<string>> seen = [];
        internal JsonArray Table(string table)
        {
            if (Document[table] is JsonArray rows) return rows;
            if (fetch is null) throw new InvalidOperationException($"Missing Oracle workspace table {table}.");
            var created = new JsonArray();
            Document[table] = created;
            return created;
        }
        internal async Task<IEnumerable<JsonObject>> Where(string table, string predicate, params (string Name, object? Value)[] parameters)
        {
            if (fetch is null) throw new InvalidOperationException("Targeted queries require an Oracle transaction.");
            var definition = Schema.Value.Tables.Single(t => t.Name == table);
            var current = Table(table);
            if (!seen.TryGetValue(table, out var keys)) seen[table] = keys = [];
            // Include newly added rows in the identity map; never overwrite pending edits,
            // resurrect removed rows, or treat an unloaded row as a deletion.
            foreach (var row in current.OfType<JsonObject>()) keys.Add(RowKey(definition, row));
            var queryKey = table + predicate + JsonSerializer.Serialize(parameters.Select(p => new { p.Name, p.Value }));
            if (queries.TryGetValue(queryKey, out var matched))
                return current.OfType<JsonObject>().Where(r => matched.Contains(RowKey(definition, r)));
            queries[queryKey] = matched = [];
            var selected = new List<JsonObject>();
            foreach (var row in (await fetch(table, predicate, parameters)).OfType<JsonObject>())
            {
                var key = RowKey(definition, row);
                matched.Add(key);
                var existing = current.OfType<JsonObject>().FirstOrDefault(r => RowKey(definition, r) == key);
                if (keys.Add(key)) { current.Add(row.DeepClone()); existing = (JsonObject)current.Last()!; }
                if (existing is not null) selected.Add(existing);
            }
            return selected;
        }
        internal async Task<IEnumerable<JsonObject>> By(string table, string column, object? value)
        {
            if (!Schema.Value.Tables.Single(t => t.Name == table).Columns.Any(c => c.Name == column)) throw new ArgumentException("Unknown column.");
            _ = (await Where(table, $"t.{Quote(column)}=:value", ("value", value))).ToArray();
            var expected = JsonSerializer.SerializeToNode(value);
            return Table(table).OfType<JsonObject>().Where(r => JsonNode.DeepEquals(r[column], expected));
        }
        internal async Task<IEnumerable<JsonObject>> Rows(string table)
        {
            if (fetch is not null && loadedAll.Add(table)) _ = (await Where(table, "1=1")).ToArray();
            return Table(table).OfType<JsonObject>();
        }
        internal async Task<JsonObject?> Find(string table, int id)
        {
            var found = Table(table).OfType<JsonObject>().FirstOrDefault(r => Id(r) == id);
            if (found is not null || fetch is null || loadedAll.Contains(table)) return found;
            return (await By(table, "Id", id)).FirstOrDefault();
        }
        internal void InitializeSequences()
        {
            var sequences = Document["_Sequences"] as JsonObject ?? new JsonObject();
            foreach (var (name, value) in Document.ToArray())
            {
                if (value is not JsonArray rows || sequences[name] is not null)
                    continue;
                var ids = rows.OfType<JsonObject>().Select(r => r["Id"] is JsonValue id && long.TryParse(id.ToJsonString(), out var number) ? number : 0L);
                sequences[name] = ids.DefaultIfEmpty().Max();
            }

            Document["_Sequences"] = sequences;
        }

        internal async Task<int> Next(string table) => checked((int)await NextLong(table));
        internal async Task<long> NextLong(string table)
        {
            var sequences = Document["_Sequences"]!.AsObject();
            if (counter is not null)
            {
                var allocated = await counter(table);
                sequences[table] = allocated;
                return allocated;
            }
            var next = checked((sequences[table] is { } value ? long.Parse(value.ToJsonString(), CultureInfo.InvariantCulture) : 0L) + 1);
            sequences[table] = next;
            return next;
        }

        internal JsonObject Add(string table, params (string Name, object? Value)[] fields)
        {
            var row = new JsonObject();
            foreach (var (name, value) in fields)
                row[name] = JsonSerializer.SerializeToNode(value);
            Table(table).Add(row);
            return row;
        }

        internal async Task RemoveBy(string table, string column, object? value, Func<JsonObject, bool>? filter = null)
        {
            foreach (var row in (await By(table, column, value)).Where(r => filter is null || filter(r)).ToArray()) Table(table).Remove(row);
        }
        internal async Task Remove(string table, Func<JsonObject, bool> filter)
        {
            foreach (var row in (await Rows(table)).Where(filter).ToArray())
                Table(table).Remove(row);
        }

        internal static int Id(JsonObject row) => Int(row, "Id");
        internal static long Long(JsonObject row, string key) => row[key] is { } value ? long.Parse(value.ToJsonString(), CultureInfo.InvariantCulture) : 0L;
        internal static int Int(JsonObject row, string key) => row[key]?.GetValue<int>() ?? 0;
        internal static int? OptionalInt(JsonObject row, string key) => row[key]?.GetValue<int>();
        internal static string Text(JsonObject row, string key) => row[key]?.GetValue<string>() ?? "";
        internal static bool Flag(JsonObject row, string key) => row[key]?.GetValue<bool>() ?? false;
        internal static DateTime? Date(JsonObject row, string key) => row[key]?.GetValue<DateTime>();
        internal static decimal Number(JsonObject row, string key) => row[key]?.GetValue<decimal>() ?? 0;
        internal static void Set(JsonObject row, string key, object? value) => row[key] = JsonSerializer.SerializeToNode(value);
        internal static void Revise(JsonObject row) => Set(row, "Revision", Int(row, "Revision") + 1);
        internal static void Revision(JsonObject row, int revision, string message)
        {
            if (Int(row, "Revision") != revision)
                throw new InvalidOperationException(message);
        }
    }
}

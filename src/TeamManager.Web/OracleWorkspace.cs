using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;
// Relational Oracle tables persist typed rows, including binary attachments.
// Oracle owns the write lock and commit boundary, including authorization and history.
public sealed partial class OracleWorkspace(IConfiguration configuration, IHttpContextAccessor? contextAccessor = null)
{
    private static readonly object RequestSnapshotKey = new();
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
        await using var transaction = target.BeginTransaction();
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

    internal async Task<T> Read<T>(Func<Workspace, T> action) => await Run(action, false);
    internal async Task<T> Write<T>(Func<Workspace, T> action) => await Run(action, true);
    private async Task<T> Run<T>(Func<Workspace, T> action, bool write)
    {
        var items = contextAccessor?.HttpContext?.Items;
        if (!write && items?[RequestSnapshotKey] is JsonObject snapshot)
            return action(new Workspace((JsonObject)snapshot.DeepClone()));
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction(write ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);
        await using var state = RelationalCommand(connection, transaction,
            "SELECT SCHEMA_VERSION FROM TMR_WORKSPACE WHERE ID=1" + (write ? " FOR UPDATE WAIT 10" : ""));
        var version = await state.ExecuteScalarAsync();
        if (version is null or DBNull || Convert.ToInt32(version) != Schema.Value.Version)
            throw new InvalidOperationException("Apply --migrate-oracle-workspace before starting the Oracle application.");
        var before = await LoadRelational(connection, transaction);
        var workspace = new Workspace((JsonObject)before.DeepClone());
        var result = action(workspace);
        if (write)
        {
            var after = NormalizeDocument(workspace.Document);
            await PersistChanges(connection, transaction, before, after);
            await using var update = RelationalCommand(connection, transaction,
                "UPDATE TMR_WORKSPACE SET REVISION=REVISION+1 WHERE ID=1");
            await update.ExecuteNonQueryAsync();
            await using var constraints = RelationalCommand(connection, transaction, "SET CONSTRAINTS ALL IMMEDIATE");
            await constraints.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        if (write) items?.Remove(RequestSnapshotKey);
        else if (items is not null) items[RequestSnapshotKey] = before;
        return result;
    }

    internal sealed class Workspace(JsonObject document)
    {
        internal JsonObject Document { get; } = document;

        internal IEnumerable<JsonObject> Rows(string table) => Table(table).OfType<JsonObject>();
        internal JsonArray Table(string table) => Document[table]?.AsArray() ?? throw new InvalidOperationException($"Missing Oracle workspace table {table}.");
        internal JsonObject? Find(string table, int id) => Rows(table).FirstOrDefault(r => Id(r) == id);
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

        internal int Next(string table) => checked((int)NextLong(table));
        internal long NextLong(string table)
        {
            var sequences = Document["_Sequences"]!.AsObject();
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

        internal void Remove(string table, Func<JsonObject, bool> filter)
        {
            foreach (var row in Rows(table).Where(filter).ToArray())
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

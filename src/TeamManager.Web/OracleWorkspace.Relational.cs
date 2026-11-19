using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private sealed record RelationalSchema(int Version, RelationalTable[] Tables);
    private sealed record RelationalTable(string Name, RelationalColumn[] Columns, RelationalKey[] Keys)
    {
        public string SqlName => Quote("TMR_" + Name);
        public string[] PrimaryKey => Keys.Single(k => k.Primary).Columns;
    }
    private sealed record RelationalKey(bool Primary, string[] Columns);
    private sealed record RelationalColumn(string Name, string Type, int Length, int Precision, int Scale,
        bool Nullable, string? DefaultValue)
    {
        public OracleDbType BindType => Type switch
        {
            "int" => OracleDbType.Int32,
            "bigint" => OracleDbType.Int64,
            "bit" => OracleDbType.Int16,
            "decimal" => OracleDbType.Decimal,
            "date" => OracleDbType.Date,
            "datetime2" => OracleDbType.TimeStamp,
            "binary" => OracleDbType.Raw,
            "varbinary" => OracleDbType.Blob,
            "nvarchar" when Length < 0 || Length > 4000 => OracleDbType.NClob,
            "nvarchar" => OracleDbType.NVarchar2,
            "varchar" or "uniqueidentifier" => OracleDbType.Varchar2,
            _ => throw new InvalidOperationException($"Unmapped Oracle column type {Type}.")
        };
    }

    private static readonly Lazy<RelationalSchema> Schema = new(() =>
        JsonSerializer.Deserialize<RelationalSchema>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "database", "oracle", "relational-schema.json")), new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true })!);
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static OracleCommand RelationalCommand(OracleConnection connection, OracleTransaction transaction, string sql)
        => new(sql, connection) { Transaction = transaction, BindByName = true, CommandTimeout = 60, InitialLOBFetchSize = -1 };

    // Normalize typed values rather than persisting JSON. Empty text is distinct from NULL
    // through a per-row bit mask because Oracle VARCHAR2 collapses an empty string to NULL.
    private static JsonObject NormalizeRow(RelationalTable table, JsonObject row, DateTime now)
    {
        if (row.Any(p => !table.Columns.Any(c => c.Name == p.Key)))
            throw new InvalidOperationException($"Unexpected column in {table.Name}; migration was not applied.");
        var result = new JsonObject();
        foreach (var column in table.Columns)
        {
            var node = row[column.Name];
            if (!row.ContainsKey(column.Name)) node = Default(column, now);
            if (node is null)
            {
                if (!column.Nullable) throw new InvalidOperationException($"Missing required value {table.Name}.{column.Name}.");
                result[column.Name] = null;
                continue;
            }
            object value = column.Type switch
            {
                "int" => int.Parse(node.ToJsonString(), CultureInfo.InvariantCulture),
                "bigint" => long.Parse(node.ToJsonString(), CultureInfo.InvariantCulture),
                "bit" => node.GetValue<bool>(),
                "decimal" => decimal.Parse(node.ToJsonString(), CultureInfo.InvariantCulture),
                "date" => DateTime.SpecifyKind(node.GetValue<DateTime>().Date, DateTimeKind.Unspecified),
                "datetime2" => DateTime.SpecifyKind(node.GetValue<DateTime>(), DateTimeKind.Unspecified),
                "uniqueidentifier" => Guid.Parse(node.GetValue<string>()).ToString("D"),
                _ => node.GetValue<string>()
            };
            result[column.Name] = JsonSerializer.SerializeToNode(value);
        }
        return result;
    }

    private static JsonNode? Default(RelationalColumn column, DateTime now)
    {
        if (column.DefaultValue is not { } sql) return null;
        sql = sql.Trim();
        while (sql.StartsWith('(') && sql.EndsWith(')')) sql = sql[1..^1].Trim();
        if (sql.Equals("sysutcdatetime()", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(now);
        if (sql.StartsWith("N'", StringComparison.Ordinal)) sql = sql[1..];
        if (sql.StartsWith('\'') && sql.EndsWith('\'')) return JsonValue.Create(sql[1..^1].Replace("''", "'"));
        return column.Type == "bit" ? JsonValue.Create(sql == "1") : JsonValue.Create(int.Parse(sql, CultureInfo.InvariantCulture));
    }

    private static JsonObject NormalizeDocument(JsonObject document, bool partial = false)
    {
        if (document.Any(p => p.Key != "_Sequences" && !Schema.Value.Tables.Any(t => t.Name == p.Key)))
            throw new InvalidOperationException("Unexpected source table; update the relational schema before migrating.");
        var normalized = new JsonObject();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        foreach (var table in Schema.Value.Tables)
        {
            if (partial && !document.ContainsKey(table.Name)) continue;
            if (document[table.Name] is not JsonArray rows)
                throw new InvalidOperationException($"Missing source table {table.Name}.");
            if (rows.Any(r => r is not JsonObject))
                throw new InvalidOperationException($"Invalid source row in {table.Name}.");
            normalized[table.Name] = new JsonArray(rows.OfType<JsonObject>().Select(r => (JsonNode)NormalizeRow(table, r, now)).ToArray());
        }
        var workspace = new Workspace(normalized);
        normalized["_Sequences"] = document["_Sequences"]?.DeepClone() ?? new JsonObject();
        if (!partial) workspace.InitializeSequences();
        return normalized;
    }

    private static async Task<JsonObject> LoadRelational(OracleConnection connection, OracleTransaction transaction)
    {
        FullReads.Add(1);
        var document = new JsonObject();
        foreach (var table in Schema.Value.Tables)
        {
            var rows = new JsonArray();
            await using var command = RelationalCommand(connection, transaction,
                $"SELECT \"__EMPTY\",{string.Join(',', table.Columns.Select(c => Quote(c.Name)))} FROM {table.SqlName}");
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var mask = reader.GetInt64(0);
                var row = new JsonObject();
                for (var i = 0; i < table.Columns.Length; i++)
                {
                    var column = table.Columns[i];
                    var ordinal = i + 1;
                    object? value;
                    if (reader.IsDBNull(ordinal)) value = (mask & (1L << i)) != 0 ? "" : null;
                    else value = column.Type switch
                    {
                        "int" => reader.GetInt32(ordinal),
                        "bigint" => reader.GetInt64(ordinal),
                        "bit" => reader.GetInt16(ordinal) != 0,
                        "decimal" => reader.GetDecimal(ordinal),
                        "date" or "datetime2" => DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Unspecified),
                        "binary" or "varbinary" => Convert.ToBase64String((byte[])reader.GetValue(ordinal)),
                        _ => reader.GetString(ordinal)
                    };
                    row[column.Name] = JsonSerializer.SerializeToNode(value);
                }
                rows.Add(row);
            }
            document[table.Name] = rows;
        }
        var counters = new JsonObject();
        await using (var command = RelationalCommand(connection, transaction, "SELECT TABLE_NAME,LAST_VALUE FROM TMR_COUNTERS"))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) counters[reader.GetString(0)] = reader.GetInt64(1);
        document["_Sequences"] = counters;
        return document;
    }

    private static string RowKey(RelationalTable table, JsonObject row)
        => new JsonArray(table.PrimaryKey.Select(k => row[k]?.DeepClone()).ToArray()).ToJsonString();

    private static void Bind(OracleCommand command, string parameter, RelationalColumn column, JsonNode? node)
    {
        object? value = node is null ? null : column.Type switch
        {
            "int" => node.GetValue<int>(),
            "bigint" => node.GetValue<long>(),
            "bit" => node.GetValue<bool>() ? 1 : 0,
            "decimal" => node.GetValue<decimal>(),
            "date" or "datetime2" => node.GetValue<DateTime>(),
            "binary" or "varbinary" => Convert.FromBase64String(node.GetValue<string>()),
            _ => node.GetValue<string>() is { Length: 0 } ? null : node.GetValue<string>()
        };
        command.Parameters.Add(parameter, column.BindType).Value = value ?? DBNull.Value;
    }

    private static async Task PersistChanges(OracleConnection connection, OracleTransaction transaction,
        JsonObject? before, JsonObject after, bool persistCounters = true)
    {
        // Foreign keys are deferred, so dependencies and cyclic project references can be
        // changed atomically. Never delete and reinsert unchanged records or binary LOBs.
        foreach (var table in Schema.Value.Tables)
        {
            if (!after.ContainsKey(table.Name)) continue;
            var previous = (before?[table.Name] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(r => RowKey(table, r));
            var current = after[table.Name]!.AsArray().OfType<JsonObject>().ToDictionary(r => RowKey(table, r));
            foreach (var (key, row) in previous.Where(p => !current.ContainsKey(p.Key)))
            {
                var predicates = table.PrimaryKey.Select((column, i) => $"{Quote(column)}=:k{i}");
                await using var delete = RelationalCommand(connection, transaction, $"DELETE FROM {table.SqlName} WHERE {string.Join(" AND ", predicates)}");
                for (var i = 0; i < table.PrimaryKey.Length; i++)
                {
                    var column = table.Columns.Single(c => c.Name == table.PrimaryKey[i]);
                    Bind(delete, "k" + i, column, row[column.Name]);
                }
                if (await delete.ExecuteNonQueryAsync() != 1) throw new DBConcurrencyException();
            }
            foreach (var (key, row) in current)
            {
                if (previous.TryGetValue(key, out var old) && JsonNode.DeepEquals(old, row)) continue;
                long empty = 0;
                for (var i = 0; i < table.Columns.Length; i++)
                    if (table.Columns[i].Type is "varchar" or "nvarchar" && row[table.Columns[i].Name]?.GetValue<string>() == "") empty |= 1L << i;
                var columns = table.Columns.Select(c => Quote(c.Name)).ToArray();
                var parameters = table.Columns.Select((_, i) => ":p" + i).ToArray();
                var insert = old is null;
                var assignments = columns.Select((column, i) => column + "=" + parameters[i]).Append("\"__EMPTY\"=:empty");
                var where = table.PrimaryKey.Select(k => $"{Quote(k)}=:p{Array.FindIndex(table.Columns, c => c.Name == k)}");
                var sql = insert
                    ? $"INSERT INTO {table.SqlName} ({string.Join(',', columns)},\"__EMPTY\") VALUES ({string.Join(',', parameters)},:empty)"
                    : $"UPDATE {table.SqlName} SET {string.Join(',', assignments)} WHERE {string.Join(" AND ", where)}";
                await using var command = RelationalCommand(connection, transaction, sql);
                for (var i = 0; i < table.Columns.Length; i++) Bind(command, "p" + i, table.Columns[i], row[table.Columns[i].Name]);
                command.Parameters.Add("empty", OracleDbType.Int64).Value = empty;
                if (await command.ExecuteNonQueryAsync() != 1) throw new DBConcurrencyException();
            }
        }
        if (!persistCounters) return;
        foreach (var (name, value) in after["_Sequences"]!.AsObject())
        {
            if (before?["_Sequences"]?[name] is { } previous && JsonNode.DeepEquals(previous, value)) continue;
            await using var counter = RelationalCommand(connection, transaction, """
                MERGE INTO TMR_COUNTERS c USING (SELECT :name TABLE_NAME,:value LAST_VALUE FROM DUAL) s
                ON (c.TABLE_NAME=s.TABLE_NAME) WHEN MATCHED THEN UPDATE SET c.LAST_VALUE=s.LAST_VALUE
                WHEN NOT MATCHED THEN INSERT (TABLE_NAME,LAST_VALUE) VALUES(s.TABLE_NAME,s.LAST_VALUE)
                """);
            counter.Parameters.Add("name", OracleDbType.Varchar2).Value = name;
            counter.Parameters.Add("value", OracleDbType.Int64).Value = value!.GetValue<long>();
            await counter.ExecuteNonQueryAsync();
        }
    }
}

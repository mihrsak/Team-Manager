using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Oracle.ManagedDataAccess.Client;
using System.Diagnostics.Metrics;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private static readonly Meter QueryMeter = new("TeamManager.Oracle");
    private static readonly Counter<long> SelectedRows = QueryMeter.CreateCounter<long>("oracle.selected_rows");
    private static readonly Counter<long> FullReads = QueryMeter.CreateCounter<long>("oracle.full_workspace_reads");

    // Values are always bound. Table/column names and predicates come only from store code.
    private static JsonArray SelectRows(OracleConnection connection, OracleTransaction transaction,
        string name, string predicate, (string Name, object? Value)[] parameters)
    {
        var table = Schema.Value.Tables.Single(t => t.Name == name);
        using var command = RelationalCommand(connection, transaction,
            $"SELECT t.\"__EMPTY\",{string.Join(',', table.Columns.Select(c => "t." + Quote(c.Name)))} FROM {table.SqlName} t WHERE {predicate}");
        foreach (var (key, value) in parameters) AddQueryParameter(command, key, value);
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read()) rows.Add(ReadTypedRow(table, reader));
        SelectedRows.Add(rows.Count, new KeyValuePair<string, object?>("query", name));
        return rows;
    }

    private static JsonObject ReadTypedRow(RelationalTable table, OracleDataReader reader)
    {
        var mask = reader.GetInt64(0);
        var row = new JsonObject();
        for (var i = 0; i < table.Columns.Length; i++)
        {
            var column = table.Columns[i];
            var ordinal = i + 1;
            object? value = reader.IsDBNull(ordinal) ? ((mask & (1L << i)) != 0 ? "" : null) : column.Type switch
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
        return row;
    }

    private static void AddQueryParameter(OracleCommand command, string name, object? value)
    {
        var type = value switch
        {
            int or null => OracleDbType.Int32,
            long => OracleDbType.Int64,
            DateTime => OracleDbType.TimeStamp,
            _ => OracleDbType.NVarchar2
        };
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }
}

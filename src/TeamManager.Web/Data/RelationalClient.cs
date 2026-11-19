using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Odp = Oracle.ManagedDataAccess.Client;
using Db2 = IBM.Data.Db2;

namespace TeamManager.Web.Data;

// Keep provider-specific mechanics outside the transaction/authorization domain.
internal sealed class RelationalConnection : IAsyncDisposable
{
    internal DbConnection Inner { get; }
    internal bool IsDb2 { get; }
    internal RelationalConnection(string connectionString)
    {
        IsDb2 = connectionString.StartsWith("db2:", StringComparison.Ordinal);
        Inner = IsDb2 ? Db2Connection(connectionString[4..]) : new Odp.OracleConnection(connectionString);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static DbConnection Db2Connection(string value) => new Db2.DB2Connection(value);
    internal Task OpenAsync(CancellationToken token = default) => IsDb2 ? Task.Run(Inner.Open, token) : Inner.OpenAsync(token);
    internal async Task<RelationalTransaction> BeginTransactionAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken token = default)
        => new(await Inner.BeginTransactionAsync(isolation, token));
    public ValueTask DisposeAsync() => Inner.DisposeAsync();
}

internal sealed class RelationalTransaction(DbTransaction transaction) : IAsyncDisposable
{
    internal DbTransaction Inner { get; } = transaction;
    internal Task CommitAsync(CancellationToken token = default) => Inner.CommitAsync(token);
    internal Task RollbackAsync(CancellationToken token = default) => Inner.RollbackAsync(token);
    public ValueTask DisposeAsync() => Inner.DisposeAsync();
}

internal sealed class RelationalCommand : IAsyncDisposable
{
    private readonly RelationalConnection connection;
    private readonly Odp.OracleCommand template;
    private DbCommand? active;
    internal RelationalCommand(string sql, RelationalConnection connection)
    {
        this.connection = connection;
        template = connection.IsDb2 ? new() : new(sql, (Odp.OracleConnection)connection.Inner);
        template.CommandText = sql;
    }
    internal string CommandText { get => template.CommandText; set => template.CommandText = value; }
    internal bool BindByName { get => template.BindByName; set => template.BindByName = value; }
    internal int CommandTimeout { get => template.CommandTimeout; set => template.CommandTimeout = value; }
    internal int InitialLOBFetchSize { get => template.InitialLOBFetchSize; set => template.InitialLOBFetchSize = value; }
    internal Odp.OracleParameterCollection Parameters => template.Parameters;
    internal RelationalTransaction? Transaction { get; set; }
    private DbCommand Prepare()
    {
        if (!connection.IsDb2)
        {
            template.Transaction = (Odp.OracleTransaction?)Transaction?.Inner;
            return template;
        }
        return PrepareDb2();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private DbCommand PrepareDb2()
    {
        active?.Dispose();
        var command = connection.Inner.CreateCommand();
        command.Transaction = Transaction?.Inner;
        command.CommandTimeout = CommandTimeout;
        var parameters = template.Parameters.Cast<Odp.OracleParameter>().ToDictionary(p => p.ParameterName.TrimStart(':'), StringComparer.OrdinalIgnoreCase);
        var sql = CommandText.Replace("SYS_EXTRACT_UTC(SYSTIMESTAMP)", "(CURRENT TIMESTAMP - CURRENT TIMEZONE)", StringComparison.OrdinalIgnoreCase)
            .Replace("DBMS_LOB.GETLENGTH(", "LENGTH(", StringComparison.OrdinalIgnoreCase)
            .Replace("DEFERRABLE INITIALLY DEFERRED", "", StringComparison.OrdinalIgnoreCase);
        if (Regex.IsMatch(sql, @"CREATE TABLE", RegexOptions.IgnoreCase))
        {
            sql = Regex.Replace(sql, @"\bBLOB\b(?!\s*\()", "BLOB(10M)", RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"\bRAW\((\d+)\)", "VARBINARY($1)", RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"(\b(?:NUMBER|VARCHAR2)\(\d+\)) PRIMARY KEY", "$1 NOT NULL PRIMARY KEY", RegexOptions.IgnoreCase);
            var primary = Regex.Match(sql, @"PRIMARY KEY \(([^)]+)\)");
            foreach (var column in primary.Groups[1].Value.Split(',').Where(s => s.Length > 0))
                sql = Regex.Replace(sql, @"(?m)^(\s*" + Regex.Escape(column.Trim()) + @"\s+\w+\([^)]*\))(?!\s+NOT NULL)", "$1 NOT NULL");
        }
        var additional = new List<(string Name, int Value)>();
        var index = 0;
        // This is a deliberately narrow dialect rule: only internally generated integer ID arrays.
        sql = Regex.Replace(sql, @"JSON_TABLE\(:(\w+),'\$\[\*\]'\s+COLUMNS\(id NUMBER PATH '\$'\)\)", match =>
        {
            var ids = JsonSerializer.Deserialize<int[]>((string)parameters[match.Groups[1].Value].Value) ?? [];
            if (ids.Length == 0) return "(SELECT CAST(NULL AS INTEGER) id FROM SYSIBM.SYSDUMMY1 WHERE 1=0) empty_ids";
            var names = ids.Select(id => { var name = "array_id_" + index++; additional.Add((name, id)); return "(@" + name + ")"; });
            return "(VALUES " + string.Join(',', names) + ") array_ids_" + index + "(id)";
        }, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"'([^']|'')*'|""([^""]|"""")*""|:(?<parameter>[A-Za-z]\w*)", match =>
            match.Groups["parameter"].Success && match.Groups["parameter"].Value is not ("NEW" or "OLD") ? "@" + match.Groups["parameter"].Value : match.Value);
        sql = Regex.Replace(sql, @"CREATE SEQUENCE (""[^""]+""|\w+) START", "CREATE SEQUENCE $1 AS BIGINT START", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"CREATE (UNIQUE )?INDEX IF NOT EXISTS", "CREATE $1INDEX", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(sql, @"CREATE UNIQUE INDEX", RegexOptions.IgnoreCase) && !sql.Contains("EXCLUDE NULL KEYS", StringComparison.OrdinalIgnoreCase)) sql += " EXCLUDE NULL KEYS";
        if (sql.Contains("q.ROWID", StringComparison.OrdinalIgnoreCase))
        {
            sql = sql.Replace("q.ROWID", "RID_BIT(q)", StringComparison.OrdinalIgnoreCase);
            sql = Regex.Replace(sql, "SELECT ROWID rid FROM (\"[^\"]+\")", "SELECT RID_BIT(queue_row) rid FROM $1 queue_row", RegexOptions.IgnoreCase);
            sql = sql.Replace("SKIP LOCKED", "SKIP LOCKED DATA", StringComparison.OrdinalIgnoreCase);
        }
        foreach (var parameter in additional) parameters.Add(parameter.Name, new Odp.OracleParameter(parameter.Name, Odp.OracleDbType.Int32) { Value = parameter.Value });
        var bindings = new List<Odp.OracleParameter>();
        // Use positional markers: the IBM named-marker parser misreads colons inside literals.
        sql = Regex.Replace(sql, @"'([^']|'')*'|""([^""]|"""")*""|@(?<parameter>[A-Za-z]\w*)", match =>
        {
            if (!match.Groups["parameter"].Success) return match.Value;
            bindings.Add(parameters[match.Groups["parameter"].Value]);
            return "?";
        });
        command.CommandText = sql;
        foreach (var parameter in bindings)
        {
            var native = new Db2.DB2Parameter("bind" + command.Parameters.Count, parameter.OracleDbType switch
            {
                Odp.OracleDbType.Int16 => Db2.DB2Type.SmallInt,
                Odp.OracleDbType.Int32 => Db2.DB2Type.Integer,
                Odp.OracleDbType.Int64 => Db2.DB2Type.BigInt,
                Odp.OracleDbType.Decimal => Db2.DB2Type.Decimal,
                Odp.OracleDbType.Date => Db2.DB2Type.Date,
                Odp.OracleDbType.TimeStamp => Db2.DB2Type.Timestamp,
                Odp.OracleDbType.Raw => Db2.DB2Type.VarBinary,
                Odp.OracleDbType.Blob => Db2.DB2Type.Blob,
                Odp.OracleDbType.Clob or Odp.OracleDbType.NClob => Db2.DB2Type.Clob,
                _ => Db2.DB2Type.VarChar
            }) { Value = parameter.Value, Direction = parameter.Direction };
            command.Parameters.Add(native);
        }
        active = command;
        return command;
    }
    internal async Task<int> ExecuteNonQueryAsync(CancellationToken token = default)
    {
        if (connection.IsDb2)
        {
            if (CommandText.Trim().Equals("SET CONSTRAINTS ALL IMMEDIATE", StringComparison.OrdinalIgnoreCase)) return 0;
            var index = Regex.Match(CommandText, "CREATE (?:UNIQUE )?INDEX IF NOT EXISTS (?:\"([^\"]+)\"|(\\w+))", RegexOptions.IgnoreCase);
            if (index.Success)
            {
                await using var check = connection.Inner.CreateCommand();
                check.Transaction = Transaction?.Inner;
                var name = index.Groups[1].Success ? index.Groups[1].Value : index.Groups[2].Value.ToUpperInvariant();
                check.CommandText = "SELECT COUNT(*) FROM SYSCAT.INDEXES WHERE INDSCHEMA=CURRENT SCHEMA AND INDNAME='" + name.Replace("'", "''") + "'";
                if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) > 0) return 0;
            }
        }
        var command = Prepare();
        return connection.IsDb2 ? await RunDb2(command, c => c.ExecuteNonQuery(), token) : await command.ExecuteNonQueryAsync(token);
    }
    // IBM 10.0.0.400 inherits DbCommand's synchronous async implementations. Isolate
    // blocking native I/O so callers can await locks without blocking independent work.
    private static async Task<T> RunDb2<T>(DbCommand command, Func<DbCommand,T> execute, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var cancellation = token.Register(() => { try { command.Cancel(); } catch (DbException) { } catch (NotSupportedException) { } catch (InvalidOperationException) { } });
        return await Task.Run(() => execute(command));
    }
    internal async Task<object?> ExecuteScalarAsync(CancellationToken token = default)
    {
        var command = Prepare();
        return connection.IsDb2 ? await RunDb2(command, c => c.ExecuteScalar(), token) : await command.ExecuteScalarAsync(token);
    }
    internal async Task<RelationalReader> ExecuteReaderAsync(CancellationToken token = default)
    {
        var command = Prepare();
        return new(connection.IsDb2 ? await RunDb2(command, c => c.ExecuteReader(), token) : await command.ExecuteReaderAsync(token), connection.IsDb2);
    }
    public async ValueTask DisposeAsync()
    {
        if (active is not null) await active.DisposeAsync();
        await template.DisposeAsync();
    }
}

internal sealed class RelationalReader(DbDataReader reader, bool isDb2 = false) : IAsyncDisposable
{
    internal Task<bool> ReadAsync(CancellationToken token = default) => isDb2 ? Task.Run(reader.Read, token) : reader.ReadAsync(token);
    internal int FieldCount => reader.FieldCount;
    internal string GetName(int ordinal) => reader.GetName(ordinal);
    internal bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
    internal object GetValue(int ordinal) => reader.GetValue(ordinal);
    internal string GetString(int ordinal) => reader.GetString(ordinal);
    internal int GetInt32(int ordinal) => Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    internal long GetInt64(int ordinal) => Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    internal short GetInt16(int ordinal) => Convert.ToInt16(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    internal decimal GetDecimal(int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    internal DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
    internal T GetFieldValue<T>(int ordinal) => reader.GetFieldValue<T>(ordinal);
    public ValueTask DisposeAsync() => reader.DisposeAsync();
}

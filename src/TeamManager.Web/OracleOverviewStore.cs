using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TeamManager.Web;

public record OracleOverviewTask(int Id, string Title, string Status, int Revision);

public sealed class OracleOverviewStore(IConfiguration configuration) : IHealthCheck
{
    internal static readonly IReadOnlyDictionary<string, string> StatusLabels = new Dictionary<string, string>
    {
        ["new"] = "Novo", ["active"] = "U tijeku", ["done"] = "Završeno"
    };

    internal static bool Enabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("OracleOverview:Enabled");

    private OracleConnection Connect()
    {
        var connectionString = configuration.GetConnectionString("TeamManagerOracle");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure ConnectionStrings:TeamManagerOracle first.");
        return new(connectionString);
    }

    public async Task Install(bool apply, bool demo)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        if (apply)
        {
            await using var migration = Command(connection,
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "oracle", "001-overview-tasks.sql")));
            await migration.ExecuteNonQueryAsync();
        }
        if (demo)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var seed = Command(connection,
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "oracle", "demo-overview-tasks.sql")));
            seed.Transaction = (OracleTransaction)transaction;
            await seed.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }

    public async Task<List<OracleOverviewTask>> List(CancellationToken cancellationToken)
    {
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = Command(connection, "SELECT ID, TITLE, STATUS, REVISION FROM TM_OVERVIEW_TASKS ORDER BY ID DESC");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tasks = new List<OracleOverviewTask>();
        while (await reader.ReadAsync(cancellationToken))
            tasks.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return tasks;
    }

    public async Task<bool> SetStatus(int id, int revision, string status, CancellationToken cancellationToken)
    {
        if (!StatusLabels.ContainsKey(status)) throw new ArgumentException("Invalid task status.", nameof(status));
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = Command(connection,
            "UPDATE TM_OVERVIEW_TASKS SET STATUS = :status, REVISION = REVISION + 1 WHERE ID = :id AND REVISION = :revision");
        command.Transaction = (OracleTransaction)transaction;
        command.Parameters.Add("status", OracleDbType.Varchar2).Value = status;
        command.Parameters.Add("id", OracleDbType.Int32).Value = id;
        command.Parameters.Add("revision", OracleDbType.Int32).Value = revision;
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = Command(connection,
            "SELECT ID, TITLE, STATUS, REVISION FROM TM_OVERVIEW_TASKS WHERE 1 = 0");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }

    private static OracleCommand Command(OracleConnection connection, string sql) =>
        new(sql, connection) { BindByName = true, CommandTimeout = 10 };
}

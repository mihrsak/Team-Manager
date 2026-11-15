using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TeamManager.Web;

public sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = new SqlConnectionStringBuilder(configuration.GetConnectionString("TeamManager"))
        {
            ConnectTimeout = 3
        };
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT TOP (0) IsActive, SessionVersion FROM dbo.AppUsers", connection)
        {
            CommandTimeout = 3
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

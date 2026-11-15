using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TeamManager.Web;

public sealed class DatabaseHealthPublisher(ILogger<DatabaseHealthPublisher> logger) : IHealthCheckPublisher
{
    private HealthStatus? previousStatus;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var currentStatus = report.Status;
        if (currentStatus == previousStatus)
            return Task.CompletedTask;

        var level = currentStatus == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning;
        logger.Log(level, "SQL readiness changed from {PreviousStatus} to {Status}",
            previousStatus, currentStatus);
        previousStatus = currentStatus;
        return Task.CompletedTask;
    }
}

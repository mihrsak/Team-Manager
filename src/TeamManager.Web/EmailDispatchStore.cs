using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record EmailJob(int UserId, int Key, DateTime? DueDate, Guid Lease, int Attempts);
public sealed record EmailDelivery(string Address, string Language, int IssueId);

public sealed class EmailDispatchStore(IConfiguration config, IssueStore issues)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task QueueReminders(DateTime today, CancellationToken cancellationToken)
    {
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT dbo.IssueReminderQueue(UserId,IssueId,DueDate)
            SELECT i.AssigneeId,i.Id,i.DueDate
            FROM dbo.Issues i
            JOIN dbo.IssueStatuses s ON s.Name=i.Status AND s.IsClosed=0
            JOIN dbo.Projects p ON p.Id=i.ProjectId
            JOIN dbo.AppUsers u ON u.Id=i.AssigneeId AND u.IsActive=1
            JOIN dbo.EmailNotificationPreferences e ON e.UserId=u.Id AND e.SendDueReminders=1
            WHERE i.DueDate=@today AND p.IssuesEnabled=1 AND p.State='Active'
              AND (p.IsPublic=1 OR EXISTS(
                       SELECT 1 FROM dbo.EffectiveUserRoles r JOIN dbo.RolePermissions rp ON rp.RoleName=r.RoleName
                       WHERE r.UserId=u.Id AND rp.Permission IN ('*','issues.view'))
                  OR EXISTS(
                       SELECT 1 FROM dbo.EffectiveProjectRoles(p.Id,u.Id) r JOIN dbo.RolePermissions rp ON rp.RoleName=r.RoleName
                       WHERE rp.Permission IN ('*','issues.view')))
              AND NOT EXISTS(SELECT 1 FROM dbo.IssueReminderQueue q WITH(UPDLOCK,HOLDLOCK)
                             WHERE q.UserId=u.Id AND q.IssueId=i.Id AND q.DueDate=i.DueDate)
            OPTION(MAXRECURSION 0)
            """, connection);
        command.Parameters.Add(new SqlParameter("@today",System.Data.SqlDbType.Date){Value=today});
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EmailJob?> Claim(bool reminder, CancellationToken cancellationToken)
    {
        var (table,key) = Queue(reminder);
        var lease = Guid.NewGuid();
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,cancellationToken);
        await using var command = new SqlCommand($"""
            WITH nextJob AS (
                SELECT TOP (1) * FROM dbo.{table} WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
                WHERE CompletedAt IS NULL AND AvailableAt<=SYSUTCDATETIME()
                ORDER BY AvailableAt,UserId,{key}
            )
            UPDATE nextJob SET LeaseId=@lease,AvailableAt=DATEADD(minute,2,SYSUTCDATETIME())
            OUTPUT inserted.UserId,inserted.{key},{(reminder?"inserted.DueDate":"CAST(NULL AS date)")},inserted.Attempts;
            """, connection,transaction);
        command.Parameters.Add(P("@lease",lease));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var job = await reader.ReadAsync(cancellationToken)
            ? new EmailJob(reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2)?null:reader.GetDateTime(2),lease,reader.GetInt32(3))
            : null;
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<EmailDelivery?> Resolve(EmailJob job, CancellationToken cancellationToken)
    {
        var reminder = job.DueDate is not null;
        var sql = reminder
            ? """
              SELECT e.Address,u.UiLanguage,i.Id,i.ProjectId
              FROM dbo.IssueReminderQueue q
              JOIN dbo.Issues i ON i.Id=q.IssueId AND i.AssigneeId=q.UserId AND i.DueDate=q.DueDate
              JOIN dbo.IssueStatuses s ON s.Name=i.Status AND s.IsClosed=0
              JOIN dbo.Projects p ON p.Id=i.ProjectId AND p.State='Active'
              JOIN dbo.AppUsers u ON u.Id=q.UserId AND u.IsActive=1
              JOIN dbo.EmailNotificationPreferences e ON e.UserId=u.Id AND e.SendDueReminders=1
              WHERE q.UserId=@user AND q.IssueId=@key AND q.DueDate=@due AND q.LeaseId=@lease
              """
            : """
              SELECT e.Address,u.UiLanguage,i.Id,i.ProjectId
              FROM dbo.IssueEmailQueue q
              JOIN dbo.IssueNotifications n ON n.UserId=q.UserId AND n.EventId=q.EventId
              JOIN dbo.IssueEvents activity ON activity.Id=n.EventId
              JOIN dbo.Issues i ON i.Id=n.IssueId
              JOIN dbo.Projects p ON p.Id=i.ProjectId AND (activity.Kind<>'time' OR p.TimeEnabled=1)
              JOIN dbo.AppUsers u ON u.Id=q.UserId AND u.IsActive=1
              JOIN dbo.EmailNotificationPreferences e ON e.UserId=u.Id AND e.SendWatchedIssues=1
              WHERE q.UserId=@user AND q.EventId=@key AND q.LeaseId=@lease
              """;
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(JobParameters(job));
        EmailDelivery delivery;
        int project;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            delivery = new(reader.GetString(0),reader.GetString(1),reader.GetInt32(2));
            project = reader.GetInt32(3);
        }

        var visible = await issues.Get(job.UserId,delivery.IssueId);
        return visible?.ProjectId == project ? delivery : null;
    }

    public async Task Complete(EmailJob job, bool failed, int retrySeconds, CancellationToken cancellationToken)
    {
        var (table,key) = Queue(job.DueDate is not null);
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            UPDATE dbo.{table}
            SET Attempts=Attempts+@failed, LeaseId=NULL,
                CompletedAt=CASE WHEN @failed=0 OR Attempts+1>=5 THEN SYSUTCDATETIME() END,
                AvailableAt=DATEADD(second,@retry,SYSUTCDATETIME())
            WHERE UserId=@user AND {key}=@key AND LeaseId=@lease
                {(job.DueDate is not null?"AND DueDate=@due":"")}
            """,connection);
        command.Parameters.AddRange(JobParameters(job));
        command.Parameters.AddRange([P("@failed",failed?1:0),P("@retry",retrySeconds)]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string Table,string Key) Queue(bool reminder)
        => reminder ? ("IssueReminderQueue","IssueId") : ("IssueEmailQueue","EventId");

    private static SqlParameter[] JobParameters(EmailJob job) =>
        [P("@user",job.UserId),P("@key",job.Key),P("@lease",job.Lease),
         new SqlParameter("@due",System.Data.SqlDbType.Date){Value=(object?)job.DueDate??DBNull.Value}];
}

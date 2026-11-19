using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private static (string Table, string Key) DeliveryQueue(bool reminder)
        => reminder ? ("IssueReminderQueue", "IssueId") : ("IssueEmailQueue", "EventId");

    internal async Task QueueMailReminders(DateTime today, CancellationToken token)
    {
        var candidates = await Query("ReminderCandidates", """
            SELECT i."Id",i."AssigneeId" FROM "TMR_Issues" i
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" AND p."State"='Active' AND p."IssuesEnabled"=1
            JOIN "TMR_IssueStatuses" s ON s."Name"=i."Status" AND s."IsClosed"=0
            JOIN "TMR_AppUsers" u ON u."Id"=i."AssigneeId" AND u."IsActive"=1
            JOIN "TMR_EmailNotificationPreferences" e ON e."UserId"=u."Id" AND e."SendDueReminders"=1
            WHERE i."DueDate"=:today AND NOT EXISTS (SELECT 1 FROM "TMR_IssueReminderQueue" q WHERE q."IssueId"=i."Id" AND q."UserId"=u."Id" AND q."DueDate"=i."DueDate")
            ORDER BY i."Id" FETCH FIRST 200 ROWS ONLY
            """, c => AddQueryParameter(c, "today", today.Date), r => (Issue: r.GetInt32(0), User: r.GetInt32(1)));
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await WriteIssueChanges(candidate.User, [candidate.Issue], async w =>
                {
                    var issue = await RequireIssue(w, candidate.User, candidate.Issue);
                    if (OptionalInt(issue, "AssigneeId") != candidate.User || Date(issue, "DueDate")?.Date != today.Date ||
                        (await w.Find("Projects", Int(issue, "ProjectId"))) is not { } project || Text(project, "State") != "Active" ||
                        (await w.By("IssueStatuses", "Name", Text(issue, "Status"))).Any(s => Flag(s, "IsClosed")) ||
                        !(await w.By("EmailNotificationPreferences", "UserId", candidate.User)).Any(e => Flag(e, "SendDueReminders"))) return false;
                    if ((await w.By("IssueReminderQueue", "IssueId", candidate.Issue)).Any(q => Int(q, "UserId") == candidate.User && Date(q, "DueDate")?.Date == today.Date)) return false;
                    w.Add("IssueReminderQueue", ("UserId", candidate.User), ("IssueId", candidate.Issue), ("DueDate", today.Date), ("Attempts", 0),
                        ("AvailableAt", DateTime.UtcNow), ("LeaseId", null), ("CompletedAt", null));
                    return true;
                });
            }
            catch (UnauthorizedAccessException) { /* Rights changed after discovery; do not queue inaccessible work. */ }
        }
    }

    internal async Task<EmailJob?> ClaimEmail(bool reminder, CancellationToken token)
    {
        var (table, key) = DeliveryQueue(reminder);
        await using var connection = Connect();
        await connection.OpenAsync(token);
        await using var transaction = (OracleTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await using var command = RelationalCommand(connection, transaction, $"""
            SELECT q."UserId",q.{Quote(key)},{(reminder ? "q.\"DueDate\"" : "CAST(NULL AS DATE)")},q."Attempts"
            FROM {Quote("TMR_" + table)} q WHERE q.ROWID=(
                SELECT rid FROM (SELECT ROWID rid FROM {Quote("TMR_" + table)}
                WHERE "CompletedAt" IS NULL AND "AvailableAt"<=SYS_EXTRACT_UTC(SYSTIMESTAMP)
                ORDER BY "AvailableAt","UserId",{Quote(key)}) WHERE ROWNUM=1)
            FOR UPDATE SKIP LOCKED
            """);
        var lease = Guid.NewGuid();
        EmailJob? job;
        await using (var reader = await command.ExecuteReaderAsync(token))
            job = await reader.ReadAsync(token) ? new(reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2), lease, reader.GetInt32(3)) : null;
        if (job is not null)
        {
            command.CommandText = $"UPDATE {Quote("TMR_" + table)} SET \"LeaseId\"=:lease,\"AvailableAt\"=:available WHERE \"UserId\"=:userId AND {Quote(key)}=:key" + (reminder ? " AND \"DueDate\"=:due" : "");
            AddQueryParameter(command, "lease", lease.ToString("D")); AddQueryParameter(command, "available", DateTime.UtcNow.AddMinutes(2));
            AddQueryParameter(command, "userId", job.UserId); AddQueryParameter(command, "key", job.Key);
            if (reminder) AddQueryParameter(command, "due", job.DueDate);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return job;
    }

    internal async Task<EmailDelivery?> ResolveEmail(EmailJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reminder = job.DueDate is not null;
        var sql = reminder ? """
            SELECT e."Address",u."UiLanguage",i."Id",i."ProjectId" FROM "TMR_IssueReminderQueue" q
            JOIN "TMR_Issues" i ON i."Id"=q."IssueId" AND i."AssigneeId"=q."UserId" AND i."DueDate"=q."DueDate"
            JOIN "TMR_IssueStatuses" s ON s."Name"=i."Status" AND s."IsClosed"=0
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" AND p."State"='Active'
            JOIN "TMR_AppUsers" u ON u."Id"=q."UserId" AND u."IsActive"=1
            JOIN "TMR_EmailNotificationPreferences" e ON e."UserId"=u."Id" AND e."SendDueReminders"=1
            WHERE q."UserId"=:userId AND q."IssueId"=:key AND q."DueDate"=:due AND q."LeaseId"=:lease
            """ : """
            SELECT e."Address",u."UiLanguage",i."Id",i."ProjectId" FROM "TMR_IssueEmailQueue" q
            JOIN "TMR_IssueNotifications" n ON n."UserId"=q."UserId" AND n."EventId"=q."EventId"
            JOIN "TMR_IssueEvents" a ON a."Id"=n."EventId" JOIN "TMR_Issues" i ON i."Id"=n."IssueId"
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" AND (a."Kind"<>'time' OR p."TimeEnabled"=1)
            JOIN "TMR_AppUsers" u ON u."Id"=q."UserId" AND u."IsActive"=1
            JOIN "TMR_EmailNotificationPreferences" e ON e."UserId"=u."Id" AND e."SendWatchedIssues"=1
            WHERE q."UserId"=:userId AND q."EventId"=:key AND q."LeaseId"=:lease
            """;
        var rows = await Query("EmailDelivery", sql, c => BindEmail(c, job, reminder), r => (Delivery: new EmailDelivery(r.GetString(0), r.GetString(1), r.GetInt32(2)), Project: r.GetInt32(3)));
        if (rows.Count == 0) return null;
        var row = rows[0];
        return (await IssueSql(job.UserId, row.Delivery.IssueId))?.ProjectId == row.Project ? row.Delivery : null;
    }

    internal async Task CompleteEmail(EmailJob job, bool failed, int retrySeconds, CancellationToken token)
    {
        var (table, key) = DeliveryQueue(job.DueDate is not null);
        await using var connection = Connect();
        await connection.OpenAsync(token);
        await using var command = new OracleCommand($"""
            UPDATE {Quote("TMR_" + table)} SET "Attempts"="Attempts"+:failed,"LeaseId"=NULL,
                "CompletedAt"=CASE WHEN :failed=0 OR "Attempts"+1>=5 THEN SYS_EXTRACT_UTC(SYSTIMESTAMP) ELSE NULL END,
                "AvailableAt"=:available WHERE "UserId"=:userId AND {Quote(key)}=:key AND "LeaseId"=:lease
                {(job.DueDate is not null ? "AND \"DueDate\"=:due" : "")}
            """, connection) { BindByName = true, CommandTimeout = 30 };
        BindEmail(command, job, job.DueDate is not null); AddQueryParameter(command, "failed", failed ? 1 : 0);
        AddQueryParameter(command, "available", DateTime.UtcNow.AddSeconds(retrySeconds));
        await command.ExecuteNonQueryAsync(token);
    }

    private static void BindEmail(OracleCommand command, EmailJob job, bool due)
    {
        AddQueryParameter(command, "userId", job.UserId); AddQueryParameter(command, "key", job.Key); AddQueryParameter(command, "lease", job.Lease.ToString("D"));
        if (due) AddQueryParameter(command, "due", job.DueDate);
    }
}

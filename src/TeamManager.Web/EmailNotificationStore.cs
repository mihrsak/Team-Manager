using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record EmailNotificationPreferences(string? Address, bool SendWatchedIssues, bool SendDueReminders);

public sealed class EmailNotificationStore(IConfiguration config, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<EmailNotificationPreferences> Get(int user)
    {
        if (oracle.Enabled) return await oracle.MailPreferences(user);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Address,SendWatchedIssues,SendDueReminders FROM dbo.EmailNotificationPreferences WHERE UserId=@user", connection);
        command.Parameters.Add(P("@user", user));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)) : new(null, false, false);
    }

    public async Task Save(int user, string? address, bool sendWatchedIssues, bool sendDueReminders)
    {
        if (oracle.Enabled) { await oracle.SaveMailPreferences(user, address, sendWatchedIssues, sendDueReminders); return; }
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand("""
            DECLARE @oldAddress nvarchar(254);
            SELECT @oldAddress=Address FROM dbo.EmailNotificationPreferences WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@user;
            IF @@ROWCOUNT=0
                INSERT dbo.EmailNotificationPreferences(UserId,Address,SendWatchedIssues,SendDueReminders) VALUES(@user,@address,@send,@reminders);
            ELSE
                UPDATE dbo.EmailNotificationPreferences SET Address=@address,SendWatchedIssues=@send,SendDueReminders=@reminders WHERE UserId=@user;

            IF @send=0 OR COALESCE(@oldAddress,N'')<>COALESCE(@address,N'')
                DELETE dbo.IssueEmailQueue WHERE UserId=@user AND CompletedAt IS NULL;
            IF @reminders=0 OR COALESCE(@oldAddress,N'')<>COALESCE(@address,N'')
                DELETE dbo.IssueReminderQueue WHERE UserId=@user AND CompletedAt IS NULL;
            """, connection, transaction);
        command.Parameters.AddRange([P("@user", user), P("@address", address), P("@send", sendWatchedIssues), P("@reminders", sendDueReminders)]);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}

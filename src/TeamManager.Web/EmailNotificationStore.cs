using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record EmailNotificationPreferences(string? Address, bool SendWatchedIssues);

public sealed class EmailNotificationStore(IConfiguration config)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task<EmailNotificationPreferences> Get(int user)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Address,SendWatchedIssues FROM dbo.EmailNotificationPreferences WHERE UserId=@user", connection);
        command.Parameters.Add(P("@user",user));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.IsDBNull(0)?null:reader.GetString(0),reader.GetBoolean(1)) : new(null,false);
    }

    public async Task Save(int user, string? address, bool sendWatchedIssues)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand("""
            DECLARE @oldAddress nvarchar(254);
            SELECT @oldAddress=Address FROM dbo.EmailNotificationPreferences WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@user;
            IF @@ROWCOUNT=0
                INSERT dbo.EmailNotificationPreferences(UserId,Address,SendWatchedIssues) VALUES(@user,@address,@send);
            ELSE
                UPDATE dbo.EmailNotificationPreferences SET Address=@address,SendWatchedIssues=@send WHERE UserId=@user;

            IF @send=0 OR COALESCE(@oldAddress,N'')<>COALESCE(@address,N'')
                DELETE dbo.IssueEmailQueue WHERE UserId=@user;
            """, connection, transaction);
        command.Parameters.AddRange([P("@user",user),P("@address",address),P("@send",sendWatchedIssues)]);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}

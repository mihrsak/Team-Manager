using Microsoft.Data.SqlClient;

namespace TeamManager.Web;

public sealed record AdministrationAuditEntry(DateTime At, string Actor, string Action, string Subject);

public sealed class AdministrationAuditStore(IConfiguration config)
{
    public async Task<List<AdministrationAuditEntry>> Recent()
    {
        await using var connection = new SqlConnection(config.GetConnectionString("TeamManager"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP (100) CreatedAt,ActorEmployeeNumber,Action,Subject
            FROM dbo.AdministrationAudit ORDER BY Id DESC
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new List<AdministrationAuditEntry>();
        while (await reader.ReadAsync())
            entries.Add(new(reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return entries;
    }
}

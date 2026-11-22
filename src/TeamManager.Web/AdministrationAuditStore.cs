using Microsoft.Data.SqlClient;

namespace TeamManager.Web;

public sealed record AdministrationAuditEntry(DateTime At, string Actor, string Action, string Subject, string Details);
public sealed record AuditChange(int ActorId, string Action, string Subject, string Details = "");

public sealed class AdministrationAuditStore(IConfiguration config,OracleWorkspace oracle)
{
    internal static async Task Write(SqlConnection connection, SqlTransaction transaction, AuditChange change)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.AdministrationAudit(ActorId,ActorEmployeeNumber,Action,Subject,Details)
            SELECT Id,EmployeeNumber,@action,@subject,@details FROM dbo.AppUsers WHERE Id=@actor
            """, connection, transaction);
        command.Parameters.AddRange([
            UserStore.P("@actor", change.ActorId), UserStore.P("@action", change.Action),
            UserStore.P("@subject", change.Subject), UserStore.P("@details", change.Details)]);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new UnauthorizedAccessException();
    }

    internal static async Task<int> Execute(SqlConnection connection, AuditChange? change, string sql, params SqlParameter[] parameters)
    {
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        var affected = await command.ExecuteNonQueryAsync();
        if (affected > 0 && change is not null) await Write(connection, transaction, change);
        await transaction.CommitAsync();
        return affected;
    }

    public async Task<List<AdministrationAuditEntry>> Recent(int offset=0,int take=100,string? query=null)
    {
        offset=Math.Clamp(offset,0,500000);take=Math.Clamp(take,1,101);
        if(oracle.Enabled)return await oracle.AuditPage(offset,take,query);
        await using var connection = new SqlConnection(config.GetConnectionString("TeamManager"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT CreatedAt,ActorEmployeeNumber,Action,Subject,Details
            FROM dbo.AdministrationAudit WHERE @query IS NULL OR CHARINDEX(@query,ActorEmployeeNumber)>0 OR CHARINDEX(@query,Action)>0 OR CHARINDEX(@query,Subject)>0 OR CHARINDEX(@query,Details)>0
            ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY
            """, connection);
        command.Parameters.AddRange([UserStore.P("@offset",offset),UserStore.P("@take",take),UserStore.P("@query",query)]);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new List<AdministrationAuditEntry>();
        while (await reader.ReadAsync())
            entries.Add(new(reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return entries;
    }
}

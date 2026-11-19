using Oracle.ManagedDataAccess.Client;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<bool> CreateApiToken(int user, int version, string name, byte[] hash)
        => WriteUser(user, async w =>
        {
            var account = await w.Find("AppUsers", user);
            if (account is null || !Flag(account, "IsActive") || Int(account, "SessionVersion") != version) return false;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Unesi naziv tokena do 80 znakova.");
            w.Add("ApiTokens", ("Id", await w.Next("ApiTokens")), ("UserId", user), ("Name", name), ("TokenHash", Convert.ToBase64String(hash)),
                ("SessionVersion", version), ("ExpiresAt", DateTime.UtcNow.AddDays(30)), ("CreatedAt", DateTime.UtcNow));
            return true;
        });

    internal Task<List<ApiTokenInfo>> ApiTokens(int user)
        => Query("ApiTokens", """
            SELECT t."Id",t."Name",t."ExpiresAt",CASE WHEN u."IsActive"=1 AND t."SessionVersion"=u."SessionVersion" AND t."ExpiresAt">SYS_EXTRACT_UTC(SYSTIMESTAMP) THEN 1 ELSE 0 END
            FROM "TMR_ApiTokens" t JOIN "TMR_AppUsers" u ON u."Id"=t."UserId" WHERE t."UserId"=:userId ORDER BY t."Id" DESC
            """, c => AddQueryParameter(c, "userId", user), r => new ApiTokenInfo(r.GetInt32(0), r.GetString(1), r.GetDateTime(2), r.GetInt32(3) != 0));

    internal Task<bool> RevokeApiToken(int user, int id) => WriteUser(user, async w =>
    {
        var token = await w.Find("ApiTokens", id);
        if (token is null || Int(token, "UserId") != user) return false;
        w.Table("ApiTokens").Remove(token);
        return true;
    });

    internal async Task<int?> AuthenticateApiToken(byte[] hash, CancellationToken cancellationToken)
    {
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = new OracleCommand("""
            SELECT t."UserId" FROM "TMR_ApiTokens" t JOIN "TMR_AppUsers" u ON u."Id"=t."UserId"
            WHERE t."TokenHash"=:hash AND u."IsActive"=1 AND t."SessionVersion"=u."SessionVersion" AND t."ExpiresAt">SYS_EXTRACT_UTC(SYSTIMESTAMP)
            """, connection) { BindByName = true, CommandTimeout = 30 };
        command.Parameters.Add("hash", OracleDbType.Raw).Value = hash;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }
}

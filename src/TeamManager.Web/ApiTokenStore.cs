using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record ApiTokenInfo(int Id, string Name, DateTime ExpiresAt, bool Active);

public sealed class ApiTokenStore(IConfiguration config)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));
    private static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    public async Task<string?> Create(int user, int version, string name)
    {
        var token = "tm_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.ApiTokens(UserId,Name,TokenHash,SessionVersion,ExpiresAt)
            SELECT u.Id,@name,@hash,u.SessionVersion,DATEADD(day,30,SYSUTCDATETIME())
            FROM dbo.AppUsers u WITH(HOLDLOCK)
            WHERE u.Id=@user AND u.IsActive=1 AND u.SessionVersion=@version
            """, connection);
        command.Parameters.AddRange([P("@user",user),P("@version",version),P("@name",name),P("@hash",Hash(token))]);
        return await command.ExecuteNonQueryAsync() == 1 ? token : null;
    }

    public async Task<List<ApiTokenInfo>> List(int user)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT t.Id,t.Name,t.ExpiresAt,CAST(CASE WHEN t.SessionVersion=u.SessionVersion AND
                t.ExpiresAt>SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit)
            FROM dbo.ApiTokens t JOIN dbo.AppUsers u ON u.Id=t.UserId
            WHERE t.UserId=@user ORDER BY t.Id DESC
            """, connection);
        command.Parameters.Add(P("@user",user));
        var tokens = new List<ApiTokenInfo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tokens.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetDateTime(2),reader.GetBoolean(3)));
        return tokens;
    }

    public async Task<bool> Revoke(int user, int id)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("DELETE dbo.ApiTokens WHERE UserId=@user AND Id=@id",connection);
        command.Parameters.AddRange([P("@user",user),P("@id",id)]);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<int?> Authenticate(string token, CancellationToken cancellationToken)
    {
        if (token.Length != 67 || !token.StartsWith("tm_",StringComparison.Ordinal) ||
            token[3..].Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return null;
        await using var connection = Connect();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT t.UserId FROM dbo.ApiTokens t JOIN dbo.AppUsers u ON u.Id=t.UserId
            WHERE t.TokenHash=@hash AND u.IsActive=1 AND t.SessionVersion=u.SessionVersion AND t.ExpiresAt>SYSUTCDATETIME()
            """,connection);
        command.Parameters.Add(P("@hash",Hash(token)));
        return await command.ExecuteScalarAsync(cancellationToken) as int?;
    }
}

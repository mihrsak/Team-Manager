using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record PendingRegistration(int Id, string EmployeeNumber, string DisplayName, DateTime At);

public sealed class RegistrationStore(IConfiguration config, IPasswordHasher<AppUser> passwords, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));

    public async Task Submit(string employee, string name, string password)
    {
        var request = new AppUser(0, employee, name, "", null, "", false);
        var hash = passwords.HashPassword(request, password);
        if (oracle.Enabled) { await oracle.SubmitRegistration(employee, name, hash); return; }
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.RegistrationRequests(EmployeeNumber,DisplayName,PasswordHash)
            SELECT @employee,@name,@hash
            WHERE NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE EmployeeNumber=@employee)
              AND NOT EXISTS(SELECT 1 FROM dbo.RegistrationRequests WITH(UPDLOCK,HOLDLOCK)
                             WHERE EmployeeNumber=@employee)
            """, connection);
        command.Parameters.AddRange([P("@employee", employee), P("@name", name), P("@hash", hash)]);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<PendingRegistration>> Pending(int actor)
    {
        if (oracle.Enabled) return await oracle.PendingRegistrations(actor);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id,EmployeeNumber,DisplayName,CreatedAt FROM dbo.RegistrationRequests ORDER BY Id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var pending = new List<PendingRegistration>();
        while (await reader.ReadAsync())
            pending.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
        return pending;
    }

    public async Task<bool> Decide(int actor, int id, bool approve)
    {
        if (oracle.Enabled) return await oracle.DecideRegistration(actor, id, approve);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand("""
            DECLARE @employee nvarchar(30),@name nvarchar(100),@hash nvarchar(500);
            SELECT @employee=EmployeeNumber,@name=DisplayName,@hash=PasswordHash
            FROM dbo.RegistrationRequests WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id;
            IF @employee IS NULL RETURN;
            IF @approve=1
                INSERT dbo.AppUsers(EmployeeNumber,DisplayName,PasswordHash,IsAdministrator)
                VALUES(@employee,@name,@hash,0);
            DELETE dbo.RegistrationRequests WHERE Id=@id;
            SELECT @employee;
            """, connection, transaction);
        command.Parameters.AddRange([P("@id", id), P("@approve", approve)]);
        if (await command.ExecuteScalarAsync() is not string employee) return false;
        await AdministrationAuditStore.Write(connection, transaction,
            new(actor, approve ? "registration.approve" : "registration.reject", employee));
        await transaction.CommitAsync();
        return true;
    }
}

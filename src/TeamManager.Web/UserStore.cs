using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;

namespace TeamManager.Web;

public record AppUser(int Id, string EmployeeNumber, string DisplayName, string JobTitle, int? OrganizationUnitId, string PasswordHash, bool IsAdministrator);
public record DirectoryEntry(AppUser User, string Unit);
public record Membership(int ScopeId, string Scope, string Role);

public sealed class UserStore(IConfiguration config, IPasswordHasher<AppUser> hasher)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));
    public static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    private static AppUser Read(SqlDataReader r) => new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetInt32(4),r.GetString(5),r.GetBoolean(6));
    public async Task<AppUser?> Find(string employee)
    {
        await using var c=Connect(); await c.OpenAsync();
        await using var cmd=new SqlCommand("SELECT Id,EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator FROM dbo.AppUsers WHERE EmployeeNumber=@employee",c);
        cmd.Parameters.Add(P("@employee",employee));
        await using var r=await cmd.ExecuteReaderAsync(); return await r.ReadAsync()?Read(r):null;
    }
    public async Task<AppUser?> Authenticate(string employee,string password)
    {
        // Serialize attempts per account so parallel requests cannot bypass the lockout counter.
        await using var c=Connect(); await c.OpenAsync();
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        await using var cmd=new SqlCommand("SELECT Id,EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator,LockoutUntil FROM dbo.AppUsers WITH(UPDLOCK,ROWLOCK) WHERE EmployeeNumber=@employee",c,tx);
        cmd.Parameters.Add(P("@employee",employee));
        AppUser? user=null; DateTime? until=null;
        await using(var r=await cmd.ExecuteReaderAsync()) { if(await r.ReadAsync()) {user=Read(r);until=r.IsDBNull(7)?null:r.GetDateTime(7);} }
        if(user is null || until>DateTime.UtcNow) return null;
        var valid=hasher.VerifyHashedPassword(user,user.PasswordHash,password)!=PasswordVerificationResult.Failed;
        cmd.Parameters.Clear();cmd.Parameters.Add(P("@id",user.Id));
        cmd.CommandText=valid?"UPDATE dbo.AppUsers SET FailedAttempts=0,LockoutUntil=NULL WHERE Id=@id":"UPDATE dbo.AppUsers SET LockoutUntil=CASE WHEN FailedAttempts>=4 THEN DATEADD(minute,5,SYSUTCDATETIME()) ELSE NULL END,FailedAttempts=CASE WHEN FailedAttempts>=4 THEN 0 ELSE FailedAttempts+1 END WHERE Id=@id";
        await cmd.ExecuteNonQueryAsync();await tx.CommitAsync();return valid?user:null;
    }
    public async Task Execute(string sql,params SqlParameter[] parameters)
    {
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand(sql,c);cmd.Parameters.AddRange(parameters);await cmd.ExecuteNonQueryAsync();
    }
    public async Task<List<DirectoryEntry>> Directory()
    {
        await using var c=Connect();await c.OpenAsync();
        await using var cmd=new SqlCommand("SELECT u.Id,u.EmployeeNumber,u.DisplayName,u.JobTitle,u.OrganizationUnitId,u.PasswordHash,u.IsAdministrator,COALESCE(o.Name,N'—') FROM dbo.AppUsers u LEFT JOIN dbo.OrganizationUnits o ON o.Id=u.OrganizationUnitId ORDER BY u.DisplayName",c);
        await using var r=await cmd.ExecuteReaderAsync();var result=new List<DirectoryEntry>();while(await r.ReadAsync())result.Add(new(Read(r),r.GetString(7)));return result;
    }
    public async Task<List<Team>> Choices(string kind)
    {
        var table=kind switch {"unit"=>"OrganizationUnits","team"=>"Teams","project"=>"Projects",_=>throw new ArgumentException("Unknown scope")};
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand($"SELECT Id,Name FROM dbo.{table} ORDER BY Name",c);
        await using var r=await cmd.ExecuteReaderAsync();var result=new List<Team>();while(await r.ReadAsync())result.Add(new(r.GetInt32(0),r.GetString(1)));return result;
    }
    public async Task<List<Membership>> Memberships(int id,bool team)
    {
        var sql=team?"SELECT t.Id,t.Name,m.Role FROM dbo.Members m JOIN dbo.Teams t ON t.Id=m.TeamId WHERE m.UserId=@id":"SELECT p.Id,p.Name,m.Role FROM dbo.ProjectMembers m JOIN dbo.Projects p ON p.Id=m.ProjectId WHERE m.UserId=@id";
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add(P("@id",id));
        await using var r=await cmd.ExecuteReaderAsync();var result=new List<Membership>();while(await r.ReadAsync())result.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2)));return result;
    }
    public async Task Create(string employee,string name,string job,int? unit,string password,bool admin=false)
    {
        var user=new AppUser(0,employee,name,job,unit,"",admin);
        await Execute("INSERT dbo.AppUsers(EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator) VALUES(@e,@n,@j,@o,@p,@a)",P("@e",employee),P("@n",name),P("@j",job),P("@o",unit),P("@p",hasher.HashPassword(user,password)),P("@a",admin));
    }
    public async Task Bootstrap()
    {
        await using var c=Connect();await c.OpenAsync();
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var check=new SqlCommand("SELECT COUNT(*) FROM dbo.AppUsers WITH(TABLOCKX,HOLDLOCK)",c,tx);
        if(Convert.ToInt32(await check.ExecuteScalarAsync())==0)
        {
            var user=new AppUser(0,"admin","Administrator","",null,"",true);
            await using var cmd=new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Name=N'Administrator')
                    INSERT dbo.Roles(Name) VALUES(N'Administrator');
                IF NOT EXISTS(SELECT 1 FROM dbo.RolePermissions WHERE RoleName=N'Administrator' AND Permission='*')
                    INSERT dbo.RolePermissions VALUES(N'Administrator','*');
                INSERT dbo.AppUsers(EmployeeNumber,DisplayName,PasswordHash,IsAdministrator)
                    VALUES('admin',N'Administrator',@hash,1);
                INSERT dbo.UserRoles(UserId,RoleName) VALUES(CONVERT(int,SCOPE_IDENTITY()),N'Administrator');
                """,c,tx);
            cmd.Parameters.Add(P("@hash",hasher.HashPassword(user,"admin")));
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
    public async Task ChangePassword(AppUser user,string password) => await Execute("UPDATE dbo.AppUsers SET PasswordHash=@p WHERE Id=@id",P("@p",hasher.HashPassword(user,password)),P("@id",user.Id));
}

using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public record AppUser(int Id, string EmployeeNumber, string DisplayName, string JobTitle, int? OrganizationUnitId, string PasswordHash, bool IsAdministrator, bool IsActive = true, int SessionVersion = 0);
public record UserMatch(int Id, string Name, string EmployeeNumber, string JobTitle);
public record SessionPreferences(string Theme, string Language);
public record DirectoryEntry(AppUser User, string Unit);
public record Membership(int ScopeId, string Scope, string Role, bool IsAdditional);

public sealed class UserStore(IConfiguration config, IPasswordHasher<AppUser> hasher, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));
    public static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    private static AppUser Read(SqlDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.GetString(5), r.GetBoolean(6), r.GetBoolean(7), r.GetInt32(8));
    public async Task<AppUser?> Find(string employee)
    {
        if (oracle.Enabled) return await oracle.Read(async w => (await w.Where("AppUsers", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).Select(OracleWorkspace.Account).FirstOrDefault());
        await using var c = Connect(); await c.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator,IsActive,SessionVersion FROM dbo.AppUsers WHERE EmployeeNumber=@employee", c);
        cmd.Parameters.Add(P("@employee", employee));
        await using var r = await cmd.ExecuteReaderAsync(); return await r.ReadAsync() ? Read(r) : null;
    }
    public async Task<AppUser?> Authenticate(string employee, string password)
    {
        if (oracle.Enabled) return await oracle.Authenticate(employee, password, hasher);
        // Serialize attempts per account so parallel requests cannot bypass the lockout counter.
        await using var c = Connect(); await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        await using var cmd = new SqlCommand("SELECT Id,EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator,IsActive,SessionVersion,LockoutUntil FROM dbo.AppUsers WITH(UPDLOCK,ROWLOCK) WHERE EmployeeNumber=@employee", c, tx);
        cmd.Parameters.Add(P("@employee", employee));
        AppUser? user = null; DateTime? until = null;
        await using (var r = await cmd.ExecuteReaderAsync()) { if (await r.ReadAsync()) { user = Read(r); until = r.IsDBNull(9) ? null : r.GetDateTime(9); } }
        if (user is null || !user.IsActive || until > DateTime.UtcNow) return null;
        var valid = hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
        cmd.Parameters.Clear(); cmd.Parameters.Add(P("@id", user.Id));
        cmd.CommandText = valid ? "UPDATE dbo.AppUsers SET FailedAttempts=0,LockoutUntil=NULL WHERE Id=@id" : "UPDATE dbo.AppUsers SET LockoutUntil=CASE WHEN FailedAttempts>=4 THEN DATEADD(minute,5,SYSUTCDATETIME()) ELSE NULL END,FailedAttempts=CASE WHEN FailedAttempts>=4 THEN 0 ELSE FailedAttempts+1 END WHERE Id=@id";
        await cmd.ExecuteNonQueryAsync(); await tx.CommitAsync(); return valid ? user : null;
    }
    public async Task Execute(string sql, params SqlParameter[] parameters)
    {
        await using var c = Connect(); await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); cmd.Parameters.AddRange(parameters); await cmd.ExecuteNonQueryAsync();
    }
    public async Task ExecuteAudited(AuditChange change, string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await AdministrationAuditStore.Execute(connection, change, sql, parameters);
    }

    public async Task<List<DirectoryEntry>> Directory()
    {
        if (oracle.Enabled) return await oracle.Read(async w =>
        {
            var result = new List<DirectoryEntry>();
            foreach (var row in (await w.Rows("AppUsers")).OrderBy(r => Text(r, "DisplayName")))
            {
                var unit = await w.Find("OrganizationUnits", OptionalInt(row, "OrganizationUnitId") ?? 0);
                result.Add(new DirectoryEntry(OracleWorkspace.Account(row), unit is null ? "—" : Text(unit, "Name")));
            }
            return result;
        });
        await using var c = Connect(); await c.OpenAsync();
        await using var cmd = new SqlCommand("SELECT u.Id,u.EmployeeNumber,u.DisplayName,u.JobTitle,u.OrganizationUnitId,u.PasswordHash,u.IsAdministrator,u.IsActive,u.SessionVersion,COALESCE(o.Name,N'—') FROM dbo.AppUsers u LEFT JOIN dbo.OrganizationUnits o ON o.Id=u.OrganizationUnitId ORDER BY u.DisplayName", c);
        await using var r = await cmd.ExecuteReaderAsync(); var result = new List<DirectoryEntry>(); while (await r.ReadAsync()) result.Add(new(Read(r), r.GetString(9))); return result;
    }
    public async Task<List<UserMatch>> Search(string query,int page=1,int take=20)
    {
        page=Math.Clamp(page,1,1000);take=Math.Clamp(take,1,21);
        if (oracle.Enabled) return await oracle.DirectorySearch(query,(page-1)*20,take);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id,DisplayName,EmployeeNumber,JobTitle
            FROM dbo.AppUsers
            WHERE CHARINDEX(@query,DisplayName)>0 OR CHARINDEX(@query,EmployeeNumber)>0 OR CHARINDEX(@query,JobTitle)>0
            ORDER BY DisplayName,EmployeeNumber OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY
            """, connection);
        command.Parameters.Add(P("@query", query));
        command.Parameters.AddRange([P("@offset",(page-1)*20),P("@take",take)]);
        var matches = new List<UserMatch>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            matches.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return matches;
    }

    public async Task<List<Team>> Choices(string kind)
    {
        var table = kind switch { "unit" => "OrganizationUnits", "team" => "Teams", "project" => "Projects", _ => throw new ArgumentException("Unknown scope") };
        if (oracle.Enabled) return await oracle.Read(async w => (await w.Rows(table)).OrderBy(r => Text(r, "Name")).Select(r => new Team(Id(r), Text(r, "Name"))).ToList());
        await using var c = Connect(); await c.OpenAsync(); await using var cmd = new SqlCommand($"SELECT Id,Name FROM dbo.{table} ORDER BY Name", c);
        await using var r = await cmd.ExecuteReaderAsync(); var result = new List<Team>(); while (await r.ReadAsync()) result.Add(new(r.GetInt32(0), r.GetString(1))); return result;
    }
    public async Task<List<Team>> MembershipProjects(int viewer)
    {
        if (oracle.Enabled) return await oracle.MembershipProjects(viewer);
        await using var connection=Connect(); await connection.OpenAsync();
        await using var command=new SqlCommand("""
            SELECT p.Id,p.Name FROM dbo.Projects p
            WHERE EXISTS(SELECT 1 FROM dbo.AppUsers WHERE Id=@viewer AND IsActive=1)
              AND (EXISTS(SELECT 1 FROM dbo.EffectiveUserRoles ur JOIN dbo.RolePermissions rp ON rp.RoleName=ur.RoleName
                          WHERE ur.UserId=@viewer AND rp.Permission IN ('*','members.manage'))
                OR EXISTS(SELECT 1 FROM dbo.EffectiveProjectRoles(p.Id,@viewer) pr JOIN dbo.RolePermissions rp ON rp.RoleName=pr.RoleName
                          WHERE rp.Permission IN ('*','members.manage')))
            ORDER BY p.Name OPTION(MAXRECURSION 0)
            """,connection);
        command.Parameters.Add(P("@viewer",viewer));
        await using var reader=await command.ExecuteReaderAsync(); var result=new List<Team>();
        while(await reader.ReadAsync()) result.Add(new(reader.GetInt32(0),reader.GetString(1)));
        return result;
    }
    public async Task<List<Membership>> Memberships(int viewer, int id, bool team)
    {
        if (oracle.Enabled) return await oracle.Memberships(viewer, id, team);
        var sql = team
            ? """
              SELECT t.Id,t.Name,r.RoleName,r.IsAdditional
              FROM dbo.Members m JOIN dbo.Teams t ON t.Id=m.TeamId
              CROSS APPLY (
                  SELECT m.Role,0 UNION ALL
                  SELECT a.RoleName,1 FROM dbo.TeamMemberRoles a WHERE a.MemberId=m.Id
              ) r(RoleName,IsAdditional)
              WHERE m.UserId=@id ORDER BY t.Name,r.IsAdditional,r.RoleName
              """
            : """
              SELECT p.Id,p.Name,r.RoleName,r.IsAdditional
              FROM dbo.ProjectMembers m JOIN dbo.Projects p ON p.Id=m.ProjectId
              CROSS APPLY (
                  SELECT m.Role,0 UNION ALL
                  SELECT a.RoleName,1 FROM dbo.ProjectMemberRoles a WHERE a.ProjectId=m.ProjectId AND a.UserId=m.UserId
              ) r(RoleName,IsAdditional)
              WHERE m.UserId=@id AND EXISTS(SELECT 1 FROM dbo.AppUsers WHERE Id=@viewer AND IsActive=1)
                AND (@viewer=@id OR p.IsPublic=1
                  OR EXISTS(SELECT 1 FROM dbo.EffectiveUserRoles ur JOIN dbo.RolePermissions rp ON rp.RoleName=ur.RoleName
                            WHERE ur.UserId=@viewer AND rp.Permission IN ('*','users.manage','issues.view','projects.manage'))
                  OR EXISTS(SELECT 1 FROM dbo.EffectiveProjectRoles(p.Id,@viewer) pr JOIN dbo.RolePermissions rp ON rp.RoleName=pr.RoleName
                            WHERE rp.Permission IN ('*','issues.view','projects.manage')))
              ORDER BY p.Name,r.IsAdditional,r.RoleName OPTION(MAXRECURSION 0)
              """;
        await using var c = Connect(); await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); cmd.Parameters.Add(P("@id", id)); cmd.Parameters.Add(P("@viewer", viewer));
        await using var r = await cmd.ExecuteReaderAsync(); var result = new List<Membership>(); while (await r.ReadAsync()) result.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1)); return result;
    }
    public async Task Create(string employee, string name, string job, int? unit, string password, bool admin = false, int? actorId = null)
    {
        if (oracle.Enabled)
        {
            await oracle.WriteNamedAdministration(actorId ?? throw new UnauthorizedAccessException(), "account-name:" + employee.Trim().ToUpperInvariant(), async w =>
            {
                if (actorId is not int actor || !(await OracleWorkspace.Right(w, actor, "users.manage"))) throw new UnauthorizedAccessException();
                if ((await w.Where("AppUsers", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).Any()) throw new ArgumentException("Broj radnika već postoji.");
                if (unit is not null && (await w.Find("OrganizationUnits", unit.Value)) is null) throw new ArgumentException("Organizacijska jedinica nije valjana.");
                var newUser = new AppUser((await w.Next("AppUsers")), employee, name, job, unit, "", admin);
                w.Add("AppUsers", ("Id", newUser.Id), ("EmployeeNumber", employee), ("DisplayName", name), ("JobTitle", job), ("OrganizationUnitId", unit),
                    ("PasswordHash", hasher.HashPassword(newUser, password)), ("IsAdministrator", admin), ("IsActive", true),
                    ("SessionVersion", 0), ("FailedAttempts", 0), ("LockoutUntil", null), ("CreatedAt", DateTime.UtcNow), ("DisplayTheme", "light"), ("UiLanguage", "hr"));
                await OracleWorkspace.Audit(w, actor, "account.create", employee); return true;
            }); return;
        }
        var user = new AppUser(0, employee, name, job, unit, "", admin);
        await using var connection = Connect();
        await AdministrationAuditStore.Execute(connection, actorId is int actor ? new(actor, "account.create", employee) : null, "INSERT dbo.AppUsers(EmployeeNumber,DisplayName,JobTitle,OrganizationUnitId,PasswordHash,IsAdministrator) VALUES(@e,@n,@j,@o,@p,@a)", P("@e", employee), P("@n", name), P("@j", job), P("@o", unit), P("@p", hasher.HashPassword(user, password)), P("@a", admin));
    }
    public async Task Bootstrap()
    {
        if (oracle.Enabled) { await oracle.ReadySql(); return; }
        await using var c = Connect(); await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.AppUsers WITH(TABLOCKX,HOLDLOCK)", c, tx);
        if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
        {
            var password = config["Bootstrap:Password"];
            if (config.GetValue<bool>("Bootstrap:RequirePassword") && (password is null || password.Length < 12))
                throw new InvalidOperationException("Bootstrap requires an administrator password of at least 12 characters for an empty database.");
            var user = new AppUser(0, "admin", "Administrator", "", null, "", true);
            await using var cmd = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Name=N'Administrator')
                    INSERT dbo.Roles(Name) VALUES(N'Administrator');
                IF NOT EXISTS(SELECT 1 FROM dbo.RolePermissions WHERE RoleName=N'Administrator' AND Permission='*')
                    INSERT dbo.RolePermissions VALUES(N'Administrator','*');
                INSERT dbo.AppUsers(EmployeeNumber,DisplayName,PasswordHash,IsAdministrator)
                    VALUES('admin',N'Administrator',@hash,1);
                INSERT dbo.UserRoles(UserId,RoleName) VALUES(CONVERT(int,SCOPE_IDENTITY()),N'Administrator');
                """, c, tx);
            cmd.Parameters.Add(P("@hash", hasher.HashPassword(user, password ?? "admin")));
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
    public async Task<bool> ChangePassword(AppUser user, string password)
    {
        if (oracle.Enabled) return await oracle.WriteAccount(user.Id, user.Id, async w =>
        {
            var r = (await w.Find("AppUsers", user.Id));
            if (r is null || !Flag(r, "IsActive") || Int(r, "SessionVersion") != user.SessionVersion) return false;
            Set(r, "PasswordHash", hasher.HashPassword(user, password)); Set(r, "SessionVersion", Int(r, "SessionVersion") + 1); return true;
        });
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.AppUsers SET PasswordHash=@hash, SessionVersion=SessionVersion+1
            WHERE Id=@id AND IsActive=1 AND SessionVersion=@version
            """, connection);
        command.Parameters.Add(P("@hash", hasher.HashPassword(user, password)));
        command.Parameters.Add(P("@id", user.Id));
        command.Parameters.Add(P("@version", user.SessionVersion));
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public const string LanguageClaim = "ui_language";
    public const string ThemeClaim = "display_theme";

    public async Task SetTheme(int userId, string theme)
    {
        if (oracle.Enabled) { await oracle.WriteUser(userId, async w => { var r = (await w.Find("AppUsers", userId)) ?? throw new UnauthorizedAccessException(); Set(r, "DisplayTheme", theme); return true; }); return; }
        await Execute("UPDATE dbo.AppUsers SET DisplayTheme=@theme WHERE Id=@id AND IsActive=1",
            P("@theme", theme), P("@id", userId));
    }

    public async Task SetLanguage(int userId, string language)
    {
        if (oracle.Enabled) { await oracle.WriteUser(userId, async w => { var r = (await w.Find("AppUsers", userId)) ?? throw new UnauthorizedAccessException(); Set(r, "UiLanguage", language); return true; }); return; }
        await Execute("UPDATE dbo.AppUsers SET UiLanguage=@language WHERE Id=@id AND IsActive=1",
            P("@language", language), P("@id", userId));
    }

    public async Task<SessionPreferences?> SessionSettings(int userId, int version)
    {
        if (oracle.Enabled) return await oracle.Read(async w => (await w.Find("AppUsers", userId)) is { } r && Flag(r, "IsActive") && Int(r, "SessionVersion") == version ?
            new SessionPreferences(Text(r, "DisplayTheme"), Text(r, "UiLanguage")) : null);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT DisplayTheme,UiLanguage FROM dbo.AppUsers WHERE Id=@id AND IsActive=1 AND SessionVersion=@version", connection);
        command.Parameters.Add(P("@id", userId));
        command.Parameters.Add(P("@version", version));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.GetString(0), reader.GetString(1)) : null;
    }
}

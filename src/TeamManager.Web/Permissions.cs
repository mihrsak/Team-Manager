using System.Security.Claims;
using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;

public record AccessRole(string Name,string Description,List<string> Permissions);
public sealed class Permissions(IConfiguration config,OracleWorkspace oracle)
{
    public static readonly Dictionary<string,string> Catalog=new(){["*"]="Plna administracija",["users.manage"]="Kreiranje i uređivanje korisnika",["organization.manage"]="Kreiranje jedinica, timova i projekata",["roles.manage"]="Uređivanje rola i dodjela globalnih ovlasti",["members.manage"]="Upravljanje članovima timova i projekata"};
    private SqlConnection Connect()=>new(config.GetConnectionString("TeamManager"));
    static Permissions()
    {
        Catalog["projects.manage"]="Uređivanje postavki projekta";
        Catalog["issues.view"]="Pregled projektnih zadataka";
        Catalog["issues.create"]="Kreiranje projektnih zadataka";
        Catalog["issues.edit"]="Uređivanje projektnih zadataka";
        Catalog["issues.comment"]="Komentiranje projektnih zadataka";
        Catalog["time.log"]="Evidentiranje vremena na zadacima";
        Catalog["repositories.view"]="Pregled Git commitova i razlika";
        Catalog["wiki.edit"]="Uređivanje projektnog wikija";
        Catalog["forum.post"]="Objavljivanje tema i odgovora na forumu";
        Catalog["forum.moderate"]="Moderiranje projektnog foruma";
        Catalog["news.comment"]="Komentiranje projektnih novosti";
        Catalog["workflow.manage"]="Uređivanje šifrarnika i tijeka rada";
    }
    public async Task<List<AccessRole>> Roles()
    {
        if (oracle.Enabled) return await oracle.AccessRoles();
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT r.Name,r.Description,p.Permission FROM dbo.Roles r LEFT JOIN dbo.RolePermissions p ON p.RoleName=r.Name ORDER BY r.Name",c);
        await using var reader=await cmd.ExecuteReaderAsync();var roles=new Dictionary<string,AccessRole>();
        while(await reader.ReadAsync()){var name=reader.GetString(0);if(!roles.ContainsKey(name))roles[name]=new(name,reader.GetString(1),[]);if(!reader.IsDBNull(2))roles[name].Permissions.Add(reader.GetString(2));}return roles.Values.ToList();
    }
    public async Task<List<string>> GlobalRoles(int userId)
    {
        if (oracle.Enabled) return await oracle.Read(w => w.By("UserRoles","UserId",userId)
            .Select(r => OracleWorkspace.Workspace.Text(r,"RoleName")).ToList());
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand("SELECT RoleName FROM dbo.UserRoles WHERE UserId=@id",c);cmd.Parameters.Add(P("@id",userId));await using var r=await cmd.ExecuteReaderAsync();var result=new List<string>();while(await r.ReadAsync())result.Add(r.GetString(0));return result;
    }
    public async Task<List<string>> Effective(int userId,string? kind=null,int? scope=null)
    {
        if (oracle.Enabled) return await oracle.Effective(userId,kind,scope);
        await using var c=Connect();await c.OpenAsync();var sql="SELECT DISTINCT p.Permission FROM dbo.RolePermissions p JOIN dbo.EffectiveUserRoles u ON u.RoleName=p.RoleName WHERE u.UserId=@id";
        if(kind=="team")sql+=" UNION SELECT p.Permission FROM dbo.RolePermissions p JOIN dbo.Members m ON m.Role=p.RoleName WHERE m.UserId=@id AND m.TeamId=@scope UNION SELECT p.Permission FROM dbo.RolePermissions p JOIN dbo.TeamMemberRoles r ON r.RoleName=p.RoleName JOIN dbo.Members m ON m.Id=r.MemberId WHERE m.UserId=@id AND m.TeamId=@scope";
        if(kind=="project")sql+=" UNION SELECT p.Permission FROM dbo.RolePermissions p JOIN dbo.EffectiveProjectRoles(@scope,@id) r ON r.RoleName=p.RoleName OPTION (MAXRECURSION 0)";
        await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add(P("@id",userId));cmd.Parameters.Add(P("@scope",scope));await using var r=await cmd.ExecuteReaderAsync();var result=new List<string>();while(await r.ReadAsync())result.Add(r.GetString(0));return result;
    }
    public async Task<bool> CanManageMembers(ClaimsPrincipal user,string kind,int scope)
    {
        var rights=await Effective(int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),kind,scope);return rights.Contains("*")||rights.Contains("members.manage");
    }
    // Every permission edit and global assignment is serialized and must preserve a full administrator.
    private async Task Mutate(Func<SqlConnection,SqlTransaction,Task> action)
    {
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        await using var mutex=new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource='TeamManager.Roles',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51000,'Role changes are busy',1;",c,tx);await mutex.ExecuteNonQueryAsync();
        await action(c,tx);
        await using var guard=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles u JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName JOIN dbo.AppUsers a ON a.Id=u.UserId WHERE p.Permission='*' AND a.IsActive=1) THROW 51001,'At least one full administrator is required',1;",c,tx);await guard.ExecuteNonQueryAsync();await tx.CommitAsync();
    }
    public Task ManageAccount(int actorId, int targetId, bool? active, string? passwordHash)
        => oracle.Enabled ? oracle.ManageAccount(actorId,targetId,active,passwordHash) : Mutate(async (connection, transaction) =>
        {
            const string sql = """
                IF @actor=@target OR NOT EXISTS(
                    SELECT 1 FROM dbo.EffectiveUserRoles u
                    JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName
                    JOIN dbo.AppUsers a ON a.Id=u.UserId
                    WHERE a.Id=@actor AND a.IsActive=1 AND p.Permission='*')
                    THROW 51002,'Full administration is required',1;
                UPDATE dbo.AppUsers
                SET IsActive=COALESCE(@active,IsActive),
                    PasswordHash=COALESCE(@hash,PasswordHash),
                    FailedAttempts=CASE WHEN @hash IS NULL THEN FailedAttempts ELSE 0 END,
                    LockoutUntil=CASE WHEN @hash IS NULL THEN LockoutUntil ELSE NULL END,
                    SessionVersion=SessionVersion+1
                WHERE Id=@target;
                IF @@ROWCOUNT=1
                    INSERT dbo.AdministrationAudit(ActorId,ActorEmployeeNumber,Action,Subject)
                    SELECT actor.Id,actor.EmployeeNumber,
                        CASE WHEN @hash IS NOT NULL THEN 'account.reset'
                             WHEN @active=1 THEN 'account.activate'
                             WHEN @active=0 THEN 'account.deactivate'
                             ELSE 'account.revoke' END,
                        target.EmployeeNumber
                    FROM dbo.AppUsers actor CROSS JOIN dbo.AppUsers target
                    WHERE actor.Id=@actor AND target.Id=@target;
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(P("@actor", actorId));
            command.Parameters.Add(P("@target", targetId));
            command.Parameters.Add(new SqlParameter("@active", System.Data.SqlDbType.Bit) { Value = (object?)active ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@hash", System.Data.SqlDbType.NVarChar, 500) { Value = (object?)passwordHash ?? DBNull.Value });
            await command.ExecuteNonQueryAsync();
        });

    public async Task Save(int actorId,string name,string description,string[] rights,bool create)
    {
        if(oracle.Enabled){await oracle.SaveRole(actorId,name,description,rights,create);return;}
        await Mutate(async(c,tx)=>{
            await using var cmd=new SqlCommand(create?"INSERT dbo.Roles(Name,Description) VALUES(@name,@description)":"UPDATE dbo.Roles SET Description=@description WHERE Name=@name; DELETE dbo.RolePermissions WHERE RoleName=@name",c,tx);cmd.Parameters.Add(P("@name",name));cmd.Parameters.Add(P("@description",description));await cmd.ExecuteNonQueryAsync();
            foreach(var right in rights.Distinct()){await using var add=new SqlCommand("INSERT dbo.RolePermissions VALUES(@name,@right)",c,tx);add.Parameters.Add(P("@name",name));add.Parameters.Add(P("@right",right));await add.ExecuteNonQueryAsync();}
            await AdministrationAuditStore.Write(c,tx,new(actorId,create?"role.create":"role.update",name,string.Join(", ",rights.Distinct().Order())));
        });
    }
    public async Task Assign(int actorId,int userId,string role,bool remove)
    {
        if(oracle.Enabled){await oracle.AssignRole(actorId,userId,role,remove);return;}
        await Mutate(async(c,tx)=>{
        await using var cmd=new SqlCommand(remove?"DELETE dbo.UserRoles WHERE UserId=@id AND RoleName=@role":"IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@id AND RoleName=@role) INSERT dbo.UserRoles VALUES(@id,@role)",c,tx);cmd.Parameters.Add(P("@id",userId));cmd.Parameters.Add(P("@role",role));
        if(await cmd.ExecuteNonQueryAsync()>0)
            await AdministrationAuditStore.Write(c,tx,new(actorId,remove?"role.revoke":"role.assign",$"#{userId}",role));
        });
    }
}

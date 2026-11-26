using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record GroupAssignment(int GroupId, int? UserId, string? RoleName);
public sealed record GroupDirectory(List<Team> Groups, List<GroupAssignment> Assignments);

public sealed class GroupStore(IConfiguration configuration, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<GroupDirectory> List(int actor)
    {
        if (oracle.Enabled) return await oracle.Groups(actor);
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id,Name FROM dbo.UserGroups ORDER BY Name;
            SELECT GroupId,UserId,NULL FROM dbo.UserGroupMembers
            UNION ALL
            SELECT GroupId,NULL,RoleName FROM dbo.UserGroupRoles;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var groups = new List<Team>();
        while (await reader.ReadAsync())
            groups.Add(new(reader.GetInt32(0), reader.GetString(1)));

        var assignments = new List<GroupAssignment>();
        await reader.NextResultAsync();
        while (await reader.ReadAsync())
            assignments.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        return new(groups, assignments);
    }

    public Task Create(int actor, string name)
        => oracle.Enabled ? oracle.CreateGroup(actor, name) : Execute(new(actor, "group.create", name),
            "SELECT Id FROM dbo.UserGroups WITH(UPDLOCK,HOLDLOCK) WHERE Name=@name",
            "INSERT dbo.UserGroups(Name) VALUES(@name)", P("@name", name));

    public Task SetMember(int actor, int groupId, int userId, bool remove)
        => oracle.Enabled ? oracle.ChangeGroupMember(actor, groupId, userId, remove) : Execute(new(actor, remove ? "group.member.remove" : "group.member.add", $"#{groupId}", $"Korisnik #{userId}"),
            "SELECT UserId FROM dbo.UserGroupMembers WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND UserId=@user", remove
            ? "DELETE dbo.UserGroupMembers WHERE GroupId=@group AND UserId=@user"
            : """
              INSERT dbo.UserGroupMembers(GroupId,UserId)
              SELECT @group,@user WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.UserGroupMembers WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND UserId=@user)
              """, P("@group", groupId), P("@user", userId));

    public Task SetRole(int actor, int groupId, string role, bool remove)
        => oracle.Enabled ? oracle.ChangeGroupRole(actor, groupId, role, remove) : Execute(new(actor, remove ? "group.role.remove" : "group.role.add", $"#{groupId}", role),
            "SELECT RoleName FROM dbo.UserGroupRoles WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND RoleName=@role", remove
            ? "DELETE dbo.UserGroupRoles WHERE GroupId=@group AND RoleName=@role"
            : """
              INSERT dbo.UserGroupRoles(GroupId,RoleName)
              SELECT @group,@role WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.UserGroupRoles WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND RoleName=@role)
              """, P("@group", groupId), P("@role", role));

    private async Task Execute(AuditChange change, string targetLock, string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Read the actor only after the target lock is acquired. A request may have
        // waited here while its account or global administration was revoked.
        await using (var target = new SqlCommand(targetLock, connection, transaction))
        {
            target.Parameters.AddRange(parameters.Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray());
            await target.ExecuteNonQueryAsync();
        }
        await using (var guard = new SqlCommand("""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM dbo.AppUsers a WITH(HOLDLOCK)
                JOIN dbo.EffectiveUserRoles r ON r.UserId=a.Id
                JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName
                WHERE a.Id=@actor AND a.IsActive=1 AND p.Permission='*'
            ) THEN 1 ELSE 0 END
            """, connection, transaction))
        {
            guard.Parameters.Add(P("@actor", change.ActorId));
            if (Convert.ToInt32(await guard.ExecuteScalarAsync()) != 1) throw new UnauthorizedAccessException();
        }
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        var affected = await command.ExecuteNonQueryAsync();
        if (affected > 0) await AdministrationAuditStore.Write(connection, transaction, change);
        // Authorized self-removal is valid: do not re-check rights after the change.
        await transaction.CommitAsync();
    }
}

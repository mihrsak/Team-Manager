using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record GroupAssignment(int GroupId, int? UserId, string? RoleName);
public sealed record GroupDirectory(List<Team> Groups, List<GroupAssignment> Assignments);

public sealed class GroupStore(IConfiguration configuration)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<GroupDirectory> List()
    {
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
        => Execute(new(actor,"group.create",name),"INSERT dbo.UserGroups(Name) VALUES(@name)", P("@name", name));

    public Task SetMember(int actor,int groupId, int userId, bool remove)
        => Execute(new(actor,remove?"group.member.remove":"group.member.add",$"#{groupId}",$"Korisnik #{userId}"),remove
            ? "DELETE dbo.UserGroupMembers WHERE GroupId=@group AND UserId=@user"
            : """
              INSERT dbo.UserGroupMembers(GroupId,UserId)
              SELECT @group,@user WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.UserGroupMembers WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND UserId=@user)
              """, P("@group", groupId), P("@user", userId));

    public Task SetRole(int actor,int groupId, string role, bool remove)
        => Execute(new(actor,remove?"group.role.remove":"group.role.add",$"#{groupId}",role),remove
            ? "DELETE dbo.UserGroupRoles WHERE GroupId=@group AND RoleName=@role"
            : """
              INSERT dbo.UserGroupRoles(GroupId,RoleName)
              SELECT @group,@role WHERE NOT EXISTS (
                  SELECT 1 FROM dbo.UserGroupRoles WITH(UPDLOCK,HOLDLOCK) WHERE GroupId=@group AND RoleName=@role)
              """, P("@group", groupId), P("@role", role));

    private async Task Execute(AuditChange change, string sql, params SqlParameter[] parameters)
    {
        await using var connection = Connect();
        await AdministrationAuditStore.Execute(connection, change, sql, parameters);
    }
}

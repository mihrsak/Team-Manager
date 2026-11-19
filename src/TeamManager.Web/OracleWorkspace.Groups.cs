using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using Oracle.ManagedDataAccess.Client;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private static async Task<HashSet<(int User, int Group)>> MembershipGroups(OracleConnection connection, OracleTransaction transaction, int[] users)
    {
        var result = new HashSet<(int, int)>();
        if (users.Length == 0) return result;
        await using var command = RelationalCommand(connection, transaction, """
            SELECT "UserId","GroupId" FROM "TMR_UserGroupMembers"
            WHERE "UserId" IN (SELECT id FROM JSON_TABLE(:users,'$[*]' COLUMNS(id NUMBER PATH '$')))
            """);
        JsonIds(command, "users", users);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return result;
    }

    internal Task<GroupDirectory> Groups(int actor) => Read(async w =>
    {
        if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
        var groups = (await w.Rows("UserGroups")).OrderBy(r => Text(r, "Name")).Select(r => new Team(Id(r), Text(r, "Name"))).ToList();
        var assignments = (await w.Rows("UserGroupMembers")).Select(r => new GroupAssignment(Int(r, "GroupId"), Int(r, "UserId"), null)).ToList();
        assignments.AddRange((await w.Rows("UserGroupRoles")).Select(r => new GroupAssignment(Int(r, "GroupId"), null, Text(r, "RoleName"))));
        return new GroupDirectory(groups, assignments);
    });

    internal Task CreateGroup(int actor, string name) => WriteNamedAdministration(actor, "group-name:" + name.Trim().ToUpperInvariant(), async w =>
    {
        if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || (await w.Where("UserGroups", "UPPER(t.\"Name\")=UPPER(:name)", ("name", name))).Any())
            throw new ArgumentException("Naziv već postoji ili odabrana grupa, korisnik ili uloga više nisu dostupni.");
        w.Add("UserGroups", ("Id", await w.Next("UserGroups")), ("Name", name));
        await Audit(w, actor, "group.create", name);
        return true;
    });

    internal Task ChangeGroupMember(int actor, int group, int user, bool remove) => Run(async w =>
    {
        if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
        if (await w.Find("UserGroups", group) is null || await w.Find("AppUsers", user) is null)
            throw new ArgumentException("Naziv već postoji ili odabrana grupa, korisnik ili uloga više nisu dostupni.");
        var row = (await w.By("UserGroupMembers", "GroupId", group)).FirstOrDefault(r => Int(r, "UserId") == user);
        if (remove && row is null || !remove && row is not null) return false;
        if (remove) { w.Table("UserGroupMembers").Remove(row); GuardAdmin(w); }
        else w.Add("UserGroupMembers", ("GroupId", group), ("UserId", user));
        await Audit(w, actor, remove ? "group.member.remove" : "group.member.add", $"#{group}", $"Korisnik #{user}");
        return true;
    }, true, new WriteScope(actor, [], [actor], [user], []) { GroupWriters = [group] });

    internal Task ChangeGroupRole(int actor, int group, string role, bool remove) => Run(async w =>
    {
        if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
        if (await w.Find("UserGroups", group) is null || !(await w.By("Roles", "Name", role)).Any())
            throw new ArgumentException("Naziv već postoji ili odabrana grupa, korisnik ili uloga više nisu dostupni.");
        var row = (await w.By("UserGroupRoles", "GroupId", group)).FirstOrDefault(r => Text(r, "RoleName") == role);
        if (remove && row is null || !remove && row is not null) return false;
        if (remove) { w.Table("UserGroupRoles").Remove(row); GuardAdmin(w); }
        else w.Add("UserGroupRoles", ("GroupId", group), ("RoleName", role));
        await Audit(w, actor, remove ? "group.role.remove" : "group.role.add", $"#{group}", role);
        return true;
    }, true, new WriteScope(actor, [], [actor], [], []) { GroupWriters = [group], CatalogReaders = ["role:" + role] });
}

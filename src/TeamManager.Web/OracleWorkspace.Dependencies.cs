using System.Text.Json;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal static string[] IssueCatalogKeys(IssueInput input) =>
        ["value:IssueTypes:" + input.Type, "value:IssueStatuses:" + input.Status, "value:IssuePriorities:" + input.Priority, "workflow-type:" + input.Type];
    private Task<T> WriteIssueChanges<T>(int actor, IEnumerable<int> ids, Func<Workspace, T> action, int? project = null, IssueInput? input = null, string[]? catalogs = null)
        => Run(action, true, new WriteScope(actor, ids.Distinct().Order().ToArray(), input?.AssigneeId is int assigned ? [actor, assigned] : [actor], [], [])
        { Projects = project is int selected ? [selected] : [], IssueCatalogs = true, CatalogReaders = [.. input is null ? [] : IssueCatalogKeys(input), .. catalogs ?? []] });
    internal Task<T> WriteCatalog<T>(int actor, string[] readers, string[] writers, Func<Workspace, T> action)
        => Run(action, true, new WriteScope(actor, [], [actor], [], []) { CatalogReaders = readers, CatalogWriters = writers });
    internal Task<T> WriteProjectMetadata<T>(int actor, int? project, int? parent, string? name, Func<Workspace, T> action)
        => Run(action, true, new WriteScope(actor, [], [actor], [], name is null ? [] : ["project-name:" + name.Trim().ToUpperInvariant()])
        { NewProject = project is null, CatalogReaders = project is null ? ["role:Voditelj"] : [], Projects = new[] { project, parent }.OfType<int>().Distinct().ToArray(), ProjectWriters = project is int id ? [id] : [] });

    private static async Task<Dictionary<int, int?>> ProjectPaths(OracleConnection connection, OracleTransaction transaction, IEnumerable<int> roots)
    {
        var paths = new Dictionary<int, int?>();
        var pending = new Stack<int>(roots);
        while (pending.TryPop(out var id))
        {
            if (paths.ContainsKey(id)) continue;
            await using var command = RelationalCommand(connection, transaction, "SELECT \"ParentId\" FROM \"TMR_Projects\" WHERE \"Id\"=:id");
            command.Parameters.Add("id", OracleDbType.Int32).Value = id;
            var parent = await command.ExecuteScalarAsync();
            paths[id] = parent is null or DBNull ? null : Convert.ToInt32(parent);
            if (paths[id] is int ancestor) pending.Push(ancestor);
        }
        return paths;
    }
    private static void JsonIds(OracleCommand command, string name, IEnumerable<int> ids)
        => command.Parameters.Add(name, OracleDbType.Clob).Value = JsonSerializer.Serialize(ids.Distinct().ToArray());
    private static async Task<HashSet<string>> DependencyRoles(OracleConnection connection, OracleTransaction transaction, IEnumerable<int> users, IEnumerable<int> projects)
    {
        await using var command = RelationalCommand(connection, transaction, """
            WITH selected_users AS (SELECT id FROM JSON_TABLE(:users,'$[*]' COLUMNS(id NUMBER PATH '$'))),
                 selected_projects AS (SELECT id FROM JSON_TABLE(:projects,'$[*]' COLUMNS(id NUMBER PATH '$')))
            SELECT "RoleName" FROM "TMR_UserRoles" WHERE "UserId" IN (SELECT id FROM selected_users)
            UNION SELECT r."RoleName" FROM "TMR_UserGroupRoles" r JOIN "TMR_UserGroupMembers" m ON m."GroupId"=r."GroupId" WHERE m."UserId" IN (SELECT id FROM selected_users)
            UNION SELECT "Role" FROM "TMR_ProjectMembers" WHERE "UserId" IN (SELECT id FROM selected_users) AND "ProjectId" IN (SELECT id FROM selected_projects)
            UNION SELECT "RoleName" FROM "TMR_ProjectMemberRoles" WHERE "UserId" IN (SELECT id FROM selected_users) AND "ProjectId" IN (SELECT id FROM selected_projects)
            UNION SELECT "Role" FROM "TMR_Members" WHERE "UserId" IN (SELECT id FROM selected_users)
            UNION SELECT r."RoleName" FROM "TMR_TeamMemberRoles" r JOIN "TMR_Members" m ON m."Id"=r."MemberId" WHERE m."UserId" IN (SELECT id FROM selected_users)
            """);
        JsonIds(command, "users", users); JsonIds(command, "projects", projects);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        await using var rows = await command.ExecuteReaderAsync();
        while (await rows.ReadAsync()) roles.Add(rows.GetString(0));
        return roles;
    }
    private sealed class OracleScopeChangedException : Exception;
}

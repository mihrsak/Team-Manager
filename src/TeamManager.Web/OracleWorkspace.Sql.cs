using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    // The hierarchy is evaluated in Oracle; only permission strings or permitted rows
    // cross the connection. A single-project lookup starts at that project's ID.
    private const string SecuritySql = """
        WITH global_roles AS (
            SELECT "RoleName" FROM "TMR_UserRoles" WHERE "UserId"=:viewer
            UNION SELECT r."RoleName" FROM "TMR_UserGroupRoles" r JOIN "TMR_UserGroupMembers" m ON m."GroupId"=r."GroupId" WHERE m."UserId"=:viewer
        ), global_rights AS (
            SELECT DISTINCT p."Permission" FROM "TMR_RolePermissions" p JOIN global_roles r ON r."RoleName"=p."RoleName"
        ), ancestry AS (
            SELECT CONNECT_BY_ROOT p."Id" project_id,p."Id" ancestor_id FROM "TMR_Projects" p
            START WITH (:scope IS NULL OR p."Id"=:scope)
            CONNECT BY NOCYCLE PRIOR p."ParentId"=p."Id" AND PRIOR p."InheritParentRoles"=1
        ), project_roles AS (
            SELECT a.project_id,m."Role" role_name FROM ancestry a JOIN "TMR_ProjectMembers" m ON m."ProjectId"=a.ancestor_id WHERE m."UserId"=:viewer
            UNION SELECT a.project_id,m."RoleName" FROM ancestry a JOIN "TMR_ProjectMemberRoles" m ON m."ProjectId"=a.ancestor_id WHERE m."UserId"=:viewer
        ), project_rights AS (
            SELECT DISTINCT r.project_id,p."Permission" FROM project_roles r JOIN "TMR_RolePermissions" p ON p."RoleName"=r.role_name
        )
        """;
    private static readonly string IssueSecuritySql = SecuritySql.Replace("START WITH (:scope IS NULL OR p.\"Id\"=:scope)", "START WITH p.\"Id\"=(SELECT \"ProjectId\" FROM \"TMR_Issues\" WHERE \"Id\"=:id) AND (:scope IS NULL OR p.\"Id\"=:scope)");
    private const string ActiveViewer = "EXISTS (SELECT 1 FROM \"TMR_AppUsers\" viewer WHERE viewer.\"Id\"=:viewer AND viewer.\"IsActive\"=1)";
    private static string HasRight(string permission) => $"(EXISTS (SELECT 1 FROM global_rights r WHERE r.\"Permission\" IN ('*',{permission})) OR EXISTS (SELECT 1 FROM project_rights r WHERE r.project_id=p.\"Id\" AND r.\"Permission\" IN ('*',{permission})))";
    private static readonly string IssueVisibilitySql = $"{ActiveViewer} AND p.\"IssuesEnabled\"=1 AND (p.\"IsPublic\"=1 OR {HasRight("'issues.view'")}) AND (i.\"IsPrivate\"=0 OR i.\"CreatedBy\"=:viewer OR i.\"AssigneeId\"=:viewer OR {HasRight("'projects.manage'")})";

    private async Task<List<T>> Query<T>(string operation, string sql, Action<OracleCommand> bind, Func<OracleDataReader, T> map)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var command = RelationalCommand(connection, transaction, sql);
        bind(command);
        var result = new List<T>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) result.Add(map(reader));
        SelectedRows.Add(result.Count, new KeyValuePair<string, object?>("query", operation));
        await transaction.CommitAsync();
        return result;
    }
    private static void Viewer(OracleCommand command, int user, int? scope = null)
    {
        command.Parameters.Add("viewer", OracleDbType.Int32).Value = user;
        command.Parameters.Add("scope", OracleDbType.Int32).Value = (object?)scope ?? DBNull.Value;
    }
    internal Task<List<string>> EffectiveSql(int user, string? kind, int? scope)
    {
        var roleSql = kind == "project" ? "SELECT role_name FROM project_roles WHERE project_id=:scope" : kind == "team" ? """
            SELECT "Role" FROM "TMR_Members" WHERE "UserId"=:viewer AND "TeamId"=:scope
            UNION SELECT r."RoleName" FROM "TMR_TeamMemberRoles" r JOIN "TMR_Members" m ON m."Id"=r."MemberId" WHERE m."UserId"=:viewer AND m."TeamId"=:scope
            """ : "SELECT \"RoleName\" FROM global_roles WHERE 1=0";
        return Query("Permissions", SecuritySql + $"SELECT DISTINCT p.\"Permission\" FROM \"TMR_RolePermissions\" p WHERE {ActiveViewer} AND (p.\"RoleName\" IN (SELECT \"RoleName\" FROM global_roles) OR p.\"RoleName\" IN ({roleSql}))", c => Viewer(c, user, scope), r => r.GetString(0));
    }
    internal async Task<bool> ManageSql(int user, int? project)
    {
        var rights = await EffectiveSql(user, project is null ? null : "project", project);
        return rights.Contains("*") || rights.Contains(project is null ? "organization.manage" : "projects.manage");
    }
    private static string CanSqlPredicate(string permission) => $"{ActiveViewer} AND p.\"IssuesEnabled\"=1 AND (:time_required=0 OR p.\"TimeEnabled\"=1) AND (:view_only=1 OR p.\"State\"='Active') AND ((:view_only=1 AND p.\"IsPublic\"=1) OR {HasRight(permission)})";
    private static void PermissionParameters(OracleCommand c, string right)
    {
        AddQueryParameter(c, "permission", right == "time.view" ? "issues.view" : right);
        AddQueryParameter(c, "time_required", right.StartsWith("time.") ? 1 : 0);
        AddQueryParameter(c, "view_only", right is "time.view" or "issues.view" ? 1 : 0);
    }
    internal async Task<bool> CanSql(int user, int project, string permission) => (await Query("Can", SecuritySql + $"SELECT 1 FROM \"TMR_Projects\" p WHERE p.\"Id\"=:scope AND {CanSqlPredicate(":permission")}", c => { Viewer(c, user, project); PermissionParameters(c, permission); }, r => 1)).Count != 0;
    internal Task<List<Team>> ProjectChoicesSql(int user, string right) => Query("ProjectChoices", SecuritySql + $"SELECT p.\"Id\",p.\"Name\" FROM \"TMR_Projects\" p WHERE {CanSqlPredicate(":permission")} ORDER BY p.\"Name\"", c => { Viewer(c, user); PermissionParameters(c, right); }, r => new Team(r.GetInt32(0), r.GetString(1)));
    internal Task<List<ProjectDetail>> ProjectListSql(int user) => Query("ProjectList", SecuritySql + $"""
        SELECT p."__EMPTY",{string.Join(',', Schema.Value.Tables.Single(t => t.Name == "Projects").Columns.Select(c => "p." + Quote(c.Name)))}
        FROM "TMR_Projects" p WHERE {ActiveViewer} AND (p."IsPublic"=1 OR {HasRight("'issues.view'")} OR {HasRight("'projects.manage'")}) ORDER BY p."Name"
        """, c => Viewer(c, user), r =>
        {
            var p = ReadTypedRow(Schema.Value.Tables.Single(t => t.Name == "Projects"), r);
            return ProjectDetailFromRow(p);
        });
    private static ProjectDetail ProjectDetailFromRow(System.Text.Json.Nodes.JsonObject p) => new(Workspace.Id(p), new() { Name = Workspace.Text(p, "Name"), Description = Workspace.Text(p, "Description"), ParentId = Workspace.OptionalInt(p, "ParentId"), IsPublic = Workspace.Flag(p, "IsPublic"), State = Workspace.Text(p, "State"), Revision = Workspace.Int(p, "Revision"), IssuesEnabled = Workspace.Flag(p, "IssuesEnabled"), TimeEnabled = Workspace.Flag(p, "TimeEnabled"), InheritParentRoles = Workspace.Flag(p, "InheritParentRoles") });
    internal async Task<IssueDetail?> IssueSql(int user, int id) => (await Query("IssueDetail", IssueSecuritySql + $"""
        SELECT i."__EMPTY",{string.Join(',', Schema.Value.Tables.Single(t => t.Name == "Issues").Columns.Select(c => "i." + Quote(c.Name)))}
        FROM "TMR_Issues" i JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" WHERE i."Id"=:id AND {IssueVisibilitySql}
        """, c => { Viewer(c, user); AddQueryParameter(c, "id", id); }, r => Detail(ReadTypedRow(Schema.Value.Tables.Single(t => t.Name == "Issues"), r)))).FirstOrDefault();
    internal Task<List<IssueRow>> IssueListSql(int user, int? project, string? status, string? query, DateTime? from, DateTime? to, bool schedule, int? assignee, bool open, bool watched) => Query("IssueList", SecuritySql + $"""
        SELECT i."Id",i."ProjectId",p."Name",i."Title",i."Status",i."Priority",u."DisplayName",i."DueDate",i."Revision",u."EmployeeNumber",i."StartDate"
        FROM "TMR_Issues" i JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" LEFT JOIN "TMR_AppUsers" u ON u."Id"=i."AssigneeId"
        WHERE {IssueVisibilitySql} AND (:scope IS NULL OR i."ProjectId"=:scope) AND (:status IS NULL OR i."Status"=:status)
          AND (:search IS NULL OR INSTR(UPPER(i."Title"),UPPER(:search))>0 OR INSTR(UPPER(i."Description"),UPPER(:search))>0)
          AND (:assignee IS NULL OR i."AssigneeId"=:assignee)
          AND (:open_only=0 OR NOT EXISTS (SELECT 1 FROM "TMR_IssueStatuses" s WHERE s."Name"=i."Status" AND s."IsClosed"=1))
          AND (:watched=0 OR EXISTS (SELECT 1 FROM "TMR_IssueWatchers" w WHERE w."IssueId"=i."Id" AND w."UserId"=:viewer))
          AND (:date_from IS NULL OR (CASE WHEN :schedule=1 THEN COALESCE(i."DueDate",i."StartDate") ELSE i."DueDate" END)>=:date_from)
          AND (:date_to IS NULL OR (CASE WHEN :schedule=1 THEN COALESCE(i."StartDate",i."DueDate") ELSE i."DueDate" END)<=:date_to)
        ORDER BY i."Id" DESC
        """, c =>
    {
        Viewer(c, user, project);
        c.Parameters.Add("status", OracleDbType.NVarchar2).Value = string.IsNullOrEmpty(status) ? DBNull.Value : status;
        c.Parameters.Add("search", OracleDbType.NVarchar2).Value = string.IsNullOrEmpty(query) ? DBNull.Value : query;
        AddQueryParameter(c, "assignee", assignee); AddQueryParameter(c, "open_only", open ? 1 : 0); AddQueryParameter(c, "watched", watched ? 1 : 0); AddQueryParameter(c, "schedule", schedule ? 1 : 0);
        c.Parameters.Add("date_from", OracleDbType.TimeStamp).Value = (object?)from ?? DBNull.Value;
        c.Parameters.Add("date_to", OracleDbType.TimeStamp).Value = (object?)to ?? DBNull.Value;
    }, r => new IssueRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetDateTime(7), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetDateTime(10)));
    internal Task<List<AttachmentInfo>> AttachmentsSql(int user, int issue, int project) => Query("AttachmentMetadata", SecuritySql + $"""
        SELECT a."Id",a."FileName",DBMS_LOB.GETLENGTH(a."Content")
        FROM "TMR_IssueAttachments" a JOIN "TMR_Issues" i ON i."Id"=a."IssueId" JOIN "TMR_Projects" p ON p."Id"=i."ProjectId"
        WHERE i."Id"=:issue AND i."ProjectId"=:scope AND {IssueVisibilitySql} ORDER BY a."Id" DESC
        """, c => { Viewer(c, user, project); AddQueryParameter(c, "issue", issue); }, r => new AttachmentInfo(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
    internal Task<List<Team>> AssigneesSql(int project) => Query("Assignees", """
        SELECT u."Id",u."DisplayName" FROM "TMR_AppUsers" u
        WHERE EXISTS (SELECT 1 FROM "TMR_ProjectMembers" m WHERE m."ProjectId"=:project AND m."UserId"=u."Id")
        ORDER BY u."DisplayName"
        """, c => AddQueryParameter(c, "project", project), r => new Team(r.GetInt32(0), r.GetString(1)));
    internal async Task<bool> ReadySql() => (await Query("Readiness", "SELECT 1 FROM TMR_WORKSPACE WHERE ID=1 AND SCHEMA_VERSION=7 AND EXISTS (SELECT 1 FROM TMR_RUNTIME_FEATURES WHERE NAME='fine-writes' AND VERSION=1) AND EXISTS (SELECT 1 FROM \"TMR_AppUsers\")", _ => { }, r => 1)).Count == 1;
    private static async Task RequireAdministrator(OracleConnection connection, OracleTransaction transaction)
    {
        await using var command = RelationalCommand(connection, transaction, """
            SELECT 1 FROM "TMR_AppUsers" u WHERE u."IsActive"=1 AND (
                EXISTS (SELECT 1 FROM "TMR_UserRoles" r JOIN "TMR_RolePermissions" p ON p."RoleName"=r."RoleName" WHERE r."UserId"=u."Id" AND p."Permission"='*')
                OR EXISTS (SELECT 1 FROM "TMR_UserGroupMembers" m JOIN "TMR_UserGroupRoles" r ON r."GroupId"=m."GroupId" JOIN "TMR_RolePermissions" p ON p."RoleName"=r."RoleName" WHERE m."UserId"=u."Id" AND p."Permission"='*')
            ) FETCH FIRST 1 ROW ONLY
            """);
        if (await command.ExecuteScalarAsync() is null)
            throw new InvalidOperationException("Mora ostati barem jedan aktivan puni administrator.");
    }
}

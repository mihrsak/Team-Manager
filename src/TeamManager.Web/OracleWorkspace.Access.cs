using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal static HashSet<string> RolesFor(Workspace w, int user, string? kind = null, int? scope = null)
    {
        var roles = w.By("UserRoles", "UserId", user).Select(r => Text(r, "RoleName")).ToHashSet();
        var groups = w.By("UserGroupMembers", "UserId", user).Select(r => Int(r, "GroupId")).ToHashSet();
        roles.UnionWith(groups.SelectMany(group => w.By("UserGroupRoles", "GroupId", group)).Select(r => Text(r, "RoleName")));
        if (kind == "project" && scope is int project)
        {
            var ancestors = new HashSet<int>();
            while (ancestors.Add(project) && w.Find("Projects", project)is { } current)
            {
                roles.UnionWith(w.By("ProjectMembers", "ProjectId", project).Where(r => Int(r, "UserId") == user).Select(r => Text(r, "Role")));
                roles.UnionWith(w.By("ProjectMemberRoles", "ProjectId", project).Where(r => Int(r, "UserId") == user).Select(r => Text(r, "RoleName")));
                if (!Flag(current, "InheritParentRoles") || OptionalInt(current, "ParentId")is not int parent)
                    break;
                project = parent;
            }
        }

        if (kind == "team")
        {
            var members = w.By("Members", "UserId", user).Where(r => Int(r, "TeamId") == scope).ToArray();
            roles.UnionWith(members.Select(r => Text(r, "Role")));
            roles.UnionWith(members.SelectMany(m => w.By("TeamMemberRoles", "MemberId", Id(m))).Select(r => Text(r, "RoleName")));
        }

        return roles;
    }

    internal static List<string> Rights(Workspace w, int user, string? kind = null, int? scope = null)
    {
        if (w.Find("AppUsers", user)is not { } account || !Flag(account, "IsActive"))
            return[];
        var roles = RolesFor(w, user, kind, scope);
        return roles.SelectMany(role => w.By("RolePermissions", "RoleName", role)).Select(r => Text(r, "Permission")).Distinct().ToList();
    }

    internal static bool Right(Workspace w, int user, string permission, int? project = null)
    {
        var rights = Rights(w, user, project is null ? null : "project", project);
        return rights.Contains("*") || rights.Contains(permission);
    }

    internal static bool Manage(Workspace w, int user, int? project) => Right(w, user, project is null ? "organization.manage" : "projects.manage", project);
    internal static bool Can(Workspace w, int user, int project, string permission)
    {
        if (w.Find("AppUsers", user) is not { } account || !Flag(account, "IsActive"))
            return false;
        var p = w.Find("Projects", project);
        if (p is null || !Flag(p, "IssuesEnabled"))
            return false;
        if (permission.StartsWith("time.") && !Flag(p, "TimeEnabled"))
            return false;
        if (permission == "time.view")
            permission = "issues.view";
        if (permission != "issues.view" && Text(p, "State") != "Active")
            return false;
        return permission == "issues.view" && Flag(p, "IsPublic") || Right(w, user, permission, project);
    }

    internal static bool Visible(Workspace w, int user, JsonObject issue) => Can(w, user, Int(issue, "ProjectId"), "issues.view") && (!Flag(issue, "IsPrivate") || Int(issue, "CreatedBy") == user || OptionalInt(issue, "AssigneeId") == user || Manage(w, user, Int(issue, "ProjectId")));
    internal static JsonObject RequireIssue(Workspace w, int user, int issue, string permission = "issues.view")
    {
        var row = w.Find("Issues", issue);
        if (row is null || !Visible(w, user, row) || !Can(w, user, Int(row, "ProjectId"), permission))
            throw new UnauthorizedAccessException();
        return row;
    }

    internal static void Audit(Workspace w, int user, string action, string subject, string detail = "")
    {
        w.Add("AdministrationAudit", ("Id", w.NextLong("AdministrationAudit")), ("ActorId", user), ("ActorEmployeeNumber", Text(w.Find("AppUsers", user)!, "EmployeeNumber")), ("Action", action), ("Subject", subject), ("Details", detail), ("CreatedAt", DateTime.UtcNow));
    }

    internal static AppUser Account(JsonObject r) => new(Id(r), Text(r, "EmployeeNumber"), Text(r, "DisplayName"), Text(r, "JobTitle"), OptionalInt(r, "OrganizationUnitId"), Text(r, "PasswordHash"), Flag(r, "IsAdministrator"), Flag(r, "IsActive"), Int(r, "SessionVersion"));
    internal Task<List<string>> Effective(int user, string? kind, int? scope) => EffectiveSql(user, kind, scope);
    internal Task<List<AccessRole>> AccessRoles() => Read(w => w.Rows("Roles").OrderBy(r => Text(r, "Name")).Select(r => new AccessRole(Text(r, "Name"), Text(r, "Description"), w.Rows("RolePermissions").Where(p => Text(p, "RoleName") == Text(r, "Name")).Select(p => Text(p, "Permission")).ToList())).ToList());
    internal Task<int> SaveProject(int user, int? id, ProjectInput input) => WriteProjectMetadata(user,id,input.ParentId,input.Name,w =>
    {
        if (!Manage(w, user, id))
            throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || (input.Description?.Length ?? 0) > 4000 || input.State is not ("Active" or "Closed" or "Archived"))
            throw new ArgumentException("Provjeri naziv, opis i stanje projekta.");
        var existing = id is int project ? w.Find("Projects", project) : null;
        if (id is not null && existing is null)
            throw new UnauthorizedAccessException();
        if (existing is not null)
            Revision(existing, input.Revision, "Projekt je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
        if (input.InheritParentRoles && input.ParentId is null)
            throw new ArgumentException("Za nasljeđivanje uloga odaberi nadređeni projekt.");
        if (input.ParentId is int parent && (existing is null || OptionalInt(existing, "ParentId") != parent || input.InheritParentRoles && !Flag(existing, "InheritParentRoles")) && !Manage(w, user, parent))
            throw new UnauthorizedAccessException();
        var seen = new HashSet<int>();
        if (id is not null)
            seen.Add(id.Value);
        for (var ancestor = input.ParentId; ancestor is int current;)
        {
            if (!seen.Add(current))
                throw new ArgumentException("Projekt ne može biti vlastiti nadređeni projekt niti potomak svojeg potprojekta.");
            var p = w.Find("Projects", current) ?? throw new ArgumentException("Nadređeni projekt ne postoji.");
            ancestor = OptionalInt(p, "ParentId");
        }

        if (w.Where("Projects", "UPPER(t.\"Name\")=UPPER(:name)", ("name", input.Name.Trim())).Any(p => Id(p) != id))
            throw new ArgumentException("Projekt s tim nazivom već postoji.");
        var row = existing ?? w.Add("Projects", ("Id", w.Next("Projects")), ("IssuesEnabled", true), ("TimeEnabled", true), ("Revision", 0));
        Set(row, "Name", input.Name.Trim());
        Set(row, "Description", input.Description ?? "");
        Set(row, "ParentId", input.ParentId);
        Set(row, "IsPublic", input.IsPublic);
        Set(row, "State", input.State);
        Set(row, "InheritParentRoles", input.InheritParentRoles);
        Revise(row);
        if (existing is null)
            w.Add("ProjectMembers", ("ProjectId", Id(row)), ("UserId", user), ("Role", "Voditelj"));
        Audit(w, user, existing is null ? "project.create" : "project.update", input.Name.Trim());
        return Id(row);
    });
    internal Task<List<ProjectDetail>> ProjectList(int user) => ProjectListSql(user);
}

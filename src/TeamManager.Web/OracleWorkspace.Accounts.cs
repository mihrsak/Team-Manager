using Microsoft.AspNetCore.Identity;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<AppUser?> Authenticate(string employee, string password, IPasswordHasher<AppUser> hasher) => Write(w =>
    {
        var row = w.Rows("AppUsers").FirstOrDefault(r => Text(r, "EmployeeNumber").Equals(employee, StringComparison.OrdinalIgnoreCase));
        if (row is null || !Flag(row, "IsActive") || Date(row, "LockoutUntil") > DateTime.UtcNow)
            return null;
        var account = Account(row);
        if (hasher.VerifyHashedPassword(account, account.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            var failures = Int(row, "FailedAttempts") + 1;
            Set(row, "FailedAttempts", failures >= 5 ? 0 : failures);
            if (failures >= 5)
                Set(row, "LockoutUntil", DateTime.UtcNow.AddMinutes(5));
            return null;
        }

        Set(row, "FailedAttempts", 0);
        Set(row, "LockoutUntil", null);
        return account;
    });
    internal Task<List<Membership>> Memberships(int user, bool team) => Read(w =>
    {
        var result = new List<Membership>();
        foreach (var m in w.Rows(team ? "Members" : "ProjectMembers").Where(r => Int(r, "UserId") == user))
        {
            var scope = Int(m, team ? "TeamId" : "ProjectId");
            var name = Text(w.Find(team ? "Teams" : "Projects", scope)!, "Name");
            result.Add(new(scope, name, Text(m, "Role"), false));
            result.AddRange(w.Rows(team ? "TeamMemberRoles" : "ProjectMemberRoles").Where(r => team ? Int(r, "MemberId") == Id(m) : Int(r, "ProjectId") == scope && Int(r, "UserId") == user).Select(r => new Membership(scope, name, Text(r, "RoleName"), true)));
        }

        return result.OrderBy(r => r.Scope).ThenBy(r => r.IsAdditional).ToList();
    });
    internal Task Membership(int actor, int user, string kind, int scope, string? role, bool removeRole, bool replace, bool removeMember) => Write(w =>
    {
        var rights = Rights(w, actor, kind, scope);
        if (!rights.Contains("*") && !rights.Contains("members.manage"))
            throw new UnauthorizedAccessException();
        if (w.Find("AppUsers", user)is null || w.Find(kind == "team" ? "Teams" : "Projects", scope)is null)
            throw new ArgumentException("Odaberi tim ili projekt i valjanu ulogu.");
        if (!removeMember)
        {
            if (!w.Rows("Roles").Any(r => Text(r, "Name") == role))
                throw new ArgumentException("Nepostojeća rola.");
            if (!rights.Contains("*") && w.Rows("RolePermissions").Any(r => Text(r, "RoleName") == role && !rights.Contains(Text(r, "Permission"))))
                throw new UnauthorizedAccessException();
        }

        var team = kind == "team";
        var table = team ? "Members" : "ProjectMembers";
        var additional = team ? "TeamMemberRoles" : "ProjectMemberRoles";
        var member = w.Rows(table).FirstOrDefault(r => Int(r, "UserId") == user && Int(r, team ? "TeamId" : "ProjectId") == scope);
        bool Match(System.Text.Json.Nodes.JsonObject r) => team ? Int(r, "MemberId") == (member is null ? 0 : Id(member)) : Int(r, "ProjectId") == scope && Int(r, "UserId") == user;
        if (removeMember)
        {
            w.Remove(additional, Match);
            if (member is not null)
                w.Table(table).Remove(member);
        }
        else if (removeRole)
            w.Remove(additional, r => Match(r) && Text(r, "RoleName") == role);
        else if (member is null)
        {
            if (team)
                w.Add(table, ("Id", w.Next(table)), ("TeamId", scope), ("UserId", user), ("Name", Text(w.Find("AppUsers", user)!, "EmployeeNumber")), ("Role", role));
            else
                w.Add(table, ("ProjectId", scope), ("UserId", user), ("Role", role));
        }
        else if (replace)
        {
            Set(member, "Role", role);
            w.Remove(additional, r => Match(r) && Text(r, "RoleName") == role);
        }
        else if (Text(member, "Role") != role && !w.Rows(additional).Any(r => Match(r) && Text(r, "RoleName") == role))
        {
            if (team)
                w.Add(additional, ("MemberId", Id(member)), ("RoleName", role));
            else
                w.Add(additional, ("ProjectId", scope), ("UserId", user), ("RoleName", role));
        }

        Audit(w, actor, removeMember ? "membership.remove" : removeRole ? "membership.role.remove" : replace ? "membership.role.replace" : "membership.role.add", $"#{user}", $"{kind} #{scope}: {role}");
        return true;
    });
    private static void GuardAdmin(Workspace w)
    {
        if (!w.Rows("AppUsers").Any(u => Flag(u, "IsActive") && Rights(w, Id(u)).Contains("*")))
            throw new InvalidOperationException("Mora ostati barem jedan aktivan puni administrator.");
    }

    internal Task SaveRole(int actor, string name, string description, string[] rights, bool create) => Write(w =>
    {
        if (!Right(w, actor, "roles.manage"))
            throw new UnauthorizedAccessException();
        var role = w.Rows("Roles").FirstOrDefault(r => Text(r, "Name") == name);
        if (create && role is not null)
            throw new ArgumentException("Naziv nije valjan ili već postoji.");
        role ??= w.Add("Roles", ("Name", name));
        Set(role, "Description", description);
        w.Remove("RolePermissions", r => Text(r, "RoleName") == name);
        foreach (var right in rights.Distinct())
            w.Add("RolePermissions", ("RoleName", name), ("Permission", right));
        GuardAdmin(w);
        Audit(w, actor, create ? "role.create" : "role.update", name);
        return true;
    });
    internal Task AssignRole(int actor, int user, string role, bool remove) => Write(w =>
    {
        if (!Right(w, actor, "roles.manage"))
            throw new UnauthorizedAccessException();
        if (w.Find("AppUsers", user)is null || !w.Rows("Roles").Any(r => Text(r, "Name") == role))
            throw new ArgumentException("Nepostojeća rola.");
        w.Remove("UserRoles", r => Int(r, "UserId") == user && Text(r, "RoleName") == role);
        if (!remove)
            w.Add("UserRoles", ("UserId", user), ("RoleName", role));
        GuardAdmin(w);
        Audit(w, actor, remove ? "role.revoke" : "role.assign", $"#{user}", role);
        return true;
    });
    internal Task ManageAccount(int actor, int user, bool? active, string? hash) => Write(w =>
    {
        if (actor == user || !Rights(w, actor).Contains("*"))
            throw new UnauthorizedAccessException();
        var row = w.Find("AppUsers", user) ?? throw new UnauthorizedAccessException();
        if (active is not null)
            Set(row, "IsActive", active.Value);
        if (hash is not null)
        {
            Set(row, "PasswordHash", hash);
            Set(row, "LockoutUntil", null);
            Set(row, "FailedAttempts", 0);
        }

        Set(row, "SessionVersion", Int(row, "SessionVersion") + 1);
        GuardAdmin(w);
        Audit(w, actor, hash is not null ? "account.reset" : active is true ? "account.activate" : active is false ? "account.deactivate" : "account.revoke", $"#{user}");
        return true;
    });
}

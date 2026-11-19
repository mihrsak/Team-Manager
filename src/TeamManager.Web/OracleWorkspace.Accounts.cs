using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using Microsoft.AspNetCore.Identity;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<AppUser?> Authenticate(string employee, string password, IPasswordHasher<AppUser> hasher) => WriteLogin(employee, async w =>
    {
        var row = (await w.Where("AppUsers", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).FirstOrDefault();
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
    internal Task<List<Membership>> Memberships(int user, bool team) => Read(async w =>
    {
        var result = new List<Membership>();
        foreach (var m in (await w.By(team ? "Members" : "ProjectMembers", "UserId", user)))
        {
            var scope = Int(m, team ? "TeamId" : "ProjectId");
            var name = Text((await w.Find(team ? "Teams" : "Projects", scope))!, "Name");
            result.Add(new(scope, name, Text(m, "Role"), false));
            result.AddRange((await w.By(team ? "TeamMemberRoles" : "ProjectMemberRoles", team ? "MemberId" : "ProjectId", team ? Id(m) : scope)).Where(r => team || Int(r, "UserId") == user).Select(r => new Membership(scope, name, Text(r, "RoleName"), true)));
        }

        return result.OrderBy(r => r.Scope).ThenBy(r => r.IsAdditional).ToList();
    });
    internal Task Membership(int actor, int user, string kind, int scope, string? role, bool removeRole, bool replace, bool removeMember) => WriteAccount(actor, user, async w =>
    {
        var rights = (await Rights(w, actor, kind, scope));
        if (!rights.Contains("*") && !rights.Contains("members.manage"))
            throw new UnauthorizedAccessException();
        if ((await w.Find("AppUsers", user)) is null || (await w.Find(kind == "team" ? "Teams" : "Projects", scope)) is null)
            throw new ArgumentException("Odaberi tim ili projekt i valjanu ulogu.");
        if (!removeMember)
        {
            if (!(await w.By("Roles", "Name", role)).Any())
                throw new ArgumentException("Nepostojeća rola.");
            if (!rights.Contains("*") && (await w.By("RolePermissions", "RoleName", role)).Any(r => !rights.Contains(Text(r, "Permission"))))
                throw new UnauthorizedAccessException();
        }

        var team = kind == "team";
        var table = team ? "Members" : "ProjectMembers";
        var additional = team ? "TeamMemberRoles" : "ProjectMemberRoles";
        var member = (await w.By(table, "UserId", user)).FirstOrDefault(r => Int(r, team ? "TeamId" : "ProjectId") == scope);
        bool Match(System.Text.Json.Nodes.JsonObject r) => team ? Int(r, "MemberId") == (member is null ? 0 : Id(member)) : Int(r, "ProjectId") == scope && Int(r, "UserId") == user;
        if (removeMember)
        {
            await w.RemoveBy(additional, team ? "MemberId" : "ProjectId", team ? (member is null ? 0 : Id(member)) : scope, Match);
            if (member is not null)
                w.Table(table).Remove(member);
        }
        else if (removeRole)
            await w.RemoveBy(additional, team ? "MemberId" : "ProjectId", team ? (member is null ? 0 : Id(member)) : scope, r => Match(r) && Text(r, "RoleName") == role);
        else if (member is null)
        {
            if (team)
                w.Add(table, ("Id", (await w.Next(table))), ("TeamId", scope), ("UserId", user), ("Name", Text((await w.Find("AppUsers", user))!, "EmployeeNumber")), ("Role", role));
            else
                w.Add(table, ("ProjectId", scope), ("UserId", user), ("Role", role));
        }
        else if (replace)
        {
            Set(member, "Role", role);
            await w.RemoveBy(additional, team ? "MemberId" : "ProjectId", team ? (member is null ? 0 : Id(member)) : scope, r => Match(r) && Text(r, "RoleName") == role);
        }
        else if (Text(member, "Role") != role && !(await w.Rows(additional)).Any(r => Match(r) && Text(r, "RoleName") == role))
        {
            if (team)
                w.Add(additional, ("MemberId", Id(member)), ("RoleName", role));
            else
                w.Add(additional, ("ProjectId", scope), ("UserId", user), ("RoleName", role));
        }

        await Audit(w, actor, removeMember ? "membership.remove" : removeRole ? "membership.role.remove" : replace ? "membership.role.replace" : "membership.role.add", $"#{user}", $"{kind} #{scope}: {role}");
        return true;
    }, role, kind == "project" ? scope : null);
    private static void GuardAdmin(Workspace w) => w.PreserveAdministrator = true;

    internal Task SaveRole(int actor, string name, string description, string[] rights, bool create) => WriteCatalog(actor, [], ["role:" + name], async w =>
    {
        if (!(await Right(w, actor, "roles.manage")))
            throw new UnauthorizedAccessException();
        var role = (await w.By("Roles", "Name", name)).FirstOrDefault();
        if (create && role is not null)
            throw new ArgumentException("Naziv nije valjan ili već postoji.");
        role ??= w.Add("Roles", ("Name", name));
        Set(role, "Description", description);
        await w.RemoveBy("RolePermissions", "RoleName", name);
        foreach (var right in rights.Distinct())
            w.Add("RolePermissions", ("RoleName", name), ("Permission", right));
        GuardAdmin(w);
        await Audit(w, actor, create ? "role.create" : "role.update", name);
        return true;
    });
    internal Task AssignRole(int actor, int user, string role, bool remove) => WriteAccount(actor, user, async w =>
    {
        if (!(await Right(w, actor, "roles.manage")))
            throw new UnauthorizedAccessException();
        if ((await w.Find("AppUsers", user)) is null || !(await w.By("Roles", "Name", role)).Any())
            throw new ArgumentException("Nepostojeća rola.");
        await w.RemoveBy("UserRoles", "UserId", user, r => Text(r, "RoleName") == role);
        if (!remove)
            w.Add("UserRoles", ("UserId", user), ("RoleName", role));
        GuardAdmin(w);
        await Audit(w, actor, remove ? "role.revoke" : "role.assign", $"#{user}", role);
        return true;
    }, role);
    internal Task ManageAccount(int actor, int user, bool? active, string? hash) => WriteAccount(actor, user, async w =>
    {
        if (actor == user || !(await Rights(w, actor)).Contains("*"))
            throw new UnauthorizedAccessException();
        var row = (await w.Find("AppUsers", user)) ?? throw new UnauthorizedAccessException();
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
        await Audit(w, actor, hash is not null ? "account.reset" : active is true ? "account.activate" : active is false ? "account.deactivate" : "account.revoke", $"#{user}");
        return true;
    });
}

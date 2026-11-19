using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using static TeamManager.Web.UserStore;
namespace TeamManager.Web;

public sealed class ProjectInput
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(4000)] public string Description { get; set; } = "";
    public int? ParentId { get; set; }
    public bool InheritParentRoles { get; set; }
    public bool IsPublic { get; set; }
    public string State { get; set; } = "Active";
    public int Revision { get; set; }
    public bool IssuesEnabled { get; set; } = true;
    public bool TimeEnabled { get; set; } = true;
}
public record ProjectDetail(int Id, ProjectInput Input);
public sealed class ProjectStore(IConfiguration config, Permissions permissions, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(config.GetConnectionString("TeamManager"));
    public async Task<bool> CanManage(int user, int? id)
    {
        if (oracle.Enabled) return await oracle.ManageSql(user, id);
        var rights = await permissions.Effective(user, id is null ? null : "project", id);
        return rights.Contains("*") || rights.Contains(id is null ? "organization.manage" : "projects.manage");
    }
    public async Task<List<ProjectDetail>> List(int user)
    {
        if (oracle.Enabled) return await oracle.ProjectList(user);
        var all = new List<ProjectDetail>();
        await using (var c = Connect())
        {
            await c.OpenAsync(); await using var cmd = new SqlCommand("SELECT Id,Name,Description,ParentId,IsPublic,State,Revision,IssuesEnabled,TimeEnabled,InheritParentRoles FROM dbo.Projects ORDER BY Name", c);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) all.Add(new(r.GetInt32(0), new() { Name = r.GetString(1), Description = r.GetString(2), ParentId = r.IsDBNull(3) ? null : r.GetInt32(3), IsPublic = r.GetBoolean(4), State = r.GetString(5), Revision = r.GetInt32(6), IssuesEnabled = r.GetBoolean(7), TimeEnabled = r.GetBoolean(8), InheritParentRoles = r.GetBoolean(9) }));
        }
        var result = new List<ProjectDetail>();
        foreach (var p in all) { var rights = await permissions.Effective(user, "project", p.Id); if (p.Input.IsPublic || rights.Contains("*") || rights.Contains("issues.view") || rights.Contains("projects.manage")) result.Add(p); }
        return result;
    }
    public async Task SetModules(int user, int id, bool issues, bool time, int revision)
    {
        if (oracle.Enabled)
        {
            await oracle.WriteProjectMetadata(user, id, null, null, async w =>
            {
                if (!(await OracleWorkspace.Manage(w, user, id))) throw new UnauthorizedAccessException();
                var p = (await w.Find("Projects", id)) ?? throw new UnauthorizedAccessException();
                OracleWorkspace.Workspace.Revision(p, revision, "Projekt je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
                OracleWorkspace.Workspace.Set(p, "IssuesEnabled", issues);
                OracleWorkspace.Workspace.Set(p, "TimeEnabled", time);
                OracleWorkspace.Workspace.Revise(p);
                await OracleWorkspace.Audit(w, user, "project.modules", $"#{id}");
                return true;
            });
            return;
        }
        if (!await CanManage(user, id)) throw new UnauthorizedAccessException();
        await using var c = Connect();
        var rows = await AdministrationAuditStore.Execute(c, new(user, "project.modules", $"#{id}", $"Zadaci: {issues}; vrijeme: {time}"),
            "UPDATE dbo.Projects SET IssuesEnabled=@issues,TimeEnabled=@time,Revision=Revision+1 WHERE Id=@id AND Revision=@revision",
            P("@issues", issues), P("@time", time), P("@id", id), P("@revision", revision));
        if (rows != 1) throw new InvalidOperationException("Projekt je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
    }
    public async Task<int> Save(int user, int? id, ProjectInput input)
    {
        if (oracle.Enabled) return await oracle.SaveProject(user, id, input);
        if (!await CanManage(user, id)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || (input.Description?.Length ?? 0) > 4000 || !new[] { "Active", "Closed", "Archived" }.Contains(input.State)) throw new ArgumentException("Provjeri naziv, opis i stanje projekta.");
        // Changing hierarchy requires authority over the destination parent as well.
        await using var c = Connect(); await c.OpenAsync(); await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        await using (var mutex = new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='TeamManager.Projects',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51002,'Project changes are busy',1;", c, tx)) await mutex.ExecuteNonQueryAsync();
        int? oldParent = null;
        bool oldInheritance = false;
        if (id is not null)
        {
            await using var old = new SqlCommand("SELECT ParentId,InheritParentRoles FROM dbo.Projects WHERE Id=@id", c, tx);
            old.Parameters.Add(P("@id", id));
            await using var reader = await old.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                oldParent = reader.IsDBNull(0) ? null : reader.GetInt32(0);
                oldInheritance = reader.GetBoolean(1);
            }
        }
        if (input.InheritParentRoles && input.ParentId is null)
            throw new ArgumentException("Za nasljeđivanje uloga odaberi nadređeni projekt.");
        if (input.ParentId is int selectedParent &&
            (input.ParentId != oldParent || (input.InheritParentRoles && !oldInheritance)) &&
            !await CanManage(user, selectedParent)) throw new UnauthorizedAccessException();
        var visited = new HashSet<int>(); if (id is not null) visited.Add(id.Value);
        var ancestor = input.ParentId;
        while (ancestor is not null)
        {
            if (!visited.Add(ancestor.Value)) throw new ArgumentException("Projekt ne može biti vlastiti nadređeni projekt niti potomak svojeg potprojekta.");
            await using var parent = new SqlCommand("SELECT ParentId FROM dbo.Projects WHERE Id=@id", c, tx); parent.Parameters.Add(P("@id", ancestor)); var value = await parent.ExecuteScalarAsync();
            if (value is null) throw new ArgumentException("Nadređeni projekt ne postoji."); ancestor = value is DBNull ? null : Convert.ToInt32(value);
        }
        var sql = id is null ? "INSERT dbo.Projects(Name,Description,ParentId,IsPublic,State,InheritParentRoles) OUTPUT inserted.Id VALUES(@n,@d,@p,@public,@state,@inherit)" : "UPDATE dbo.Projects SET Name=@n,Description=@d,ParentId=@p,IsPublic=@public,State=@state,InheritParentRoles=@inherit,Revision=Revision+1 OUTPUT inserted.Id WHERE Id=@id AND Revision=@rev";
        await using var cmd = new SqlCommand(sql, c, tx); cmd.Parameters.AddRange([P("@n", input.Name.Trim()), P("@d", input.Description ?? ""), P("@p", input.ParentId), P("@public", input.IsPublic), P("@state", input.State), P("@id", id), P("@rev", input.Revision), P("@inherit", input.InheritParentRoles)]);
        var saved = await cmd.ExecuteScalarAsync(); if (saved is null) throw new InvalidOperationException("Projekt je u međuvremenu izmijenjen. Ponovno otvori stranicu.");
        var result = Convert.ToInt32(saved);
        if (id is null) { await using var member = new SqlCommand("INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@p,@u,N'Voditelj')", c, tx); member.Parameters.AddRange([P("@p", result), P("@u", user)]); await member.ExecuteNonQueryAsync(); }
        await AdministrationAuditStore.Write(c, tx, new(user, id is null ? "project.create" : "project.update", input.Name.Trim(), $"Projekt #{result}; stanje: {input.State}"));
        await tx.CommitAsync(); return result;
    }
}

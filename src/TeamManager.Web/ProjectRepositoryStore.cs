using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record ProjectRepository(int Id, string Name, string Url);

public sealed class ProjectRepositoryStore(IConfiguration configuration, ProjectStore projects)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<ProjectRepository>> List(int project)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Id,Name,Url FROM dbo.ProjectRepositories WHERE ProjectId=@project ORDER BY Name", connection);
        command.Parameters.Add(P("@project", project));
        var repositories = new List<ProjectRepository>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            repositories.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2)));
        return repositories;
    }

    public async Task<bool> Add(int actor, int project, string name, Uri uri)
    {
        if (!await projects.CanManage(actor, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        return await AdministrationAuditStore.Execute(connection,
            new(actor,"project.repository.add",name,$"#{project}"),"""
            INSERT dbo.ProjectRepositories(ProjectId,Name,Url)
            SELECT Id,@name,@url FROM dbo.Projects WITH(HOLDLOCK) WHERE Id=@project AND State='Active'
            """, P("@project", project), P("@name", name), P("@url", uri.AbsoluteUri)) == 1;
    }

    public async Task<bool> Remove(int actor, int project, int id)
    {
        if (!await projects.CanManage(actor, project)) throw new UnauthorizedAccessException();
        await using var connection = Connect();
        return await AdministrationAuditStore.Execute(connection,
            new(actor,"project.repository.remove",$"#{id}",$"#{project}"),"""
            DELETE r FROM dbo.ProjectRepositories r JOIN dbo.Projects p WITH(HOLDLOCK) ON p.Id=r.ProjectId
            WHERE r.Id=@id AND r.ProjectId=@project AND p.State='Active'
            """, P("@id",id), P("@project",project)) == 1;
    }
}

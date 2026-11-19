using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

internal sealed class RepositoryNameConflictException() : InvalidOperationException("Taj naziv već postoji.");

public sealed partial class OracleWorkspace
{
    internal Task<List<ProjectRepository>> RepositoriesRead(int user, int project, int? repository = null, bool history = false)
        => Query("ProjectRepositories", SecuritySql + $"""
            SELECT r."Id",r."Name",r."Url" FROM "TMR_ProjectRepositories" r JOIN "TMR_Projects" p ON p."Id"=r."ProjectId"
            WHERE p."Id"=:scope AND (:repository IS NULL OR r."Id"=:repository) AND {ProjectContentVisibilitySql}
              AND (:history=0 OR {HasRight("'repositories.view'")}) ORDER BY r."Name"
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "repository", repository); AddQueryParameter(c, "history", history ? 1 : 0); },
            r => new ProjectRepository(r.GetInt32(0), r.GetString(1), r.GetString(2)));

    internal Task<bool> AddRepository(int actor, int project, string name, Uri uri)
        => WriteProjectData(actor, project, "repositories", async w =>
        {
            if (!await Manage(w, actor, project)) throw new UnauthorizedAccessException();
            if ((await w.Find("Projects", project)) is not { } p || Text(p, "State") != "Active") return false;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsoluteUri.Length > 500)
                throw new ArgumentException("Unesi naziv do 80 znakova i HTTPS adresu repozitorija bez vjerodajnica, parametara ili fragmenta.");
            if ((await w.Where("ProjectRepositories", "t.\"ProjectId\"=:project AND UPPER(t.\"Name\")=UPPER(:name)", ("project", project), ("name", name))).Any())
                throw new RepositoryNameConflictException();
            w.Add("ProjectRepositories", ("Id", await w.Next("ProjectRepositories")), ("ProjectId", project), ("Name", name), ("Url", uri.AbsoluteUri));
            await Audit(w, actor, "project.repository.add", name, $"#{project}");
            return true;
        });

    internal Task<bool> RemoveRepository(int actor, int project, int id)
        => WriteProjectData(actor, project, "repositories", async w =>
        {
            if (!await Manage(w, actor, project)) throw new UnauthorizedAccessException();
            if ((await w.Find("Projects", project)) is not { } p || Text(p, "State") != "Active") return false;
            var row = await w.Find("ProjectRepositories", id);
            if (row is null || Int(row, "ProjectId") != project) return false;
            w.Table("ProjectRepositories").Remove(row);
            await Audit(w, actor, "project.repository.remove", $"#{id}", $"#{project}");
            return true;
        });
}

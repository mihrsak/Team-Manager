using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private static readonly string ProjectContentVisibilitySql = $"{ActiveViewer} AND (p.\"IsPublic\"=1 OR {HasRight("'issues.view'")} OR {HasRight("'projects.manage'")})";

    internal Task<List<ProjectNewsItem>> NewsList(int user, int project, int? news)
        => Query("ProjectNews", SecuritySql + $"""
            SELECT n."Id",n."Title",CASE WHEN :news IS NULL THEN TO_NCLOB('') ELSE n."Body" END,u."DisplayName",n."CreatedAt"
            FROM "TMR_ProjectNews" n JOIN "TMR_Projects" p ON p."Id"=n."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=n."AuthorId"
            WHERE p."Id"=:scope AND (:news IS NULL OR n."Id"=:news) AND {ProjectContentVisibilitySql}
            ORDER BY n."Id" DESC
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "news", news); },
            r => new ProjectNewsItem(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3), r.GetDateTime(4)));

    internal Task<List<ProjectNewsComment>> NewsComments(int user, int project, int news)
        => Query("ProjectNewsComments", SecuritySql + $"""
            SELECT c."Body",u."DisplayName",c."CreatedAt" FROM "TMR_ProjectNewsComments" c
            JOIN "TMR_ProjectNews" n ON n."Id"=c."NewsId" JOIN "TMR_Projects" p ON p."Id"=n."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=c."AuthorId"
            WHERE p."Id"=:scope AND n."Id"=:news AND {ProjectContentVisibilitySql} ORDER BY c."Id"
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "news", news); },
            r => new ProjectNewsComment(r.GetString(0), r.GetString(1), r.GetDateTime(2)));

    private static async Task<bool> NewsVisible(Workspace w, int user, int project)
        => (await w.Find("Projects", project)) is { } p &&
            (Flag(p, "IsPublic") || await Right(w, user, "issues.view", project) || await Manage(w, user, project));

    internal Task<int?> PublishNews(int actor, int project, string title, string body)
        => WriteProjectData<int?>(actor, project, "news", async w =>
        {
            if (!await Manage(w, actor, project)) throw new UnauthorizedAccessException();
            var p = await w.Find("Projects", project);
            if (p is null || Text(p, "State") != "Active") return null;
            if (string.IsNullOrWhiteSpace(title) || title.Length > 150 || string.IsNullOrWhiteSpace(body) || body.Length > 4000)
                throw new ArgumentException("Unesi naslov do 150 znakova i tekst do 4000 znakova.");
            var id = await w.Next("ProjectNews");
            w.Add("ProjectNews", ("Id", id), ("ProjectId", project), ("AuthorId", actor), ("Title", title), ("Body", body), ("CreatedAt", DateTime.UtcNow));
            return id;
        });

    internal Task<bool> CommentNews(int actor, int project, int news, string body)
        => WriteProjectData(actor, project, "news:" + news, async w =>
        {
            if (!await NewsVisible(w, actor, project) || !await Right(w, actor, "news.comment", project))
                throw new UnauthorizedAccessException();
            var p = await w.Find("Projects", project);
            var item = await w.Find("ProjectNews", news);
            if (p is null || Text(p, "State") != "Active" || item is null || Int(item, "ProjectId") != project) return false;
            if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) throw new ArgumentException("Unesi komentar do 2000 znakova.");
            w.Add("ProjectNewsComments", ("Id", await w.Next("ProjectNewsComments")), ("NewsId", news), ("AuthorId", actor), ("Body", body), ("CreatedAt", DateTime.UtcNow));
            return true;
        });
}

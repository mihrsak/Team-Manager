using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

internal sealed class WikiTitleConflictException() : InvalidOperationException("Stranica s tim naslovom već postoji u projektu.");

public sealed partial class OracleWorkspace
{
    internal Task<List<WikiPage>> WikiPages(int user, int project)
        => Query("WikiPages", SecuritySql + $"""
            SELECT w."Id",w."Title",w."Revision" FROM "TMR_ProjectWikiPages" w JOIN "TMR_Projects" p ON p."Id"=w."ProjectId"
            WHERE p."Id"=:scope AND {ProjectContentVisibilitySql} ORDER BY w."Title"
            """, c => Viewer(c, user, project), r => new WikiPage(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));

    internal Task<List<WikiRevisionInfo>> WikiHistory(int user, int project, int page)
        => Query("WikiHistory", SecuritySql + $"""
            SELECT r."Revision",u."DisplayName",r."CreatedAt" FROM "TMR_ProjectWikiRevisions" r
            JOIN "TMR_ProjectWikiPages" w ON w."Id"=r."PageId" JOIN "TMR_Projects" p ON p."Id"=w."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=r."AuthorId"
            WHERE p."Id"=:scope AND w."Id"=:page AND {ProjectContentVisibilitySql} ORDER BY r."Revision" DESC
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "page", page); }, r => new WikiRevisionInfo(r.GetInt32(0), r.GetString(1), r.GetDateTime(2)));

    internal async Task<WikiRevision?> WikiRevisionRead(int user, int project, int page, int? revision)
        => (await Query("WikiRevision", SecuritySql + $"""
            SELECT r."Revision",r."Title",r."Body",u."DisplayName",r."CreatedAt" FROM "TMR_ProjectWikiRevisions" r
            JOIN "TMR_ProjectWikiPages" w ON w."Id"=r."PageId" JOIN "TMR_Projects" p ON p."Id"=w."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=r."AuthorId"
            WHERE p."Id"=:scope AND w."Id"=:page AND r."Revision"=COALESCE(:revision,w."Revision") AND {ProjectContentVisibilitySql}
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "page", page); AddQueryParameter(c, "revision", revision); },
            r => new WikiRevision(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3), r.GetDateTime(4)))).FirstOrDefault();

    internal Task<int?> SaveWiki(int user, int project, int? page, int revision, string title, string body)
        // A project scope also serializes title uniqueness with renames and new pages.
        => WriteProjectData<int?>(user, project, "wiki", async w =>
        {
            if (!await Right(w, user, "wiki.edit", project) || (await w.Find("Projects", project)) is not { } p ||
                Text(p, "State") != "Active" || !await ProjectContentVisible(w, user, project)) throw new UnauthorizedAccessException();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(body) || body.Length > 20000)
                throw new ArgumentException("Unesi naslov do 120 znakova i sadržaj do 20 000 znakova.");
            var row = page is int id ? await w.Find("ProjectWikiPages", id) : null;
            if (page is not null && (row is null || Int(row, "ProjectId") != project || Int(row, "Revision") != revision)) return null;
            if ((await w.Where("ProjectWikiPages", "t.\"ProjectId\"=:project AND UPPER(t.\"Title\")=UPPER(:title)", ("project", project), ("title", title))).Any(r => Id(r) != page))
                throw new WikiTitleConflictException();
            row ??= w.Add("ProjectWikiPages", ("Id", await w.Next("ProjectWikiPages")), ("ProjectId", project), ("Revision", 0));
            Set(row, "Title", title);
            Revise(row);
            w.Add("ProjectWikiRevisions", ("PageId", Id(row)), ("Revision", Int(row, "Revision")), ("Title", title), ("Body", body), ("AuthorId", user), ("CreatedAt", DateTime.UtcNow));
            return Id(row);
        });
}

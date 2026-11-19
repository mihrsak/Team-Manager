using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<List<ForumTopic>> ForumTopicsRead(int user, int project, int? topic)
        => Query("ForumTopics", SecuritySql + $"""
            SELECT t."Id",t."Title",CASE WHEN :topic IS NULL THEN TO_NCLOB('') ELSE t."Body" END,u."DisplayName",t."CreatedAt",t."IsLocked"
            FROM "TMR_ForumTopics" t JOIN "TMR_Projects" p ON p."Id"=t."ProjectId" JOIN "TMR_AppUsers" u ON u."Id"=t."AuthorId"
            WHERE p."Id"=:scope AND (:topic IS NULL OR t."Id"=:topic) AND {ProjectContentVisibilitySql} ORDER BY t."Id" DESC
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "topic", topic); },
            r => new ForumTopic(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3), r.GetDateTime(4), r.GetInt32(5) != 0));

    internal Task<List<ForumReply>> ForumRepliesRead(int user, int project, int topic)
        => Query("ForumReplies", SecuritySql + $"""
            SELECT r."Body",u."DisplayName",r."CreatedAt" FROM "TMR_ForumReplies" r
            JOIN "TMR_ForumTopics" t ON t."Id"=r."TopicId" JOIN "TMR_Projects" p ON p."Id"=t."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=r."AuthorId"
            WHERE p."Id"=:scope AND t."Id"=:topic AND {ProjectContentVisibilitySql} ORDER BY r."Id"
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "topic", topic); },
            r => new ForumReply(r.IsDBNull(0) ? "" : r.GetString(0), r.GetString(1), r.GetDateTime(2)));

    private static async Task RequireForum(Workspace w, int actor, int project, string permission)
    {
        if (!await ProjectContentVisible(w, actor, project) || !await Right(w, actor, permission, project)) throw new UnauthorizedAccessException();
    }

    internal Task<int?> CreateForumTopic(int actor, int project, string title, string body)
        => WriteProjectData<int?>(actor, project, "forum", async w =>
        {
            await RequireForum(w, actor, project, "forum.post");
            if ((await w.Find("Projects", project)) is not { } p || Text(p, "State") != "Active") return null;
            if (string.IsNullOrWhiteSpace(title) || title.Length > 150 || string.IsNullOrWhiteSpace(body) || body.Length > 4000)
                throw new ArgumentException("Unesi naslov do 150 znakova i tekst do 4000 znakova.");
            var id = await w.Next("ForumTopics");
            w.Add("ForumTopics", ("Id", id), ("ProjectId", project), ("AuthorId", actor), ("Title", title), ("Body", body), ("IsLocked", false), ("CreatedAt", DateTime.UtcNow));
            return id;
        });

    internal Task<bool> ReplyForum(int actor, int project, int topic, string body)
        => WriteProjectData(actor, project, "forum-topic:" + topic, async w =>
        {
            await RequireForum(w, actor, project, "forum.post");
            var p = await w.Find("Projects", project);
            var t = await w.Find("ForumTopics", topic);
            if (p is null || Text(p, "State") != "Active" || t is null || Int(t, "ProjectId") != project || Flag(t, "IsLocked")) return false;
            if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) throw new ArgumentException("Unesi odgovor do 4000 znakova.");
            w.Add("ForumReplies", ("Id", await w.Next("ForumReplies")), ("TopicId", topic), ("AuthorId", actor), ("Body", body), ("CreatedAt", DateTime.UtcNow));
            return true;
        });

    internal Task<bool> LockForum(int actor, int project, int topic, bool locked)
        => WriteProjectData(actor, project, "forum-topic:" + topic, async w =>
        {
            await RequireForum(w, actor, project, "forum.moderate");
            var p = await w.Find("Projects", project);
            var t = await w.Find("ForumTopics", topic);
            if (p is null || Text(p, "State") != "Active" || t is null || Int(t, "ProjectId") != project) return false;
            Set(t, "IsLocked", locked);
            return true;
        });
}

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<List<UserMatch>> DirectorySearch(string query,int offset,int take)=>Query("DirectorySearch","""
        SELECT "Id","DisplayName","EmployeeNumber","JobTitle" FROM "TMR_AppUsers"
        WHERE INSTR(UPPER("DisplayName"),UPPER(:search))>0 OR INSTR(UPPER("EmployeeNumber"),UPPER(:search))>0 OR INSTR(UPPER("JobTitle"),UPPER(:search))>0
        ORDER BY "DisplayName","EmployeeNumber" OFFSET :offset ROWS FETCH NEXT :take ROWS ONLY
        """,c=>{AddQueryParameter(c,"search",query);AddQueryParameter(c,"offset",offset);AddQueryParameter(c,"take",take);},r=>new UserMatch(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3)));
    internal Task<List<ContentSearchHit>> ContentSearch(int viewer,string query,string? module,int offset,int take)
        => Query("ProjectContentSearch",SecuritySql+$"""
            , hits AS (
                SELECT 'wiki' module,w."Id" id,w."ProjectId" project_id,w."Title" title FROM "TMR_ProjectWikiPages" w
                JOIN "TMR_ProjectWikiRevisions" r ON r."PageId"=w."Id" AND r."Revision"=w."Revision"
                WHERE INSTR(UPPER(w."Title"),UPPER(:search))>0 OR INSTR(UPPER(r."Body"),UPPER(:search))>0
                UNION ALL SELECT 'documents',d."Id",d."ProjectId",d."Title" FROM "TMR_ProjectDocuments" d
                WHERE INSTR(UPPER(d."Title"),UPPER(:search))>0 OR INSTR(UPPER(d."Description"),UPPER(:search))>0 OR INSTR(UPPER(d."FileName"),UPPER(:search))>0
                UNION ALL SELECT 'news',n."Id",n."ProjectId",n."Title" FROM "TMR_ProjectNews" n
                WHERE INSTR(UPPER(n."Title"),UPPER(:search))>0 OR INSTR(UPPER(n."Body"),UPPER(:search))>0
                UNION ALL SELECT 'forum',t."Id",t."ProjectId",t."Title" FROM "TMR_ForumTopics" t
                WHERE INSTR(UPPER(t."Title"),UPPER(:search))>0 OR INSTR(UPPER(t."Body"),UPPER(:search))>0
                   OR EXISTS(SELECT 1 FROM "TMR_ForumReplies" r WHERE r."TopicId"=t."Id" AND INSTR(UPPER(r."Body"),UPPER(:search))>0)
            )
            SELECT h.module,h.id,p."Id",h.title,p."Name" FROM hits h JOIN "TMR_Projects" p ON p."Id"=h.project_id
            WHERE (:module IS NULL OR h.module=:module) AND {ProjectContentVisibilitySql}
            ORDER BY h.module,h.title,p."Id",h.id OFFSET :offset ROWS FETCH NEXT :take ROWS ONLY
            """,c=>{Viewer(c,viewer);AddQueryParameter(c,"search",query);c.Parameters.Add("module",Oracle.ManagedDataAccess.Client.OracleDbType.Varchar2).Value=(object?)module??DBNull.Value;AddQueryParameter(c,"offset",offset);AddQueryParameter(c,"take",take);},
            r=>new ContentSearchHit(r.GetString(0).Trim(),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4)));
}

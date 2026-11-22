namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<List<TransferProject>> TransferProjects(int user)=>Query("TransferProjects",SecuritySql+$"""
        SELECT p."Id",p."Name",CASE WHEN {HasRight("'projects.manage'")} THEN 1 ELSE 0 END
        FROM "TMR_Projects" p WHERE {ActiveViewer} AND p."State"='Active' AND p."IssuesEnabled"=1
          AND {HasRight("'issues.create'")} AND (p."IsPublic"=1 OR {HasRight("'issues.view'")})
        ORDER BY p."Name"
        """,c=>Viewer(c,user),r=>new TransferProject(r.GetInt32(0),r.GetString(1),r.GetInt32(2)!=0));
}

using Microsoft.Data.SqlClient;
namespace TeamManager.Web;

public sealed record ContentSearchHit(string Module,int Id,int ProjectId,string Title,string Project);
public sealed class ProjectContentSearchStore(IConfiguration configuration,OracleWorkspace oracle)
{
    public async Task<List<ContentSearchHit>> Search(int viewer,string query,string? module,int offset,int take)
    {
        if(oracle.Enabled)return await oracle.ContentSearch(viewer,query,module,offset,take);
        await using var connection=new SqlConnection(configuration.GetConnectionString("TeamManager"));await connection.OpenAsync();
        await using var command=new SqlCommand("""
            WITH hits AS (
                SELECT 'wiki' Module,w.Id,w.ProjectId,w.Title FROM dbo.ProjectWikiPages w
                JOIN dbo.ProjectWikiRevisions r ON r.PageId=w.Id AND r.Revision=w.Revision
                WHERE CHARINDEX(@query,w.Title)>0 OR CHARINDEX(@query,r.Body)>0
                UNION ALL SELECT 'documents',d.Id,d.ProjectId,d.Title FROM dbo.ProjectDocuments d
                WHERE CHARINDEX(@query,d.Title)>0 OR CHARINDEX(@query,d.Description)>0 OR CHARINDEX(@query,d.FileName)>0
                UNION ALL SELECT 'news',n.Id,n.ProjectId,n.Title FROM dbo.ProjectNews n
                WHERE CHARINDEX(@query,n.Title)>0 OR CHARINDEX(@query,n.Body)>0
                UNION ALL SELECT 'forum',t.Id,t.ProjectId,t.Title FROM dbo.ForumTopics t
                WHERE CHARINDEX(@query,t.Title)>0 OR CHARINDEX(@query,t.Body)>0
                   OR EXISTS(SELECT 1 FROM dbo.ForumReplies r WHERE r.TopicId=t.Id AND CHARINDEX(@query,r.Body)>0)
            )
            SELECT h.Module,h.Id,p.Id,h.Title,p.Name FROM hits h JOIN dbo.Projects p ON p.Id=h.ProjectId
            WHERE (@module IS NULL OR h.Module=@module) AND EXISTS(SELECT 1 FROM dbo.AppUsers WHERE Id=@viewer AND IsActive=1)
              AND (p.IsPublic=1 OR EXISTS(SELECT 1 FROM dbo.EffectiveUserRoles r JOIN dbo.RolePermissions rp ON rp.RoleName=r.RoleName WHERE r.UserId=@viewer AND rp.Permission IN ('*','issues.view','projects.manage'))
                   OR EXISTS(SELECT 1 FROM dbo.EffectiveProjectRoles(p.Id,@viewer) r JOIN dbo.RolePermissions rp ON rp.RoleName=r.RoleName WHERE rp.Permission IN ('*','issues.view','projects.manage')))
            ORDER BY h.Module,h.Title,p.Id,h.Id OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY OPTION(MAXRECURSION 0)
            """,connection);
        command.Parameters.AddRange([UserStore.P("@viewer",viewer),UserStore.P("@query",query),UserStore.P("@module",module),UserStore.P("@offset",offset),UserStore.P("@take",take)]);
        await using var reader=await command.ExecuteReaderAsync();var result=new List<ContentSearchHit>();
        while(await reader.ReadAsync())result.Add(new(reader.GetString(0).Trim(),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4)));
        return result;
    }
}

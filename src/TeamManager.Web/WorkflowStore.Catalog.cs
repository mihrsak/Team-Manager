using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;
using static TeamManager.Web.OracleWorkspace.Workspace;
namespace TeamManager.Web;
public sealed record CatalogValue(string Kind,string Name,int SortOrder,bool IsActive);
public sealed partial class WorkflowStore
{
    internal static string CatalogTable(string kind)=>kind switch{"type"=>"IssueTypes","status"=>"IssueStatuses","priority"=>"IssuePriorities",_=>throw new ArgumentException("Neispravan naziv.")};
    public async Task<List<CatalogValue>> Catalog(string kind)
    {
        var table=CatalogTable(kind);
        if(oracle.Enabled)return await oracle.Read(async w=>(await w.Rows(table)).Select(r=>new CatalogValue(kind,Text(r,"Name"),Int(r,"SortOrder"),Flag(r,"IsActive"))).OrderBy(r=>r.SortOrder).ThenBy(r=>r.Name).ToList());
        await using var c=Connect();await c.OpenAsync();await using var cmd=new SqlCommand($"SELECT Name,SortOrder,IsActive FROM dbo.{table} ORDER BY SortOrder,Name",c);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<CatalogValue>();while(await r.ReadAsync())rows.Add(new(kind,r.GetString(0),r.GetInt32(1),r.GetBoolean(2)));return rows;
    }
    public async Task<List<string>> Selectable(string kind,string? current=null)=>(await Catalog(kind)).Where(r=>r.IsActive||r.Name==current).Select(r=>r.Name).ToList();
    public async Task SetCatalog(int actor,string kind,string name,int order,bool active)
    {
        var table=CatalogTable(kind);if(order is <0 or >100000)throw new ArgumentException("Poredak mora biti između 0 i 100000.");
        if(kind=="status"&&name=="Novo"&&!active)throw new ArgumentException("Početni status Novo mora ostati aktivan.");
        if(oracle.Enabled){await oracle.WriteCatalog(actor,[],["value:"+table+":"+name],async w=>{if(!await OracleWorkspace.Right(w,actor,"workflow.manage"))throw new UnauthorizedAccessException();var row=(await w.Rows(table)).SingleOrDefault(r=>Text(r,"Name")==name)??throw new ArgumentException("Neispravan naziv.");var before=new{Order=Int(row,"SortOrder"),Active=Flag(row,"IsActive")};OracleWorkspace.Workspace.Set(row,"SortOrder",order);OracleWorkspace.Workspace.Set(row,"IsActive",active);await OracleWorkspace.Audit(w,actor,"workflow.value.update",name,System.Text.Json.JsonSerializer.Serialize(new{Kind=kind,Before=before,After=new{Order=order,Active=active}}));return true;});return;}
        await using var c=Connect();await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var cmd=new SqlCommand($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.EffectiveUserRoles r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName JOIN dbo.AppUsers u ON u.Id=r.UserId WHERE r.UserId=@actor AND u.IsActive=1 AND p.Permission IN ('*','workflow.manage')) THROW 51000,'Unauthorized catalog change',1;
            SELECT SortOrder,IsActive FROM dbo.{table} WITH(UPDLOCK,HOLDLOCK) WHERE Name=@name;
            """,c,tx);cmd.Parameters.AddRange([P("@actor",actor),P("@name",name)]);
        int beforeOrder;bool beforeActive;await using(var r=await cmd.ExecuteReaderAsync()){if(!await r.ReadAsync())throw new ArgumentException("Neispravan naziv.");beforeOrder=r.GetInt32(0);beforeActive=r.GetBoolean(1);}
        cmd.CommandText=$"UPDATE dbo.{table} SET SortOrder=@order,IsActive=@active WHERE Name=@name";cmd.Parameters.AddRange([P("@order",order),P("@active",active)]);await cmd.ExecuteNonQueryAsync();
        await AdministrationAuditStore.Write(c,tx,new(actor,"workflow.value.update",name,System.Text.Json.JsonSerializer.Serialize(new{Kind=kind,Before=new{Order=beforeOrder,Active=beforeActive},After=new{Order=order,Active=active}})));await tx.CommitAsync();
    }
    internal static async Task RequireCatalog(SqlConnection c,SqlTransaction tx,string kind,string name,string? previous)
    {
        var table=CatalogTable(kind);await using var cmd=new SqlCommand($"SELECT 1 FROM dbo.{table} WITH(HOLDLOCK) WHERE Name=@name AND (IsActive=1 OR Name=@previous)",c,tx);cmd.Parameters.AddRange([P("@name",name),P("@previous",previous)]);if(await cmd.ExecuteScalarAsync() is null)throw new ArgumentException("Neaktivna vrijednost nije dostupna za novu dodjelu.");
    }
}

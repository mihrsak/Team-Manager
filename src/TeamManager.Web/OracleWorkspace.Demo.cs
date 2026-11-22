using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OracleCommand=TeamManager.Web.Data.RelationalCommand;
namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    public async Task ExportSqlRelational(string file)
    {
        if(Enabled)throw new InvalidOperationException("Select the SQL Server source provider before exporting.");
        await using var c=new SqlConnection(configuration.GetConnectionString("TeamManager"));await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
        var document=new JsonObject();
        foreach(var table in Schema.Value.Tables)
        {
            await using var cmd=new SqlCommand("SELECT * FROM dbo.["+table.Name.Replace("]","]]")+"]",c,tx);await using var r=await cmd.ExecuteReaderAsync();var rows=new JsonArray();
            while(await r.ReadAsync()){var row=new JsonObject();for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:JsonSerializer.SerializeToNode(r.GetValue(i));rows.Add(row);}document[table.Name]=rows;
        }
        var sequences=new JsonObject();await using(var cmd=new SqlCommand("SELECT OBJECT_NAME(object_id),CONVERT(bigint,COALESCE(last_value,0)) FROM sys.identity_columns WHERE OBJECT_SCHEMA_NAME(object_id)='dbo'",c,tx))await using(var r=await cmd.ExecuteReaderAsync()){while(await r.ReadAsync())sequences[r.GetString(0)]=r.GetInt64(1);}
        document["_Sequences"]=sequences;var normalized=NormalizeDocument(document);await tx.CommitAsync();await File.WriteAllTextAsync(file,normalized.ToJsonString());Console.WriteLine("SQL Server relational snapshot exported. Keep the file outside source control; it contains private data and account hashes.");
    }
    public async Task CreateLocalDb2DemoSchema(string schema)
    {
        if(!IsDb2||!Regex.IsMatch(schema,"^TMR_DEMO_[A-F0-9]{12}$"))throw new InvalidOperationException("Only a generated local demo schema is supported.");
        // Inspect the configured endpoint before opening a native connection.
        var value=configuration.GetConnectionString("TeamManagerDb2")??"";
        if(!Regex.IsMatch(value,@"(?:^|;)\s*Server\s*=\s*(?:localhost|127\.0\.0\.1):\d+\s*(?:;|$)",RegexOptions.IgnoreCase))throw new InvalidOperationException("Demo schema creation requires a loopback Db2 instance.");
        await using var c=Connect();await c.OpenAsync();await using var cmd=new OracleCommand("CREATE SCHEMA "+schema,c);await cmd.ExecuteNonQueryAsync();Console.WriteLine("Created isolated local Db2 demo schema.");
    }
}

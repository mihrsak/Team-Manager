using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
namespace TeamManager.Web;
public static class DatabaseInstaller
{
    public static readonly string[] SchemaFiles=["001-schema.sql","003-users.sql","004-roles.sql","005-issues.sql","007-workflow.sql","008-projects.sql","009-project-modules.sql"];
    public static async Task Run(string connectionString,bool createDatabase,bool demo)
    {
        var settings=new SqlConnectionStringBuilder(connectionString);
        if(string.IsNullOrWhiteSpace(settings.InitialCatalog)||new[]{"master","tempdb","model","msdb"}.Contains(settings.InitialCatalog,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Configure a dedicated application database.");
        if(createDatabase)
        {
            var name=settings.InitialCatalog;var master=new SqlConnectionStringBuilder(connectionString){InitialCatalog="master"};
            await using var c=new SqlConnection(master.ConnectionString);await c.OpenAsync();
            await using var cmd=new SqlCommand("IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(max)=N'CREATE DATABASE '+QUOTENAME(@name); EXEC sys.sp_executesql @sql; END",c);cmd.Parameters.AddWithValue("@name",name);await cmd.ExecuteNonQueryAsync();
        }
        await using var connection=new SqlConnection(connectionString);await connection.OpenAsync();
        foreach(var file in demo?new[]{"002-demo.sql","006-demo-issues.sql"}:SchemaFiles)
        {
            var path=Path.Combine(AppContext.BaseDirectory,"database",file);
            var sql=await File.ReadAllTextAsync(path);
            foreach(var batch in Regex.Split(sql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            {
                if(string.IsNullOrWhiteSpace(batch))continue;
                await using var cmd=new SqlCommand(batch,connection){CommandTimeout=120};await cmd.ExecuteNonQueryAsync();
            }
            Console.WriteLine($"Applied: {file}");
        }
    }
}

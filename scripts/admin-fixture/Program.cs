using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Oracle.ManagedDataAccess.Client;
using IBM.Data.Db2;
using TeamManager.Web;

if(args.Length!=4)throw new ArgumentException("Supply provider, ignored local source settings, ignored relational snapshot and new ignored output settings path.");
var provider=args[0];if(provider is not ("Oracle" or "Db2"))throw new ArgumentException("Select Oracle or Db2.");
var settings=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[1])).Build();
var output=Path.GetFullPath(args[3]);if(File.Exists(output))throw new InvalidOperationException("Fixture output already exists; never overwrite credentials or another fixture.");
var snapshot=JsonNode.Parse(await File.ReadAllTextAsync(args[2]))!.AsObject();
var keep=new HashSet<string>{"Roles","RolePermissions","IssueTypes","IssueStatuses","IssuePriorities","WorkflowTransitions"};
foreach(var name in snapshot.Select(p=>p.Key).Where(k=>k!="_Sequences"&&!keep.Contains(k)).ToArray())snapshot[name]=new JsonArray();
var user=new AppUser(1,"admin","Isolated administrator","QA",null,"",true);
var hash=new PasswordHasher<AppUser>().HashPassword(user,"admin");
snapshot["AppUsers"]=new JsonArray(new JsonObject{["Id"]=1,["EmployeeNumber"]="admin",["DisplayName"]="Isolated administrator",["JobTitle"]="QA",["OrganizationUnitId"]=null,["PasswordHash"]=hash,["IsAdministrator"]=true,["IsActive"]=true,["SessionVersion"]=0,["CreatedAt"]=DateTime.UtcNow,["DisplayTheme"]="light",["UiLanguage"]="hr",["FailedAttempts"]=0,["LockoutUntil"]=null});
var role=snapshot["RolePermissions"]!.AsArray().First(r=>r!["Permission"]!.GetValue<string>()=="*")!["RoleName"]!.GetValue<string>();
snapshot["UserRoles"]=new JsonArray(new JsonObject{["UserId"]=1,["RoleName"]=role});
snapshot["_Sequences"]=new JsonObject{["AppUsers"]=1};
var connections=new Dictionary<string,string?>{{"TeamManager","Server=127.0.0.1,1;Connect Timeout=1;User Id=unavailable;Password=unavailable;Encrypt=True;TrustServerCertificate=True"}};
string fixtureName;
if(provider=="Db2")
{
 await using var initialize=new DB2Connection(settings.GetConnectionString("TeamManagerDb2"));await initialize.OpenAsync();
 var builder=new DB2ConnectionStringBuilder(settings.GetConnectionString("TeamManagerDb2"));
 if(!builder.Server.StartsWith("localhost:",StringComparison.OrdinalIgnoreCase)&&!builder.Server.StartsWith("127.0.0.1:",StringComparison.Ordinal))throw new InvalidOperationException("Only local Db2 fixtures are supported.");
 fixtureName="TMR_ADMINQA_"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
 await using var create=initialize.CreateCommand();create.CommandText="CREATE SCHEMA "+fixtureName;await create.ExecuteNonQueryAsync();
 builder.CurrentSchema=fixtureName;connections["TeamManagerDb2"]=builder.ConnectionString;
}
else
{
 var builder=new OracleConnectionStringBuilder(settings.GetConnectionString("TeamManagerOracle"));
 if(!builder.DataSource.StartsWith("localhost:",StringComparison.OrdinalIgnoreCase)&&!builder.DataSource.StartsWith("127.0.0.1:",StringComparison.Ordinal))throw new InvalidOperationException("Only local Oracle fixtures are supported.");
 // The parent preparation script creates only this generated empty QA user.
 fixtureName=builder.UserID.ToUpperInvariant();if(!Regex.IsMatch(fixtureName,"^TMR_ADMINQA_[A-F0-9]{12}$"))throw new InvalidOperationException("Oracle requires an independently generated admin QA schema.");
 connections["TeamManagerOracle"]=builder.ConnectionString;
 await using var check=new OracleConnection(builder.ConnectionString);await check.OpenAsync();await using var count=new OracleCommand("SELECT COUNT(*) FROM USER_TABLES",check);if(Convert.ToInt32(await count.ExecuteScalarAsync())!=0)throw new InvalidOperationException("Oracle fixture must be empty.");
}
var document=new {Database=new{Provider=provider},ConnectionStrings=connections,Smtp=new{Enabled=false},Registration=new{Enabled=true},AdminFixture=new{Schema=fixtureName,CreatedAt=DateTime.UtcNow,Isolated=true}};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllTextAsync(output,JsonSerializer.Serialize(document));
var configuration=new ConfigurationBuilder().AddJsonFile(output).Build();var workspace=new OracleWorkspace(configuration);
if(provider=="Db2")
{
 var sanitized=Path.Combine(Path.GetDirectoryName(output)!,"minimal-snapshot.private.json");await File.WriteAllTextAsync(sanitized,snapshot.ToJsonString());await workspace.ImportDb2(sanitized);await workspace.InstallConcurrency();
}
else
{
 await using var connection=new OracleConnection(connections["TeamManagerOracle"]);await connection.OpenAsync();
 await using(var ddl=new OracleCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database/oracle/005-active-workspace.sql")),connection))await ddl.ExecuteNonQueryAsync();
 await using(var insert=new OracleCommand("INSERT INTO TM_ACTIVE_WORKSPACE(ID,REVISION,CONTENT) VALUES(1,1,:content)",connection)){insert.Parameters.Add("content",OracleDbType.Clob).Value=snapshot.ToJsonString();await insert.ExecuteNonQueryAsync();}
 await workspace.MigrateRelational();
}
Console.WriteLine(provider+" isolated administrator fixture installed: "+fixtureName);

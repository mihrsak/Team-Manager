using System.Data;
using IBM.Data.Db2;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using TeamManager.Web;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

if (args.Length is < 2 or > 3) throw new ArgumentException("Supply ignored Db2 settings, an Oracle relational snapshot, and optionally a retained TMR_QA_<12 hex> schema.");
var settings = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[0])).Build();
// Initialize the native provider before its connection-string builder (IBM 10.0.0.400).
await using var initialize = new DB2Connection(settings.GetConnectionString("TeamManagerDb2"));
await initialize.OpenAsync();
var native = new DB2ConnectionStringBuilder(settings.GetConnectionString("TeamManagerDb2"));
if (!native.Server.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase) && !native.Server.StartsWith("127.0.0.1:", StringComparison.Ordinal))
    throw new InvalidOperationException("These tests require an explicitly configured loopback Db2 database.");
var schema = args.Length == 3 ? args[2] : "TMR_QA_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^TMR_QA_[A-F0-9]{12}$")) throw new InvalidOperationException("Only an isolated generated test schema may be reused.");
if (args.Length == 2)
await using (var owner = new DB2Connection(native.ConnectionString))
{
    await owner.OpenAsync();
    await using var create = owner.CreateCommand(); create.CommandText = "CREATE SCHEMA " + schema; await create.ExecuteNonQueryAsync();
}
native.CurrentSchema = schema;
var configuration = new ConfigurationBuilder().AddConfiguration(settings).AddInMemoryCollection(new Dictionary<string,string?>
{
    ["Database:Provider"] = "Db2", ["ConnectionStrings:TeamManagerDb2"] = native.ConnectionString,
    ["ConnectionStrings:TeamManager"] = "Server=127.0.0.1,1;Connect Timeout=1;User Id=unavailable;Password=unavailable;Encrypt=True;TrustServerCertificate=True"
}).Build();
var workspace = new OracleWorkspace(configuration);
if (args.Length == 2) await workspace.ImportDb2(Path.GetFullPath(args[1]));
await workspace.InstallConcurrency();
try { await workspace.ImportDb2(Path.GetFullPath(args[1])); throw new Exception("Importer overwrote an installed workspace."); }
catch(InvalidOperationException error) when(error.Message.StartsWith("Refusing to overwrite an installed Db2 workspace.")) { }
await using var connection = new DB2Connection(native.ConnectionString); await connection.OpenAsync();
async Task<object?> Scalar(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var archive = (string)(await Scalar("SELECT CONTENT FROM TM_ACTIVE_WORKSPACE WHERE ID=1"))!;
try { await Scalar("UPDATE TM_ACTIVE_WORKSPACE SET REVISION=REVISION+1 WHERE ID=1"); throw new Exception("Archive was writable"); }
catch (DB2Exception) { }
var admin = Convert.ToInt32(await Scalar("SELECT MIN(u.\"Id\") FROM \"TMR_AppUsers\" u JOIN \"TMR_UserRoles\" r ON r.\"UserId\"=u.\"Id\" JOIN \"TMR_RolePermissions\" p ON p.\"RoleName\"=r.\"RoleName\" WHERE p.\"Permission\"='*' AND u.\"IsActive\"=1"));
var permissions = new Permissions(configuration, workspace);
var projects = new ProjectStore(configuration, permissions, workspace);
var issues = new IssueStore(configuration, permissions, new WorkflowStore(configuration, workspace), projects, workspace);
var suffix = Guid.NewGuid().ToString("N")[..10];
var project = await projects.Save(admin, null, new ProjectInput { Name="Db2 Željko " + suffix, Description="" });
Check((await projects.List(admin)).Single(p=>p.Id==project).Input.Description=="", "Empty text changed.");
var ids = await Task.WhenAll(Enumerable.Range(0, 12).Select(i=>issues.Save(admin, project, null, new IssueInput { Title="Db2 concurrent " + suffix + " " + i, IsPrivate=false })));
Check(ids.Distinct().Count()==12, "Concurrent inserts reused IDs.");
var first = await issues.Get(admin, ids[0]); Check(first is not null, "Missing issue.");
await using (var rollback=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    await using var invalid=connection.CreateCommand(); invalid.Transaction=(DB2Transaction)rollback;
    invalid.CommandText=$"UPDATE \"TMR_Issues\" SET \"ProjectId\"=-2147483647 WHERE \"Id\"={ids[0]}";
    try { await invalid.ExecuteNonQueryAsync(); throw new Exception("Db2 accepted an orphan issue."); }
    catch(DB2Exception error) when(error.Errors[0].SQLState=="23503") { }
    await rollback.RollbackAsync();
}
Check((await issues.Get(admin,ids[0]))!.ProjectId==project,"Rejected foreign-key change did not roll back.");
async Task<bool> Edit(string title) { try { await issues.Save(admin, project, ids[0], new IssueInput { Title=title, Revision=1, IsPrivate=false }); return true; } catch(InvalidOperationException) { return false; } }
var edits = await Task.WhenAll(Edit("Prva " + suffix), Edit("Second " + suffix));
Check(edits.Count(success=>success)==1, "Concurrent stale revisions were not rejected.");
var users = new UserStore(configuration, new PasswordHasher<AppUser>(), workspace);
await users.Create("db2"+suffix, "Db2 viewer", "", null, "Db2-local-test-2026", actorId:admin);
var viewer = (await users.Find("db2"+suffix))!.Id;
Check(await issues.Get(viewer, ids[0]) is null, "Project leaked before access grant.");
var role="db2Read"+suffix; await permissions.Save(admin,role,"Db2 fixture",["issues.view","issues.comment"],true);
var groups=new GroupStore(configuration,workspace); await groups.Create(admin,"Db2 group "+suffix);
var group=(await groups.List(admin)).Groups.Single(g=>g.Name=="Db2 group "+suffix).Id;
await groups.SetMember(admin,group,viewer,false); await groups.SetRole(admin,group,role,false);
Check(await issues.Get(viewer,ids[0]) is not null,"Group permission missing.");
await groups.SetRole(admin,group,role,true);
Check(await issues.Get(viewer,ids[0]) is null,"Revoked group permission remained effective.");
await groups.SetRole(admin,group,role,false);
await using (var holder = new DB2Connection(native.ConnectionString))
{
    await holder.OpenAsync(); await using var held = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await using var acquire = holder.CreateCommand(); acquire.Transaction=(DB2Transaction)held;
    acquire.CommandText=$"VALUES SYSIBMADM.DBMS_LOCK.REQUEST(SYSIBMADM.DBMS_UTILITY.GET_HASH_VALUE(CURRENT SCHEMA||':TeamManager:group:{group}',0,1073741823),6,10,TRUE)";
    Check(Convert.ToInt32(await acquire.ExecuteScalarAsync())==0,"Could not hold group lock.");
    await using var revoke=holder.CreateCommand(); revoke.Transaction=(DB2Transaction)held;
    revoke.CommandText=$"DELETE FROM \"TMR_UserGroupRoles\" WHERE \"GroupId\"={group}"; await revoke.ExecuteNonQueryAsync();
    var queued=issues.Comment(viewer,ids[0],"Must reject after revocation");
    Check(await Task.WhenAny(queued,Task.Delay(200))!=queued,"Queued writer ignored group lock.");
    await issues.Comment(admin,ids[1],"Independent writer").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked writer committed."); } catch(UnauthorizedAccessException) { }
}
Console.WriteLine("PASS: Db2 group revocation blocks dependent writes, allows independent writers and rechecks committed permissions.");
// This generated fixture schema is isolated from the application and source Oracle schema.
foreach(var table in new[]{"IssueEmailQueue","IssueReminderQueue"}) await Scalar($"UPDATE \"TMR_{table}\" SET \"AvailableAt\"=TIMESTAMP '2100-01-01 00:00:00' WHERE \"CompletedAt\" IS NULL");
await Scalar("UPDATE \"TMR_EmailNotificationPreferences\" SET \"SendDueReminders\"=0");
var preferences=new EmailNotificationStore(configuration,workspace);
var dispatch=new EmailDispatchStore(configuration,issues,workspace);
var recipients=new List<int>();
foreach(var language in new[]{"hr","en"})
{
    var employee="mail"+language+suffix;
    await users.Create(employee,"Db2 mail "+language,"",null,"Db2-local-test-2026",actorId:admin);
    var recipient=(await users.Find(employee))!.Id; recipients.Add(recipient);
    await permissions.Assign(admin,recipient,role,false); await users.SetLanguage(recipient,language);
    await using (var member=connection.CreateCommand())
    {
        member.CommandText="INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(?,?,?)";
        member.Parameters.Add(new DB2Parameter("project",DB2Type.Integer){Value=project});
        member.Parameters.Add(new DB2Parameter("user",DB2Type.Integer){Value=recipient});
        member.Parameters.Add(new DB2Parameter("role",DB2Type.VarChar){Value=role});
        await member.ExecuteNonQueryAsync();
    }
    await preferences.Save(recipient,language+"@example.test",true,true);
}
var mailIssue=await issues.Save(admin,project,null,new IssueInput{Title="Db2 SMTP watched"});
foreach(var recipient in recipients) await issues.SetWatching(recipient,mailIssue,true);
await issues.Comment(admin,mailIssue,"Db2 SMTP event");
await issues.Save(admin,project,null,new IssueInput{Title="Db2 reminder",AssigneeId=recipients[1],DueDate=DateTime.UtcNow.Date,IsPrivate=true});
var claims=(await Task.WhenAll(dispatch.Claim(false,CancellationToken.None),dispatch.Claim(false,CancellationToken.None))).OfType<EmailJob>().ToArray();
Check(claims.Length>0 && claims.Select(j=>(j.UserId,j.Key)).Distinct().Count()==claims.Length,"SMTP workers shared a claim.");
foreach(var job in claims)
{
    await dispatch.Complete(job with {Lease=Guid.NewGuid()},false,0,CancellationToken.None);
    Check(await dispatch.Resolve(job,CancellationToken.None) is not null,"Stale lease completed an owned job.");
    await dispatch.Complete(job,true,0,CancellationToken.None);
}
using var receiver=new TestSmtpReceiver();
var smtpConfiguration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{
    ["Smtp:Enabled"]="true",["Smtp:Host"]="127.0.0.1",["Smtp:Port"]=receiver.Port.ToString(),["Smtp:UseTls"]="false",
    ["Smtp:From"]="sender@example.test",["Smtp:BaseUrl"]="https://localhost:7589",["Smtp:PollSeconds"]="1",["Smtp:RetryBaseSeconds"]="1",["Smtp:ReminderTimeZone"]="UTC"
}).Build();
await using var services=new ServiceCollection().AddSingleton(dispatch).AddLogging().AddLocalization(options=>options.ResourcesPath="Resources").BuildServiceProvider();
using var worker=new SmtpNotificationWorker(smtpConfiguration,services.GetRequiredService<IServiceScopeFactory>(),services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<UiText>>(),Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpNotificationWorker>.Instance);
await worker.StartAsync(CancellationToken.None);
for(var attempt=0;attempt<100 && receiver.Messages.Count<3;attempt++) await Task.Delay(200);
await Task.Delay(1500); await worker.StopAsync(CancellationToken.None);
Check(receiver.Messages.Count==3 && receiver.Rejected==1,"Db2 SMTP retry/reminder delivery failed or duplicated.");
var messages=receiver.Messages.Select(raw=>MimeKit.MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(raw)))).ToArray();
Check(messages.Any(m=>m.Subject?.Contains("Aktivnost")==true) && messages.Any(m=>m.Subject?.Contains("Activity")==true),"Mail lost HR/EN locale.");
Console.WriteLine("PASS: Db2 real loopback SMTP, HR/EN watcher notifications, due reminder, SMTP 451 retry, concurrent claims and stale lease protection.");
Check((string)(await Scalar("SELECT CONTENT FROM TM_ACTIVE_WORKSPACE WHERE ID=1"))! == archive,"Runtime modified archived snapshot.");
Console.WriteLine("PASS: " + (args.Length == 2 ? "fresh Db2 migration and independent row comparison; " : "retained isolated Db2 schema; ") + "immutable archive, idempotent installer, Unicode/empty text, concurrent IDs, stale revisions and group revocation.");
Console.WriteLine("Retained isolated test schema: " + schema);

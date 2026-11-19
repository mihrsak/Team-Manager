using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using TeamManager.Web;

if (args.Length == 0) throw new ArgumentException("Supply an ignored local settings file, optionally followed by --fixture-from <source-settings>.");
var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[0])).AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Database:Provider"] = "Oracle",
    ["ConnectionStrings:TeamManager"] = "Server=127.0.0.1,1;Database=unavailable;User Id=unavailable;Password=unavailable;Connect Timeout=1;Encrypt=True;TrustServerCertificate=True"
}).Build();
var connectionString = configuration.GetConnectionString("TeamManagerOracle")!;
var builder = new OracleConnectionStringBuilder(connectionString);
if (!builder.DataSource.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase) && !builder.DataSource.StartsWith("127.0.0.1:", StringComparison.Ordinal))
    throw new InvalidOperationException("These tests require an explicitly configured loopback Oracle test instance.");
await using var connection = new OracleConnection(connectionString);
await connection.OpenAsync();
var workspace = new OracleWorkspace(configuration);
string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task<object?> Scalar(string sql)
{
    await using var command = new OracleCommand(sql, connection) { InitialLOBFetchSize = -1 };
    return await command.ExecuteScalarAsync();
}
async Task ExpectConstraint(string sql, int number)
{
    await using var transaction = connection.BeginTransaction();
    try
    {
        await using var command = new OracleCommand(sql, connection) { Transaction = transaction };
        await command.ExecuteNonQueryAsync();
        command.CommandText = "SET CONSTRAINTS ALL IMMEDIATE";
        await command.ExecuteNonQueryAsync();
        throw new Exception("Expected Oracle constraint " + number);
    }
    catch (OracleException error) when (error.Number == number) { }
    finally { await transaction.RollbackAsync(); }
}

if (args.Length == 3 && args[1] == "--fixture-from")
{
    // Only this deliberately named, empty disposable schema can receive a fixture.
    Check(builder.UserID.Equals("TMR_MIGRATION_QA", StringComparison.OrdinalIgnoreCase), "Fixture requires the disposable TMR_MIGRATION_QA schema.");
    Check(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM USER_TABLES")) == 0, "Fixture schema must be empty.");
    var sourceConfiguration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[2])).Build();
    await using var source = new OracleConnection(sourceConfiguration.GetConnectionString("TeamManagerOracle"));
    await source.OpenAsync();
    await using var sourceCommand = new OracleCommand("SELECT CONTENT FROM TM_ACTIVE_WORKSPACE WHERE ID=1", source) { InitialLOBFetchSize = -1 };
    var fixture = JsonNode.Parse((string)(await sourceCommand.ExecuteScalarAsync())!)!.AsObject();
    // Exercise nullable empty text, Unicode, binary LOBs and high-water counters.
    var unitId = 1_900_000_001;
    fixture["OrganizationUnits"]!.AsArray().Add(new JsonObject { ["Id"] = unitId, ["Name"] = "Željko 测试 😀" });
    fixture["_Sequences"]!["OrganizationUnits"] = 1_900_000_100L;
    var user = fixture["AppUsers"]!.AsArray()[0]!.AsObject();
    user["JobTitle"] = "";
    user["CreatedAt"] = "2026-11-19T10:11:12.1234567";
    fixture["EmailNotificationPreferences"]!.AsArray().Clear();
    fixture["EmailNotificationPreferences"]!.AsArray().Add(new JsonObject
    {
        ["UserId"] = user["Id"]!.DeepClone(),
        ["Address"] = "",
        ["SendWatchedIssues"] = false,
        ["SendDueReminders"] = false
    });
    await using (var install = new OracleCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database/oracle/005-active-workspace.sql")), connection))
        await install.ExecuteNonQueryAsync();
    var firstIssue = fixture["Issues"]!.AsArray()[0]!.AsObject();
    var originalProject = firstIssue["ProjectId"]!.DeepClone();
    firstIssue["ProjectId"] = -1; // Valid JSON, invalid relational relationship.
    async Task Seed(bool insert)
    {
        await using var command = new OracleCommand(insert
            ? "INSERT INTO TM_ACTIVE_WORKSPACE(ID,REVISION,CONTENT) VALUES(1,17,:content)"
            : "UPDATE TM_ACTIVE_WORKSPACE SET CONTENT=:content WHERE ID=1", connection);
        command.Parameters.Add("content", OracleDbType.Clob).Value = fixture.ToJsonString();
        await command.ExecuteNonQueryAsync();
    }
    await Seed(true);
    try { await workspace.MigrateRelational(); throw new Exception("Invalid source unexpectedly migrated."); }
    catch (OracleException error) when (error.Number == 2291) { }
    Check(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM TMR_WORKSPACE")) == 0, "Failed migration published its marker.");
    Check(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM \"TMR_Projects\"")) == 0, "Failed migration left partial relational data.");
    Console.WriteLine("PASS: an orphan relationship rolls back the entire migration without modifying legacy JSON.");
    firstIssue["ProjectId"] = originalProject;
    await Seed(false);
    await workspace.MigrateRelational();
    Check(Convert.ToInt64(await Scalar("SELECT LAST_VALUE FROM TMR_COUNTERS WHERE TABLE_NAME='OrganizationUnits'")) == 1_900_000_100L, "Identity high-water value lost.");
    Console.WriteLine("PASS: retry after rollback preserves Unicode, empty/null text, timestamp precision, binary content and high-water counters.");
}

var revision = Convert.ToInt64(await Scalar("SELECT REVISION FROM TMR_WORKSPACE WHERE ID=1"));
var archived = (string)(await Scalar("SELECT CONTENT FROM TM_ACTIVE_WORKSPACE WHERE ID=1"))!;
var archiveHash = SHA256.HashData(Encoding.UTF8.GetBytes(archived));
await workspace.MigrateRelational();
Check(revision == Convert.ToInt64(await Scalar("SELECT REVISION FROM TMR_WORKSPACE WHERE ID=1")), "Repeated migration altered active state.");
await ExpectConstraint("UPDATE TM_ACTIVE_WORKSPACE SET REVISION=REVISION+1 WHERE ID=1", 20001);
await ExpectConstraint("UPDATE \"TMR_Issues\" SET \"ProjectId\"=-1 WHERE \"Id\"=(SELECT MIN(\"Id\") FROM \"TMR_Issues\")", 2291);
await ExpectConstraint("INSERT INTO \"TMR_IssueAttachments\" (\"Id\",\"IssueId\",\"AuthorId\",\"FileName\",\"Content\",\"CreatedAt\") SELECT -1,MIN(\"Id\"),MIN(\"CreatedBy\"),'empty',EMPTY_BLOB(),SYS_EXTRACT_UTC(SYSTIMESTAMP) FROM \"TMR_Issues\"", 20002);
Console.WriteLine("PASS: migration is idempotent; legacy writes, orphan references and empty attachments are rejected by Oracle.");

// Independently compare original fields through typed SQL reads, including BLOB bytes.
var schema = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database/oracle/relational-schema.json")))!;
var document = JsonNode.Parse(archived)!.AsObject();
var checkedRows = 0;
foreach (var table in schema["tables"]!.AsArray())
{
    var name = table!["name"]!.GetValue<string>();
    var columns = table["columns"]!.AsArray();
    var keys = table["keys"]!.AsArray().Single(k => k!["primary"]!.GetValue<bool>())!["columns"]!.AsArray();
    foreach (var expected in document[name]!.AsArray().OfType<JsonObject>())
    {
        var predicate = string.Join(" AND ", keys.Select((k, i) => Q(k!.GetValue<string>()) + "=:k" + i));
        await using var query = new OracleCommand($"SELECT \"__EMPTY\",{string.Join(',', columns.Select(c => Q(c!["name"]!.GetValue<string>())))} FROM {Q("TMR_" + name)} WHERE {predicate}", connection) { InitialLOBFetchSize = -1 };
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i]!.GetValue<string>();
            var type = columns.Single(c => c!["name"]!.GetValue<string>() == key)!["type"]!.GetValue<string>();
            var node = expected[key]!;
            var parameter = query.Parameters.Add("k" + i, type switch { "int" => OracleDbType.Int32, "bigint" => OracleDbType.Int64, "binary" => OracleDbType.Raw, "date" => OracleDbType.Date, _ => OracleDbType.NVarchar2 });
            parameter.Value = type switch { "int" => node.GetValue<int>(), "bigint" => node.GetValue<long>(), "binary" => Convert.FromBase64String(node.GetValue<string>()), "date" => node.GetValue<DateTime>(), _ => node.GetValue<string>() };
        }
        await using var reader = await query.ExecuteReaderAsync();
        Check(await reader.ReadAsync(), "Missing imported row in " + name);
        var mask = reader.GetInt64(0);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i]!;
            var field = column["name"]!.GetValue<string>();
            if (!expected.ContainsKey(field) || name == "AppUsers" && new[] { "UiLanguage", "DisplayTheme", "FailedAttempts", "LockoutUntil" }.Contains(field)) continue;
            var node = expected[field];
            var empty = (mask & (1L << i)) != 0;
            if (node is null) { Check(reader.IsDBNull(i + 1) && !empty, "NULL changed: " + name + "." + field); continue; }
            var type = column["type"]!.GetValue<string>();
            var equal = type switch
            {
                "int" or "bigint" or "decimal" => !reader.IsDBNull(i + 1) && decimal.Parse(node.ToJsonString(), CultureInfo.InvariantCulture) == reader.GetDecimal(i + 1),
                "bit" => !reader.IsDBNull(i + 1) && node.GetValue<bool>() == (reader.GetInt32(i + 1) != 0),
                "date" or "datetime2" => !reader.IsDBNull(i + 1) && node.GetValue<DateTime>().Ticks == reader.GetDateTime(i + 1).Ticks,
                "binary" or "varbinary" => !reader.IsDBNull(i + 1) && Convert.FromBase64String(node.GetValue<string>()).SequenceEqual((byte[])reader.GetValue(i + 1)),
                _ => node.GetValue<string>() == "" ? reader.IsDBNull(i + 1) && empty : !reader.IsDBNull(i + 1) && node.GetValue<string>() == reader.GetString(i + 1)
            };
            Check(equal, "Imported value changed: " + name + "." + field);
        }
        checkedRows++;
    }
}
Console.WriteLine($"PASS: {checkedRows} archived records independently compared with typed relational columns (excluding live UI preferences and login counters).");

var permissions = new Permissions(configuration, workspace);
var projects = new ProjectStore(configuration, permissions, workspace);
var admin = Convert.ToInt32(await Scalar("SELECT MIN(u.\"Id\") FROM \"TMR_AppUsers\" u JOIN \"TMR_UserRoles\" r ON r.\"UserId\"=u.\"Id\" JOIN \"TMR_RolePermissions\" p ON p.\"RoleName\"=r.\"RoleName\" WHERE p.\"Permission\"='*' AND u.\"IsActive\"=1"));
var project = await projects.Save(admin, null, new ProjectInput { Name = "Relational persistence " + Guid.NewGuid().ToString("N"), Description = "" });
Check((await projects.List(admin)).Single(p => p.Id == project).Input.Description == "", "Empty description did not round-trip.");
Check(Convert.ToInt64(await Scalar($"SELECT BITAND(\"__EMPTY\",4) FROM \"TMR_Projects\" WHERE \"Id\"={project}")) != 0, "Empty text mask missing.");
Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_ProjectMembers\" WHERE \"ProjectId\"={project} AND \"UserId\"={admin}")) == 1, "Project membership not committed relationally.");
var archiveAfter = (string)(await Scalar("SELECT CONTENT FROM TM_ACTIVE_WORKSPACE WHERE ID=1"))!;
Check(archiveHash.SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(archiveAfter))), "Runtime modified JSON archive.");
Console.WriteLine("PASS: active project writes and memberships persist in relational tables with SQL Server unavailable, while archived JSON remains unchanged.");

// Runtime query budgets: these instruments count rows returned to the application,
// including lazy reads used inside a write transaction. No workspace snapshot is allowed.
var counts = new Dictionary<string, long>();
long snapshots = 0;
using var listener = new System.Diagnostics.Metrics.MeterListener();
listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == "TeamManager.Oracle") meterListener.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
{
    if (instrument.Name == "oracle.full_workspace_reads") snapshots += value;
    if (instrument.Name == "oracle.selected_rows")
        foreach (var tag in tags) if (tag.Key == "query")
        {
            var key = Convert.ToString(tag.Value)!;
            counts[key] = counts.GetValueOrDefault(key) + value;
        }
});
listener.Start();
var workflow = new WorkflowStore(configuration, workspace);
var issues = new IssueStore(configuration, permissions, workflow, projects, workspace);
var input = new IssueInput { Title = "SQL literal %_ " + Guid.NewGuid().ToString("N"), Description = "Željko query test", AssigneeId = admin, StartDate = new(2026, 11, 1), DueDate = new(2026, 11, 30), IsPrivate = false };
var issueId = await issues.Save(admin, project, null, input);
counts.Clear();
Check((await issues.Get(admin, issueId))?.Input.Title == input.Title, "Targeted detail missing.");
Check(counts.GetValueOrDefault("IssueDetail") == 1 && counts.Count == 1, "Detail fetched unrelated data.");
counts.Clear();
input.Revision = 1;
input.Title += " updated";
await issues.Save(admin, project, issueId, input);
Check(counts.GetValueOrDefault("Issues") == 1, "Saving one issue fetched unrelated issues.");
Check(!counts.ContainsKey("IssueAttachments") && !counts.ContainsKey("ProjectDocuments") && !counts.ContainsKey("IssuePrivateNotes"), "Saving an issue fetched unrelated LOBs or notes.");
Check((await issues.List(admin, project, null, "%_", new(2026, 11, 1), new(2026, 11, 30), false, admin, true)).Single().Id == issueId, "SQL filters changed literal wildcard or date/assignee semantics.");
Check((await issues.List(admin, project, null, null, new(2026, 12, 1))).Count == 0, "Date filter ignored.");
await issues.SetWatching(admin, issueId, true);
Check((await issues.List(admin, project, null, null, watchedOnly: true)).Single().Id == issueId, "Watched filter ignored.");
await issues.SetWatching(admin, issueId, false);
Check((await issues.List(admin, project, null, null, watchedOnly: true)).Count == 0, "Removed watcher remained visible.");
Check(snapshots == 0, "Runtime loaded a full workspace snapshot.");
Console.WriteLine("PASS: detail returns one row; save reads one issue and no unrelated LOBs; SQL search/date/assignee/open/watched filters retain behavior without workspace snapshots.");

var suffix = Guid.NewGuid().ToString("N")[..10];
var users = new UserStore(configuration, new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>(), workspace);
await users.Create("sql" + suffix, "SQL viewer " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
var viewer = (await users.Find("sql" + suffix))!.Id;
var readRole = "sqlRead" + suffix;
var manageRole = "sqlManage" + suffix;
await permissions.Save(admin, readRole, "SQL fixture", ["issues.view"], true);
await permissions.Save(admin, manageRole, "SQL fixture", ["projects.manage"], true);
Check(await issues.Get(viewer, issueId) is null, "Private project leaked before membership.");
async Task FixtureSql(string sql, params (string Name, object Value)[] values)
{
    await using var transaction = connection.BeginTransaction();
    await using var command = new OracleCommand(sql, connection) { Transaction = transaction, BindByName = true };
    foreach (var (name, value) in values) command.Parameters.Add(new OracleParameter(name, value));
    await command.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
}
var group = -Random.Shared.Next(1, int.MaxValue);
await FixtureSql("INSERT INTO \"TMR_UserGroups\" (\"Id\",\"Name\") VALUES(:id,:name)", ("id", group), ("name", "SQL fixture " + suffix));
await FixtureSql("INSERT INTO \"TMR_UserGroupMembers\" (\"GroupId\",\"UserId\") VALUES(:groupId,:userId)", ("groupId", group), ("userId", viewer));
await FixtureSql("INSERT INTO \"TMR_UserGroupRoles\" (\"GroupId\",\"RoleName\") VALUES(:groupId,:role)", ("groupId", group), ("role", readRole));
Check((await permissions.Effective(viewer)).Contains("issues.view") && await issues.Get(viewer, issueId) is not null, "Group role not applied in SQL.");
await FixtureSql("DELETE FROM \"TMR_UserGroupRoles\" WHERE \"GroupId\"=:groupId", ("groupId", group));
Check(await issues.Get(viewer, issueId) is null, "Group revocation did not take effect.");
await FixtureSql("INSERT INTO \"TMR_Teams\" (\"Id\",\"Name\",\"CreatedAt\") VALUES(:id,:name,SYS_EXTRACT_UTC(SYSTIMESTAMP))", ("id", group), ("name", "SQL team " + suffix));
await FixtureSql("INSERT INTO \"TMR_Members\" (\"Id\",\"TeamId\",\"UserId\",\"Name\",\"Role\") VALUES(:id,:id,:userId,:name,:role)", ("id", group), ("userId", viewer), ("name", suffix), ("role", readRole));
await FixtureSql("INSERT INTO \"TMR_TeamMemberRoles\" (\"MemberId\",\"RoleName\") VALUES(:id,:role)", ("id", group), ("role", manageRole));
Check((await permissions.Effective(viewer, "team", group)).ToHashSet().SetEquals(["issues.view", "projects.manage"]), "Team primary/additional roles changed.");
Check((await permissions.Effective(viewer)).Count == 0, "Team-scoped rights leaked globally.");
await FixtureSql("INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(:projectId,:userId,:role)", ("projectId", project), ("userId", viewer), ("role", readRole));
var child = await projects.Save(admin, null, new ProjectInput { Name = "SQL child " + suffix, ParentId = project, InheritParentRoles = true });
var childIssue = await issues.Save(admin, child, null, new IssueInput { Title = "SQL inherited " + suffix, IsPrivate = false });
var privateIssue = await issues.Save(admin, child, null, new IssueInput { Title = "SQL private " + suffix, IsPrivate = true });
Check(await issues.Get(viewer, childIssue) is not null && await issues.Get(viewer, privateIssue) is null, "Inherited issue visibility changed.");
await FixtureSql("INSERT INTO \"TMR_ProjectMemberRoles\" (\"ProjectId\",\"UserId\",\"RoleName\") VALUES(:projectId,:userId,:role)", ("projectId", project), ("userId", viewer), ("role", manageRole));
Check(await issues.Get(viewer, privateIssue) is not null, "Additional inherited manager role not applied.");
var childInput = (await projects.List(admin)).Single(p => p.Id == child).Input;
childInput.InheritParentRoles = false;
await projects.Save(admin, child, childInput);
Check(await issues.Get(viewer, childIssue) is null, "Inheritance stop ignored.");
await permissions.ManageAccount(admin, viewer, false, null);
Check((await permissions.Effective(viewer, "project", project)).Count == 0 && !await issues.Can(viewer, project, "issues.view"), "Inactive account retained permissions.");
Check(snapshots == 0, "Runtime loaded a workspace snapshot during permission changes.");
Console.WriteLine("PASS: SQL permissions retain group revocation, team scope, additional/inherited roles, inheritance stops, private visibility and inactive-account checks.");
var closedInput = (await projects.List(admin)).Single(p => p.Id == project).Input;
closedInput.State = "Closed";
await projects.Save(admin, project, closedInput);
Check(await issues.Can(admin, project, "issues.view") && !await issues.Can(admin, project, "issues.edit"), "Closed-project read/write rules changed.");
closedInput = (await projects.List(admin)).Single(p => p.Id == project).Input;
await projects.SetModules(admin, project, false, false, closedInput.Revision);
Check(!await issues.Can(admin, project, "issues.view") && await issues.Get(admin, issueId) is null, "Disabled module remained readable.");
Check(snapshots == 0, "Module operation loaded the workspace.");
Console.WriteLine("PASS: closed-project and disabled-module SQL checks match mutation authorization.");

// Hold real Oracle locks on a separate connection. A blocked write to one issue
// must not stall an unrelated issue, even while the old global rows are locked.
var fineProject = await projects.Save(admin, null, new ProjectInput { Name = "Fine locks " + suffix });
var fineA = await issues.Save(admin, fineProject, null, new IssueInput { Title = "Lock A " + suffix, IsPrivate = false });
var fineB = await issues.Save(admin, fineProject, null, new IssueInput { Title = "Lock B " + suffix, IsPrivate = false });
await using var holder = new OracleConnection(connectionString);
await holder.OpenAsync();
async Task Hold(OracleTransaction transaction, string resource, int mode)
{
    await using var command = new OracleCommand("""
        BEGIN :result:=DBMS_LOCK.REQUEST(DBMS_UTILITY.GET_HASH_VALUE(SYS_CONTEXT('USERENV','CURRENT_SCHEMA')||':TeamManager:'||:resource,0,1073741823),:mode,1,TRUE); END;
        """, holder) { Transaction = transaction, BindByName = true };
    command.Parameters.Add("resource", OracleDbType.Varchar2).Value = resource;
    command.Parameters.Add("mode", OracleDbType.Int32).Value = mode;
    var result = command.Parameters.Add("result", OracleDbType.Int32);
    result.Direction = ParameterDirection.Output;
    await command.ExecuteNonQueryAsync();
    Check(result.Value.ToString() == "0", "Fixture could not acquire Oracle lock.");
}
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "policy", 4);
    await Hold(held, "issue:" + fineA, 6);
    await using (var legacy = new OracleCommand("SELECT ID FROM TMR_WORKSPACE WHERE ID=1 FOR UPDATE", holder) { Transaction = held }) await legacy.ExecuteScalarAsync();
    await using (var legacy = new OracleCommand("SELECT LAST_VALUE FROM TMR_COUNTERS WHERE TABLE_NAME='IssueEvents' FOR UPDATE", holder) { Transaction = held }) await legacy.ExecuteScalarAsync();
    var waiting = issues.Comment(admin, fineA, "Held issue");
    await issues.Comment(admin, fineB, "Independent issue").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!waiting.IsCompleted, "Same-issue write ignored its lock.");
    var policyChange = permissions.Assign(admin, viewer, readRole, true);
    await policyChange.WaitAsync(TimeSpan.FromSeconds(5));
    await held.RollbackAsync();
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    await policyChange.WaitAsync(TimeSpan.FromSeconds(5));
}
Console.WriteLine("PASS: unrelated issue completes while another issue, legacy workspace row and legacy counter are locked; unrelated account-role changes also complete.");

var concurrentIds = await Task.WhenAll(Enumerable.Range(0, 12).Select(n => issues.Save(admin, fineProject, null, new IssueInput { Title = $"Concurrent {suffix} {n}", IsPrivate = false })));
Check(concurrentIds.Distinct().Count() == 12, "Parallel inserts reused an Oracle sequence ID.");
var beforeRetry = await issues.Save(admin, fineProject, null, new IssueInput { Title = "Before installer retry " + suffix });
await workspace.InstallConcurrency();
var afterRetry = await issues.Save(admin, fineProject, null, new IssueInput { Title = "After installer retry " + suffix });
Check(afterRetry > beforeRetry, "Installer reset an active Oracle sequence.");
async Task<bool> BulkAttempt(int first, int second, string priority)
{
    try { await issues.UpdateMany(admin, new Dictionary<int, int> { { first, 1 }, { second, 1 } }, null, priority); return true; }
    catch (InvalidOperationException error) when (error.Message.StartsWith("Zadatak je")) { return false; }
}
var bulkResults = await Task.WhenAll(BulkAttempt(fineA, fineB, "Visok"), BulkAttempt(fineB, fineA, "Nizak"));
Check(bulkResults.Count(x => x) == 1, "Reversed bulk edits did not resolve as one atomic commit and one stale revision.");
Check((await issues.Get(admin, fineA))!.Input.Priority == (await issues.Get(admin, fineB))!.Input.Priority, "Bulk edit committed only part of the update.");
Console.WriteLine("PASS: parallel inserts use unique IDs, installer retries preserve sequence progress, reversed bulk edits avoid deadlock and partial commits.");

var contributor = "sqlCon" + suffix;
await users.Create(contributor, "SQL contributor " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
var writer = (await users.Find(contributor))!.Id;
var writerRole = "sqlWrite" + suffix;
await permissions.Save(admin, writerRole, "SQL concurrency fixture", ["issues.view", "issues.comment", "time.log"], true);
await permissions.Assign(admin, writer, writerRole, false);
var eventCount = Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_IssueEvents\" WHERE \"IssueId\"={fineB}"));
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "policy", 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = writer;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = writerRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(writer, fineB, "Must not commit after revocation");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Queued write bypassed policy lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked write was accepted."); }
    catch (UnauthorizedAccessException) { }
}
Check(eventCount == Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_IssueEvents\" WHERE \"IssueId\"={fineB}")), "Revoked write left an event.");
await permissions.Assign(admin, writer, writerRole, false);
Console.WriteLine("PASS: queued writes recheck committed permissions after policy-lock release and cannot commit using revoked rights.");

// SQL aggregation and limits must apply after visibility/time-module filtering.
var hidden = await issues.Save(admin, fineProject, null, new IssueInput { Title = "Hidden report issue " + suffix, IsPrivate = true });
await issues.LogTime(admin, fineB, new(2026, 11, 20), 1.25m, "Public time", "SQL QA");
await issues.LogTime(admin, fineB, new(2026, 11, 20), 1.25m, "Public time", "SQL QA");
await issues.LogTime(admin, hidden, new(2026, 11, 20), 7.5m, "Private time", "SQL QA");
await issues.LogTime(writer, fineB, new(2026, 11, 20), 1.75m, "Contributor time", "SQL QA");
counts.Clear();
var publicTime = await issues.TimeReport(writer, [fineProject], new(2026, 11, 20), new(2026, 11, 20), null, "sql qa");
Check(publicTime.Sum(x => x.Hours) == 4.25m, "Time aggregate leaked a private issue or lost public hours.");
Check((await issues.TimeReport(admin, [fineProject], new(2026, 11, 20), new(2026, 11, 20), null, "SQL QA")).Sum(x => x.Hours) == 11.75m, "Admin time aggregate lost private hours.");
Check((await issues.TimeReport(writer, [fineProject], null, null, contributor, null)).Single().Hours == 1.75m, "Person filter changed aggregate.");
Check(counts.Keys.All(k => k == "TimeReport") && counts.GetValueOrDefault("TimeReport") == 5, "Time report fetched raw rows rather than aggregates.");
var roadmap = new RoadmapStore(configuration, projects, workspace);
await roadmap.Save(admin, fineProject, new RoadmapItem(0, "milestone", "SQL report " + suffix, "", null, false, 0));
var milestone = (await roadmap.List(fineProject)).Single();
foreach (var selected in new[] { fineB, hidden })
{
    var changed = (await issues.Get(admin, selected))!.Input;
    changed.RoadmapItemId = milestone.Id;
    await issues.Save(admin, fineProject, selected, changed);
}

counts.Clear();
Check((await roadmap.Progress(writer, fineProject))[milestone.Id].Sum(x => x.Count) == 1, "Roadmap count leaked private issues.");
Check((await roadmap.Progress(admin, fineProject))[milestone.Id].Sum(x => x.Count) == 2, "Admin roadmap count lost private issues.");
Check(counts.Keys.All(k => k == "RoadmapProgress") && counts["RoadmapProgress"] == 2, "Roadmap fetched raw issues rather than status aggregates.");
Check((await roadmap.Search([fineProject], "SQL report " + suffix)).Single().Id == milestone.Id, "SQL roadmap search changed project filtering.");
await issues.SetWatching(writer, fineB, true);
for (var n = 0; n < 60; n++) await issues.Comment(admin, fineB, $"SQL report {n}");
await issues.Comment(admin, hidden, "Hidden latest report event");
counts.Clear();
var recent = await issues.RecentActivity(writer, [fineProject]);
var personal = new IssuePersonalStore(configuration, issues, workspace);
var notifications = await personal.Notifications(writer);
var audit = new AdministrationAuditStore(configuration, workspace);
var audits = await audit.Recent();
Check(recent.Count == 50 && recent.All(e => e.IssueId != hidden), "Activity limit or visibility changed.");
Check(notifications.Count == 50 && notifications.All(n => n.IssueId == fineB), "Notification limit or visibility changed.");
Check(audits.Count <= 100 && counts.GetValueOrDefault("AdministrationAudit") == audits.Count, "Audit limit was not applied in SQL.");
Check(counts.Keys.All(k => k is "RecentActivity" or "Notifications" or "AdministrationAudit") && counts["RecentActivity"] == 50 && counts["Notifications"] == 50, "Helper reports fetched unrelated rows or LOBs.");
Check(await personal.MarkRead(writer, notifications[0].EventId), "Notification could not be marked read.");
Check((await personal.Notifications(writer)).All(n => n.EventId != notifications[0].EventId), "Read notification displaced newer unread notifications.");
var moduleInput = (await projects.List(admin)).Single(p => p.Id == fineProject).Input;
await projects.SetModules(admin, fineProject, true, false, moduleInput.Revision);
Check((await issues.TimeReport(writer, [fineProject], null, null, null, null)).Count == 0, "Time report ignored disabled time module.");
Check((await issues.RecentActivity(writer, [fineProject])).All(e => e.Kind != "time"), "Activity exposed time events with time module disabled.");
await permissions.ManageAccount(admin, writer, false, null);
Check((await personal.Notifications(writer)).Count == 0 && (await issues.RecentActivity(writer, [fineProject])).Count == 0, "Reports exposed data to inactive account.");
Check(snapshots == 0, "Concurrency/report flow loaded the workspace.");
Console.WriteLine("PASS: SQL reports enforce aggregation, filters, private visibility, time-module flags, inactive accounts and 50/100-row limits without fetching raw workspaces or unrelated LOBs.");

var busyMessage = "Spremanje je trenutačno zauzeto. Pokušaj ponovno.";
var resources = new System.Resources.ResourceManager("TeamManager.Web.Resources.UiText", typeof(UiText).Assembly);
Check(resources.GetString(busyMessage, CultureInfo.GetCultureInfo("en")) == "Saving is temporarily busy. Please try again.", "English lock-timeout message missing.");
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "issue:" + fineA, 6);
    try { await issues.Comment(admin, fineA, "Must time out"); throw new Exception("Expected lock timeout."); }
    catch (InvalidOperationException error) when (error.Message == busyMessage) { }
    await held.RollbackAsync();
}
await issues.Comment(admin, fineA, "Lock released after timeout").WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine("PASS: lock timeout reports a localized retry message and releases transactional locks for a successful retry.");
if (Environment.GetEnvironmentVariable("TEAM_MANAGER_ORACLE_URL") is { Length: > 0 } httpAddress)
{
    var address = new Uri(httpAddress);
    Check(address.IsLoopback && address.Scheme == "https", "HTTP verification requires loopback HTTPS.");
    using var http = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = address };
    string Token(string html) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    var login = await http.GetStringAsync("/Login");
    using (var response = await http.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "EmployeeNumber", "admin" }, { "Password", Environment.GetEnvironmentVariable("TEAM_MANAGER_QA_PASSWORD") ?? "admin" }, { "__RequestVerificationToken", Token(login) } }))) response.EnsureSuccessStatusCode();
    foreach (var language in new[] { "hr", "en" })
    {
        var form = await http.GetStringAsync("/Issue?id=" + fineA);
        using (var response = await http.PostAsync("/Profile?handler=Language", new FormUrlEncodedContent(new Dictionary<string, string> { { "language", language }, { "__RequestVerificationToken", Token(form) } }))) response.EnsureSuccessStatusCode();
        form = await http.GetStringAsync("/Issue?id=" + fineA);
        await using var held = holder.BeginTransaction();
        await Hold(held, "issue:" + fineA, 6);
        using var responseBusy = await http.PostAsync("/Issue?id=" + fineA + "&handler=Comment", new FormUrlEncodedContent(new Dictionary<string, string> { { "text", "HTTP timeout" }, { "__RequestVerificationToken", Token(form) } }));
        Check(responseBusy.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && responseBusy.Headers.RetryAfter is not null, "Busy handler did not return a retryable 503.");
        Check(await responseBusy.Content.ReadAsStringAsync() == (language == "hr" ? busyMessage : "Saving is temporarily busy. Please try again."), "HTTP retry message locale changed.");
        await held.RollbackAsync();
    }
    Console.WriteLine("PASS: unhandled HTTP lock timeouts return localized HR/EN 503 responses with Retry-After rather than server errors.");
}


// Administrative scopes: unrelated targets must progress, while edits affecting
// the actor of a queued write must remain mutually exclusive.
var targetNameA = "sqlAdminA" + suffix;
var targetNameB = "sqlAdminB" + suffix;
await users.Create(targetNameA, "Scoped A " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
await users.Create(targetNameB, "Scoped B " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
var targetA = (await users.Find(targetNameA))!.Id;
var targetB = (await users.Find(targetNameB))!.Id;
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + targetA, 6);
    var blocked = permissions.Assign(admin, targetA, readRole, false);
    await permissions.Assign(admin, targetB, readRole, false).WaitAsync(TimeSpan.FromSeconds(5));
    await users.SetTheme(targetB, "dark").WaitAsync(TimeSpan.FromSeconds(5));
    Check((await users.Authenticate(targetNameB, "SQL-test-password-2026"))?.Id == targetB, "Unrelated account login stalled or failed.");
    Check(!blocked.IsCompleted, "Target-specific administration ignored its identity lock.");
    await held.RollbackAsync();
    await blocked.WaitAsync(TimeSpan.FromSeconds(5));
}
var parallelProject = await projects.Save(admin, null, new ProjectInput { Name = "Scoped roadmap " + suffix });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, $"project-data:{fineProject}:roadmap", 6);
    var blocked = roadmap.Save(admin, fineProject, new RoadmapItem(0, "milestone", "Blocked " + suffix, "", null, false, 0));
    await roadmap.Save(admin, parallelProject, new RoadmapItem(0, "milestone", "Independent " + suffix, "", null, false, 0)).WaitAsync(TimeSpan.FromSeconds(5));
    await permissions.Assign(admin, targetB, readRole, true).WaitAsync(TimeSpan.FromSeconds(5));
    Check(!blocked.IsCompleted, "Project administration ignored its entity lock.");
    await held.RollbackAsync();
    await blocked.WaitAsync(TimeSpan.FromSeconds(5));
}
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "administrators", 6);
    var guarded = permissions.Assign(admin, targetA, readRole, true);
    await users.SetTheme(targetB, "light").WaitAsync(TimeSpan.FromSeconds(5));
    await roadmap.Save(admin, parallelProject, new RoadmapItem(0, "milestone", "Guard independent " + suffix, "", null, false, 0)).WaitAsync(TimeSpan.FromSeconds(5));
    Check(await Task.WhenAny(guarded, Task.Delay(200)) != guarded, "Last-administrator guard was bypassed.");
    await held.RollbackAsync();
    await guarded.WaitAsync(TimeSpan.FromSeconds(5));
}
Console.WriteLine("PASS: independent account administration/login and project roadmaps run in parallel; same-target edits and the last-administrator check remain serialized without blocking unrelated work.");

await permissions.Assign(admin, targetA, writerRole, false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + targetA, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:target AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("target", OracleDbType.Int32).Value = targetA;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = writerRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(targetA, fineB, "Must fail after scoped revoke");
    await users.SetTheme(targetB, "dark").WaitAsync(TimeSpan.FromSeconds(5));
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Issue write bypassed identity-policy lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Scoped role revocation was ignored."); } catch (UnauthorizedAccessException) { }
}
await FixtureSql("INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(:project,:target,:role)", ("project", fineProject), ("target", targetA), ("role", writerRole));
var inheritedProject = await projects.Save(admin, null, new ProjectInput { Name = "Scoped inherited " + suffix, ParentId = fineProject, InheritParentRoles = true });
var inheritedIssue = await issues.Save(admin, inheritedProject, null, new IssueInput { Title = "Scoped inherited write", IsPrivate = false });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + targetA, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_ProjectMembers\" WHERE \"ProjectId\"=:project AND \"UserId\"=:target", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("project", OracleDbType.Int32).Value = fineProject;
        revoke.Parameters.Add("target", OracleDbType.Int32).Value = targetA;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(targetA, inheritedIssue, "Must fail after inherited membership revoke");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Inherited membership reader ignored identity lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Inherited revocation was ignored."); } catch (UnauthorizedAccessException) { }
}
Console.WriteLine("PASS: queued writes recheck global and inherited membership rights after target-user revocation, while unrelated users continue working.");

// Explicit assignee dependencies must be frozen even when the actor differs.
await FixtureSql("INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(:project,:target,:role)", ("project", inheritedProject), ("target", targetA), ("role", readRole));
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + targetA, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_ProjectMembers\" WHERE \"ProjectId\"=:project AND \"UserId\"=:target", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("project", OracleDbType.Int32).Value = inheritedProject;
        revoke.Parameters.Add("target", OracleDbType.Int32).Value = targetA;
        await revoke.ExecuteNonQueryAsync();
    }
    var changed = (await issues.Get(admin, inheritedIssue))!.Input;
    changed.AssigneeId = targetA;
    var queued = issues.Save(admin, inheritedProject, inheritedIssue, changed);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Assignee membership dependency was not locked.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Removed assignee membership was accepted."); } catch (ArgumentException) { }
}
Check((await issues.Get(admin, inheritedIssue))!.Input.AssigneeId is null, "Failed assignee edit partially committed.");
Console.WriteLine("PASS: assignee membership is rechecked under its own identity lock and invalid assignment changes roll back.");

if (builder.UserID.Equals("TMR_MIGRATION_QA", StringComparison.OrdinalIgnoreCase))
{
    var fullRole = (string)(await Scalar("SELECT r.\"RoleName\" FROM \"TMR_UserRoles\" r JOIN \"TMR_RolePermissions\" p ON p.\"RoleName\"=r.\"RoleName\" WHERE r.\"UserId\"=" + admin + " AND p.\"Permission\"='*' FETCH FIRST 1 ROW ONLY"))!;
    await permissions.Assign(admin, targetA, fullRole, false);
    await permissions.Assign(admin, targetB, fullRole, false);
    var activeBefore = new List<int>();
    await using (var activeUsers = new OracleCommand($"SELECT \"Id\" FROM \"TMR_AppUsers\" WHERE \"IsActive\"=1 AND \"Id\" NOT IN ({targetA},{targetB})", connection))
    await using (var activeRows = await activeUsers.ExecuteReaderAsync())
        while (await activeRows.ReadAsync()) activeBefore.Add(activeRows.GetInt32(0));
    try
    {
        await FixtureSql($"UPDATE \"TMR_AppUsers\" SET \"IsActive\"=0 WHERE \"IsActive\"=1 AND \"Id\" NOT IN ({targetA},{targetB})");
        async Task<bool> RevokeSelf(int target)
        {
            try { await permissions.Assign(target, target, fullRole, true); return true; }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Mora ostati")) { return false; }
        }
        var result = await Task.WhenAll(RevokeSelf(targetA), RevokeSelf(targetB));
        Check(result.Count(x => x) == 1, "Concurrent administrator revocations did not preserve one full administrator.");
    }
    finally { foreach (var id in activeBefore) await FixtureSql("UPDATE \"TMR_AppUsers\" SET \"IsActive\"=1 WHERE \"Id\"=:id", ("id", id)); }
    Console.WriteLine("PASS: concurrent self-revocations in the isolated schema cannot remove the last active full administrator.");
}
var managerName = "sqlManager" + suffix;
await users.Create(managerName, "Scoped manager " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
var manager = (await users.Find(managerName))!.Id;
var managerRole = "sqlManager" + suffix;
await permissions.Save(admin, managerRole, "Scoped authority", ["roles.manage"], true);
await permissions.Assign(admin, manager, managerRole, false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + manager, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:target AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("target", OracleDbType.Int32).Value = manager;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = managerRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = permissions.Assign(manager, targetB, readRole, false);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Administrative actor bypassed identity lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked administrator changed another account."); } catch (UnauthorizedAccessException) { }
}
var duplicateName = "sqlDup" + suffix;
async Task<bool> CreateDuplicate(string employee)
{
    try { await users.Create(employee, "Duplicate fixture", "", null, "SQL-test-password-2026", actorId: admin); return true; }
    catch (ArgumentException) { return false; }
}
Check((await Task.WhenAll(CreateDuplicate(duplicateName), CreateDuplicate(duplicateName.ToUpperInvariant()))).Count(x => x) == 1, "Parallel case-insensitive account creation bypassed uniqueness.");
await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => users.Authenticate(targetNameB, "incorrect-password")));
Check(await Scalar($"SELECT \"LockoutUntil\" FROM \"TMR_AppUsers\" WHERE \"Id\"={targetB}") is not DBNull, "Concurrent login attempts bypassed account lockout.");
Console.WriteLine("PASS: queued administrators recheck their own authority; parallel account creation preserves case-insensitive uniqueness and failed logins preserve lockout counters.");
if (Environment.GetEnvironmentVariable("TEAM_MANAGER_ORACLE_URL") is { Length: > 0 } scopedHttpAddress)
{
    foreach (var language in new[] { "hr", "en" })
    {
        await permissions.Assign(admin, manager, managerRole, false);
        await users.SetLanguage(manager, language);
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = new Uri(scopedHttpAddress) };
        string Token403(string html) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await http.GetStringAsync("/Login");
        using (var response = await http.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string> { { "EmployeeNumber", managerName }, { "Password", "SQL-test-password-2026" }, { "__RequestVerificationToken", Token403(login) } }))) response.EnsureSuccessStatusCode();
        var form = await http.GetStringAsync("/Profile?id=" + targetB);
        await using var held = holder.BeginTransaction();
        await Hold(held, "identity:" + manager, 6);
        await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:target AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
        {
            revoke.Parameters.Add("target", OracleDbType.Int32).Value = manager;
            revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = managerRole;
            await revoke.ExecuteNonQueryAsync();
        }
        var queued = http.PostAsync("/Profile?id=" + targetB + "&handler=GlobalRole", new FormUrlEncodedContent(new Dictionary<string, string> { { "role", readRole }, { "remove", "false" }, { "__RequestVerificationToken", Token403(form) } }));
        Check(await Task.WhenAny(queued, Task.Delay(1000)) != queued, "HTTP administrative write did not wait for actor lock.");
        await held.CommitAsync();
        using var denied = await queued;
        Check(denied.StatusCode == System.Net.HttpStatusCode.Forbidden, "Revoked HTTP administrative write did not return 403.");
        Check(await denied.Content.ReadAsStringAsync() == (language == "hr" ? "Nemaš ovlasti za ovu radnju." : "You do not have permission for this action."), "Revoked HTTP write lost its HR/EN response.");
    }
    Console.WriteLine("PASS: administrative authority revoked during HTTP waits returns HR/EN 403 without committing the change.");
}
var watcherName = "sqlWatcher" + suffix;
await users.Create(watcherName, "Scoped watcher " + suffix, "", null, "SQL-test-password-2026", actorId: admin);
var watcher = (await users.Find(watcherName))!.Id;
await permissions.Assign(admin, watcher, readRole, false);
await issues.SetWatching(watcher, fineA, true);
await FixtureSql("INSERT INTO \"TMR_EmailNotificationPreferences\" (\"UserId\",\"Address\",\"SendWatchedIssues\",\"SendDueReminders\") VALUES(:id,:address,1,0)", ("id", watcher), ("address", "watcher@example.test"));
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + watcher, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:target AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("target", OracleDbType.Int32).Value = watcher;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = readRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(admin, fineA, "Recheck watched recipient");
    await issues.Comment(admin, fineB, "Unrelated watcher scope").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!queued.IsCompleted, "Watched-recipient rights were not locked.");
    await held.CommitAsync();
    await queued;
}
Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_IssueEmailQueue\" WHERE \"UserId\"={watcher}")) == 0, "A revoked watcher was queued an email.");
Console.WriteLine("PASS: watcher access changes serialize with event creation; revoked recipients are not queued email while unrelated issues remain writable.");

// Catalogue and hierarchy dependencies must exclude unrelated branches/roles.
var catalogRoleA = "catA" + suffix;
var catalogRoleB = "catB" + suffix;
await permissions.Save(admin, catalogRoleA, "Scoped catalogue", ["issues.view", "issues.comment", "issues.edit"], true);
await permissions.Save(admin, catalogRoleB, "Scoped catalogue", ["issues.view"], true);
var catalogName = "sqlCatalog" + suffix;
await users.Create(catalogName, "Catalogue reader", "", null, "SQL-test-password-2026", actorId: admin);
var catalogUser = (await users.Find(catalogName))!.Id;
await permissions.Assign(admin, catalogUser, catalogRoleA, false);
var branchA = await projects.Save(admin, null, new ProjectInput { Name = "Branch A " + suffix });
var branchB = await projects.Save(admin, null, new ProjectInput { Name = "Branch B " + suffix });
var branchTaskA = await issues.Save(admin, branchA, null, new IssueInput { Title = "Branch catalogue A", IsPrivate = false });
var branchTaskB = await issues.Save(admin, branchB, null, new IssueInput { Title = "Branch catalogue B", IsPrivate = false });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "role:" + catalogRoleA, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_RolePermissions\" WHERE \"RoleName\"=:role AND \"Permission\"='issues.comment'", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = catalogRoleA;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(catalogUser, branchTaskA, "Must recheck role definition");
    await permissions.Save(admin, catalogRoleB, "Independent catalogue update", ["issues.view"], false).WaitAsync(TimeSpan.FromSeconds(5));
    await issues.Comment(admin, branchTaskB, "Independent branch and role").WaitAsync(TimeSpan.FromSeconds(5));
    await users.Authenticate(targetNameB, "SQL-test-password-2026").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!queued.IsCompleted, "Role definition reader ignored its catalogue lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked catalogue permission was accepted."); } catch (UnauthorizedAccessException) { }
}
await permissions.Save(admin, catalogRoleA, "Restored fixture", ["issues.view", "issues.comment", "issues.edit"], false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "project:" + branchA, 6);
    var revisionA = (await projects.List(admin)).Single(p => p.Id == branchA).Input.Revision;
    var revisionB = (await projects.List(admin)).Single(p => p.Id == branchB).Input.Revision;
    var blocked = projects.SetModules(admin, branchA, true, true, revisionA);
    await projects.SetModules(admin, branchB, true, true, revisionB).WaitAsync(TimeSpan.FromSeconds(5));
    await issues.Comment(admin, branchTaskB, "Unrelated project remains writable").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!blocked.IsCompleted, "Project metadata ignored its branch lock.");
    await held.RollbackAsync();
    await blocked.WaitAsync(TimeSpan.FromSeconds(5));
}
Console.WriteLine("PASS: role-definition and project metadata locks block dependent writes while unrelated catalogue edits, branches and login continue.");

var typeA = "TypeA" + suffix;
var typeB = "TypeB" + suffix;
await workflow.AddValue(admin, "type", typeA, false);
await workflow.AddValue(admin, "type", typeB, false);
var typedA = await issues.Save(admin, branchA, null, new IssueInput { Title = "Typed A", Type = typeA, IsPrivate = false });
var typedB = await issues.Save(admin, branchB, null, new IssueInput { Title = "Typed B", Type = typeB, IsPrivate = false });
var typedAComment = await issues.Save(admin, branchA, null, new IssueInput { Title = "Typed A comment", Type = typeA, IsPrivate = false });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "workflow-type:" + typeA, 6);
    var a = (await issues.Get(admin, typedA))!.Input; a.Title += " changed";
    var b = (await issues.Get(admin, typedB))!.Input; b.Title += " changed";
    var blocked = issues.Save(admin, branchA, typedA, a);
    await issues.Save(admin, branchB, typedB, b).WaitAsync(TimeSpan.FromSeconds(5));
    await workflow.Set(admin, new Transition(catalogRoleB, typeB, "Novo", "U tijeku"), false).WaitAsync(TimeSpan.FromSeconds(5));
    await issues.Comment(admin, typedAComment, "Comment does not depend on workflow transitions").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!blocked.IsCompleted, "Issue edit ignored workflow type lock.");
    await held.RollbackAsync(); await blocked.WaitAsync(TimeSpan.FromSeconds(5));
}
await workflow.Set(admin, new Transition(catalogRoleA, typeA, "Novo", "U tijeku"), false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "workflow-type:" + typeA, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_WorkflowTransitions\" WHERE \"RoleName\"=:role AND \"Type\"=:type", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = catalogRoleA;
        revoke.Parameters.Add("type", OracleDbType.NVarchar2).Value = typeA;
        await revoke.ExecuteNonQueryAsync();
    }
    var inputAfter = (await issues.Get(catalogUser, typedA))!.Input; inputAfter.Status = "U tijeku";
    var queued = issues.Save(catalogUser, branchA, typedA, inputAfter);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Transition mutation was not isolated.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Removed workflow transition was accepted."); } catch (ArgumentException) { }
}
Check((await issues.Get(admin, typedA))!.Input.Status == "Novo", "Rejected workflow change partially committed.");
Console.WriteLine("PASS: workflow changes are isolated by issue type; unrelated types and comments progress, and queued status edits recheck removed transitions.");

var inheritedName = "sqlBranch" + suffix;
await users.Create(inheritedName, "Branch reader", "", null, "SQL-test-password-2026", actorId: admin);
var branchReader = (await users.Find(inheritedName))!.Id;
await FixtureSql("INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(:project,:userId,:role)", ("project", branchA), ("userId", branchReader), ("role", catalogRoleA));
var movingChild = await projects.Save(admin, null, new ProjectInput { Name = "Moving child " + suffix, ParentId = branchA, InheritParentRoles = true });
var movingTask = await issues.Save(admin, movingChild, null, new IssueInput { Title = "Moving inherited task", IsPrivate = false });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "project:" + movingChild, 6);
    await using (var move = new OracleCommand("UPDATE \"TMR_Projects\" SET \"ParentId\"=:parent,\"Revision\"=\"Revision\"+1 WHERE \"Id\"=:child", holder) { Transaction = held, BindByName = true })
    {
        move.Parameters.Add("parent", OracleDbType.Int32).Value = branchB;
        move.Parameters.Add("child", OracleDbType.Int32).Value = movingChild;
        await move.ExecuteNonQueryAsync();
    }
    var queued = issues.Comment(branchReader, movingTask, "Must not retain old ancestor rights");
    await issues.Comment(admin, branchTaskB, "Independent hierarchy branch").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!queued.IsCompleted, "Hierarchy reader ignored project lock.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Old ancestor authority survived reparenting."); } catch (UnauthorizedAccessException) { }
}
var cycleA = await projects.Save(admin, null, new ProjectInput { Name = "Cycle A " + suffix });
var cycleB = await projects.Save(admin, null, new ProjectInput { Name = "Cycle B " + suffix });
var inputCycleA = (await projects.List(admin)).Single(p => p.Id == cycleA).Input; inputCycleA.ParentId = cycleB;
var inputCycleB = (await projects.List(admin)).Single(p => p.Id == cycleB).Input; inputCycleB.ParentId = cycleA;
async Task<bool> MoveAttempt(int id, ProjectInput input)
{
    try { await projects.Save(admin, id, input); return true; }
    catch (ArgumentException) { return false; }
    catch (InvalidOperationException error) when (error.Message.StartsWith("Projekt je")) { return false; }
}
var cycleResults = await Task.WhenAll(MoveAttempt(cycleA, inputCycleA), MoveAttempt(cycleB, inputCycleB));
Check(cycleResults.Count(x => x) == 1, "Concurrent reparenting introduced a cycle or rejected both valid first moves.");
Console.WriteLine("PASS: hierarchy paths are rediscovered after waiting; stale inherited authority is rejected and reversed concurrent moves cannot create cycles.");

// Creation reserves and locks its own ID before validating names and parents.
var createName = "Reserved project " + suffix;
var probeId = Convert.ToInt32(await Scalar("SELECT \"TMR_SEQ_Projects\".NEXTVAL FROM DUAL"));
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "project-name:" + createName.ToUpperInvariant(), 6);
    var creating = projects.Save(admin, null, new ProjectInput { Name = createName });
    Check(await Task.WhenAny(creating, Task.Delay(200)) != creating, "Project creation ignored its name lock.");
    var guessedEdit = projects.Save(admin, probeId + 1, new ProjectInput { Name = "Guessed pending project " + suffix, Revision = 0 });
    var independent = await projects.Save(admin, null, new ProjectInput { Name = "Independent creation " + suffix }).WaitAsync(TimeSpan.FromSeconds(5));
    await issues.Comment(admin, branchTaskB, "Project creation does not stall existing issues").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!guessedEdit.IsCompleted, "Reserved project ID was not protected against a guessed-ID edit.");
    await held.RollbackAsync();
    var created = await creating;
    Check(created == probeId + 1 && independent != created, "Reserved project ID changed during creation.");
    try { await guessedEdit; throw new Exception("Stale guessed-ID edit was accepted."); }
    catch (InvalidOperationException error) when (error.Message.StartsWith("Projekt je")) { }
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_ProjectMembers\" WHERE \"ProjectId\"={created} AND \"UserId\"={admin}")) == 1, "Creator membership was not atomic.");
}
async Task<int?> CreateNamed(string name)
{
    try { return await projects.Save(admin, null, new ProjectInput { Name = name }); } catch (ArgumentException) { return null; }
}
var duplicateProjects = await Task.WhenAll(CreateNamed("Duplicate project " + suffix), CreateNamed(("Duplicate project " + suffix).ToUpperInvariant()));
Check(duplicateProjects.Count(id => id is not null) == 1, "Parallel project creation bypassed case-insensitive uniqueness.");
Console.WriteLine("PASS: new projects reserve and lock their IDs; unrelated creation and writes progress, creator membership is atomic, and duplicate names/stale guessed-ID edits are rejected.");

var links = new IssueLinkStore(configuration, issues, workspace);
var graphIds = concurrentIds.Take(6).ToArray();
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "link-graph:blocks", 6);
    var blocked = links.Set(admin, graphIds[0], graphIds[1], "blocks", true, false);
    await links.Set(admin, graphIds[2], graphIds[3], "parent", true, false).WaitAsync(TimeSpan.FromSeconds(5));
    await links.Set(admin, graphIds[4], graphIds[5], "related", true, false).WaitAsync(TimeSpan.FromSeconds(5));
    await issues.Comment(admin, branchTaskB, "Independent graph activity").WaitAsync(TimeSpan.FromSeconds(5));
    Check(!blocked.IsCompleted, "Directed graph edit ignored its graph-kind lock.");
    await held.RollbackAsync(); await blocked;
}
await links.Set(admin, graphIds[2], graphIds[3], "blocks", true, false);
async Task<bool> LinkAttempt(int first, int second)
{
    try { await links.Set(admin, first, second, "blocks", true, false); return true; } catch (ArgumentException) { return false; }
}
var graphResults = await Task.WhenAll(LinkAttempt(graphIds[1], graphIds[2]), LinkAttempt(graphIds[3], graphIds[0]));
Check(graphResults.Count(x => x) == 1, "Concurrent disjoint graph edits introduced a directed cycle.");
Console.WriteLine("PASS: graph kinds and unrelated issue pairs progress independently; disjoint concurrent edges cannot close a directed cycle.");

var fields = new CustomFieldStore(configuration, projects, issues, workspace);
await fields.Create(admin, "project", "Structural field A " + suffix);
await fields.Create(admin, "project", "Structural field B " + suffix);
var projectTarget = await fields.Target(admin, true, "project", branchA);
var fieldList = await fields.List(admin, "project", branchA, projectTarget);
var fieldA = fieldList.Single(f => f.Name == "Structural field A " + suffix).Id;
var fieldB = fieldList.Single(f => f.Name == "Structural field B " + suffix).Id;
var saved = new SavedIssueQueryStore(configuration, workspace);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, $"project-data:{branchA}:field-value:{fieldA}", 6);
    var blocked = fields.Set(admin, true, "project", branchA, fieldA, 0, "A");
    await fields.Set(admin, true, "project", branchA, fieldB, 0, "B").WaitAsync(TimeSpan.FromSeconds(5));
    await saved.Create(targetB, "Independent saved query " + suffix, null, null, null, null, null, null).WaitAsync(TimeSpan.FromSeconds(5));
    Check(!blocked.IsCompleted, "Custom field value ignored its own key.");
    await held.RollbackAsync(); await blocked;
}
var savedRow = (await saved.List(targetB)).Single(q => q.Name == "Independent saved query " + suffix);
Check(!await saved.Delete(targetA, savedRow.Id), "Saved query ownership was not enforced.");
Check(await saved.Delete(targetB, savedRow.Id), "Owner could not delete saved query.");
Console.WriteLine("PASS: field values and saved queries use entity/user scopes, preserve revisions and enforce ownership without global serialization.");

await issues.LogTime(admin, branchTaskB, new(2026, 11, 20), 1m, "Scoped field time");
var timeEntry = Convert.ToInt32(await Scalar($"SELECT MAX(\"Id\") FROM \"TMR_TimeEntries\" WHERE \"IssueId\"={branchTaskB}"));
await fields.Create(admin, "time", "Structural time field " + suffix);
var timeTarget = await fields.Target(admin, true, "time", timeEntry);
var timeField = (await fields.List(admin, "time", timeEntry, timeTarget)).Single(f => f.Name == "Structural time field " + suffix).Id;
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "project:" + branchB, 6);
    await using (var disable = new OracleCommand($"UPDATE \"TMR_Projects\" SET \"TimeEnabled\"=0,\"Revision\"=\"Revision\"+1 WHERE \"Id\"={branchB}", holder) { Transaction = held }) await disable.ExecuteNonQueryAsync();
    var queued = fields.Set(admin, true, "time", timeEntry, timeField, 0, "Must not save");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Time-field write bypassed its project dependency.");
    await held.CommitAsync();
    try { await queued; throw new Exception("Time field was saved after time module was disabled."); } catch (UnauthorizedAccessException) { }
}
Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_TimeFieldValues\" WHERE \"EntityId\"={timeEntry} AND \"FieldId\"={timeField}")) == 0, "Rejected time-field write partially committed.");
var restore = (await projects.List(admin)).Single(p => p.Id == branchB).Input;
await projects.SetModules(admin, branchB, true, true, restore.Revision);
var noticeName = "sqlNotice" + suffix;
await users.Create(noticeName, "Scoped notification", "", null, "SQL-test-password-2026", actorId: admin);
var noticeUser = (await users.Find(noticeName))!.Id;
await permissions.Assign(admin, noticeUser, readRole, false);
await issues.SetWatching(noticeUser, branchTaskB, true);
await issues.Comment(admin, branchTaskB, "Scoped notification record");
var notice = (await new IssuePersonalStore(configuration, issues, workspace).Notifications(noticeUser)).First();
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "project:" + branchB, 6);
    await using (var disable = new OracleCommand($"UPDATE \"TMR_Projects\" SET \"IssuesEnabled\"=0,\"Revision\"=\"Revision\"+1 WHERE \"Id\"={branchB}", holder) { Transaction = held }) await disable.ExecuteNonQueryAsync();
    var queued = personal.MarkRead(noticeUser, notice.EventId);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Notification write bypassed project visibility dependency.");
    await held.CommitAsync();
    Check(!await queued, "Hidden notification was marked read.");
}
Check(Convert.ToInt32(await Scalar($"SELECT \"IsRead\" FROM \"TMR_IssueNotifications\" WHERE \"UserId\"={noticeUser} AND \"EventId\"={notice.EventId}")) == 0, "Hidden notification state was modified.");
Console.WriteLine("PASS: time-field and notification writes resolve their issue first, lock its permission dependencies, and reject module revocation without partial updates.");

// Project news must remain usable without SQL Server, including authority changes while queued.
var news = new ProjectNewsStore(configuration, workspace);
var newsProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle news " + suffix });
var newsOther = await projects.Save(admin, null, new ProjectInput { Name = "Other news " + suffix });
var newsBody = string.Concat(Enumerable.Repeat("Ž č ć š đ 🚀 HR/EN\n", 180));
var newsId = (await news.Publish(admin, newsProject, "Oracle novosti / News", newsBody))!.Value;
Check((await news.List(admin, newsProject, null)).Single().Body == "", "News list loaded article body.");
Check((await news.List(admin, newsProject, newsId)).Single().Body == newsBody, "News Unicode body changed.");
Check((await news.List(admin, newsOther, newsId)).Count == 0, "News crossed project boundary.");
Check(!await news.Comment(admin, newsOther, newsId, "Wrong project"), "Cross-project comment was inserted.");
var newsRole = "news" + suffix;
await permissions.Save(admin, newsRole, "News tests", ["issues.view", "news.comment"], true);
var newsName = "news" + suffix;
await users.Create(newsName, "News reader", "", null, "SQL-test-password-2026", actorId: admin);
var newsUser = (await users.Find(newsName))!.Id;
await FixtureSql("INSERT INTO \"TMR_ProjectMembers\" (\"ProjectId\",\"UserId\",\"Role\") VALUES(:projectId,:userId,:role)", ("projectId", newsProject), ("userId", newsUser), ("role", newsRole));
await news.Comment(newsUser, newsProject, newsId, "Komentar / Comment čćž 🚀");
Check((await news.Comments(newsUser, newsProject, newsId)).Single().Body == "Komentar / Comment čćž 🚀", "Comment relation/body changed.");
Check((await news.Comments(admin, newsProject, newsId)).Single().Author == "News reader", "Comment author link changed.");
Check((await news.List(admin, newsProject, newsId)).Single().Author == (await users.Find("admin"))!.DisplayName, "Article author link changed.");
try { await news.Publish(newsUser, newsProject, "Unauthorized", "No manager right"); throw new Exception("Reader published news."); } catch (UnauthorizedAccessException) { }
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + newsUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_ProjectMembers\" WHERE \"ProjectId\"=:project AND \"UserId\"=:userId", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("project", OracleDbType.Int32).Value = newsProject;
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = newsUser;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = news.Comment(newsUser, newsProject, newsId, "Revoked while waiting");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "News write ignored identity lock.");
    await news.Publish(admin, newsOther, "Independent news", "Other project remains writable").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked reader added comment."); } catch (UnauthorizedAccessException) { }
}
Check((await news.List(newsUser, newsProject, newsId)).Count == 0 && (await news.Comments(newsUser, newsProject, newsId)).Count == 0, "Revoked user read private news/comments.");
Check((await news.Comments(admin, newsProject, newsId)).Count == 1, "Rejected comment partially committed.");
var newsInput = (await projects.List(admin)).Single(p => p.Id == newsProject).Input;
newsInput.State = "Closed";
await projects.Save(admin, newsProject, newsInput);
Check(await news.Publish(admin, newsProject, "Closed", "Must not publish") is null && !await news.Comment(admin, newsProject, newsId, "Closed"), "Closed project accepted news writes.");
Check((await news.List(admin, newsProject, newsId)).Single().Body == newsBody, "Closing project lost article.");
newsInput = (await projects.List(admin)).Single(p => p.Id == newsProject).Input;
newsInput.IsPublic = true;
await projects.Save(admin, newsProject, newsInput);
Check((await news.List(newsUser, newsProject, newsId)).Count == 1 && (await news.Comments(newsUser, newsProject, newsId)).Count == 1, "Public news denied authenticated nonmember.");
await permissions.ManageAccount(admin, newsUser, false, null);
Check((await news.List(newsUser, newsProject, newsId)).Count == 0 && (await news.Comments(newsUser, newsProject, newsId)).Count == 0, "Inactive user read public news.");
Check(snapshots == 0, "News loaded a whole workspace.");
Console.WriteLine("PASS: Oracle news retains Unicode article/comment relationships, scoped reads, author names, private access and closed-project rules; queued revocation rejects writes while independent projects progress.");

// Project documents retain their binary content and authorization without SQL Server.
var documents = new ProjectDocumentStore(configuration, projects, workspace);
var docProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle documents " + suffix });
var docOther = await projects.Save(admin, null, new ProjectInput { Name = "Other documents " + suffix });
var docBytes = Enumerable.Range(0, 262144).Select(i => (byte)(i % 256)).ToArray();
Microsoft.AspNetCore.Http.IFormFile DocumentFile(byte[] bytes, string name = "../mapa/čćž.bin")
    => new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachment", name);
await documents.Add(admin, docProject, "Dokument / Document", "Opis čćž 🚀", DocumentFile(docBytes), CancellationToken.None);
counts.Clear();
var doc = (await documents.List(admin, docProject)).Single();
Check(doc.Size == docBytes.Length && doc.FileName == "čćž.bin" && doc.Description == "Opis čćž 🚀" && doc.Author == (await users.Find("admin"))!.DisplayName, "Document metadata/author/name changed.");
Check(counts.GetValueOrDefault("ProjectDocumentMetadata") == 1 && !counts.ContainsKey("ProjectDocuments"), "Document list loaded attachment rows instead of metadata.");
Check((await documents.Download(admin, docProject, doc.Id))!.Content.SequenceEqual(docBytes), "Document BLOB changed.");
Check(await documents.Download(admin, docOther, doc.Id) is null && !await documents.Remove(admin, docOther, doc.Id), "Document crossed project boundary.");
try { await documents.Add(admin, docProject, "Empty", "", DocumentFile([]), CancellationToken.None); throw new Exception("Empty document accepted."); } catch (ArgumentException) { }
try { await documents.Add(admin, docProject, "Invalid name", "", DocumentFile([1], ".."), CancellationToken.None); throw new Exception("Invalid filename accepted."); } catch (ArgumentException) { }
try { await documents.Add(admin, docProject, "Oversized", "", new Microsoft.AspNetCore.Http.FormFile(new MemoryStream([1]), 0, 10 * 1024 * 1024 + 1, "attachment", "large.bin"), CancellationToken.None); throw new Exception("Oversized document accepted."); } catch (ArgumentException) { }
var docRole = "docs" + suffix;
await permissions.Save(admin, docRole, "Document test manager", ["projects.manage"], true);
var docName = "docs" + suffix;
await users.Create(docName, "Document manager", "", null, "SQL-test-password-2026", actorId: admin);
var docUser = (await users.Find(docName))!.Id;
Check((await documents.List(docUser, docProject)).Count == 0 && await documents.Download(docUser, docProject, doc.Id) is null, "Private document leaked.");
await permissions.Assign(admin, docUser, docRole, false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + docUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = docUser;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = docRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = documents.Remove(docUser, docProject, doc.Id);
    var queuedUpload = documents.Add(docUser, docProject, "Revoked upload", "", DocumentFile([9, 8, 7]), CancellationToken.None);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Document deletion ignored identity lock.");
    await documents.Add(admin, docOther, "Independent", "", DocumentFile([1, 0, 255]), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked manager deleted document."); } catch (UnauthorizedAccessException) { }
    try { await queuedUpload; throw new Exception("Revoked manager uploaded document while queued."); } catch (UnauthorizedAccessException) { }
}
Check((await documents.Download(admin, docProject, doc.Id))!.Content.SequenceEqual(docBytes), "Rejected deletion changed content.");
Check((await documents.List(admin, docProject)).Count == 1, "Rejected upload left metadata.");
try { await documents.Add(docUser, docProject, "Unauthorized", "", DocumentFile([1]), CancellationToken.None); throw new Exception("Revoked manager uploaded."); } catch (UnauthorizedAccessException) { }
var docInput = (await projects.List(admin)).Single(p => p.Id == docProject).Input;
docInput.State = "Closed";
await projects.Save(admin, docProject, docInput);
Check(!await documents.Remove(admin, docProject, doc.Id), "Closed project allowed deletion.");
try { await documents.Add(admin, docProject, "Closed", "", DocumentFile([1]), CancellationToken.None); throw new Exception("Closed project accepted upload."); } catch (UnauthorizedAccessException) { }
Check((await documents.Download(admin, docProject, doc.Id))!.Content.SequenceEqual(docBytes), "Closed project lost document.");
docInput = (await projects.List(admin)).Single(p => p.Id == docProject).Input;
docInput.State = "Active";
await projects.Save(admin, docProject, docInput);
Check(await documents.Remove(admin, docProject, doc.Id) && !await documents.Remove(admin, docProject, doc.Id) && await documents.Download(admin, docProject, doc.Id) is null, "Document deletion was not scoped/idempotent.");
Check(snapshots == 0, "Documents loaded a whole workspace.");
Console.WriteLine("PASS: Oracle documents preserve metadata, author, sanitized filename and 256 KiB BLOB; private access, project boundaries, upload validation, queued revocation, independent writes and closed-project rules hold.");

var wiki = new ProjectWikiStore(configuration, workspace);
var wikiProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle wiki " + suffix });
var wikiOther = await projects.Save(admin, null, new ProjectInput { Name = "Other wiki " + suffix });
var wikiBody = string.Concat(Enumerable.Repeat("Ž č ć š đ 🚀 HR/EN\n", 900));
var wikiId = (await wiki.Save(admin, wikiProject, null, 0, "Prva stranica", wikiBody))!.Value;
Check((await wiki.Revision(admin, wikiProject, wikiId, null))!.Body == wikiBody, "Wiki NCLOB changed.");
Check((await wiki.History(admin, wikiProject, wikiId)).Single().Author == (await users.Find("admin"))!.DisplayName, "Wiki author relation changed.");
counts.Clear();
Check((await wiki.Pages(admin, wikiProject)).Single().Revision == 1 && (await wiki.History(admin, wikiProject, wikiId)).Count == 1, "Wiki metadata missing.");
Check(!counts.ContainsKey("ProjectWikiRevisions") && !counts.ContainsKey("WikiRevision"), "Wiki metadata loaded revision bodies.");
var wikiResults = await Task.WhenAll(wiki.Save(admin, wikiProject, wikiId, 1, "Druga stranica", "Second A"), wiki.Save(admin, wikiProject, wikiId, 1, "Druga stranica", "Second B"));
Check(wikiResults.Count(r => r is not null) == 1 && (await wiki.History(admin, wikiProject, wikiId)).Select(r => r.Number).SequenceEqual([2, 1]), "Concurrent wiki edits lost update or added partial revision.");
Check((await wiki.Revision(admin, wikiProject, wikiId, 1))!.Body == wikiBody, "Wiki history overwritten.");
Check(await wiki.Revision(admin, wikiOther, wikiId, 1) is null && (await wiki.History(admin, wikiOther, wikiId)).Count == 0 && await wiki.Save(admin, wikiOther, wikiId, 2, "Wrong project", "No") is null, "Wiki crossed project boundary.");
try { await wiki.Save(admin, wikiProject, null, 0, "DRUGA STRANICA", "Duplicate"); throw new Exception("Duplicate wiki title accepted."); } catch (InvalidOperationException error) when (error.Message == "Stranica s tim naslovom već postoji u projektu.") { }
var wikiRole = "wiki" + suffix;
await permissions.Save(admin, wikiRole, "Wiki editor", ["issues.view", "wiki.edit"], true);
var wikiName = "wiki" + suffix;
await users.Create(wikiName, "Wiki editor", "", null, "SQL-test-password-2026", actorId: admin);
var wikiUser = (await users.Find(wikiName))!.Id;
Check((await wiki.Pages(wikiUser, wikiProject)).Count == 0 && await wiki.Revision(wikiUser, wikiProject, wikiId, 1) is null, "Private wiki leaked.");
await permissions.Assign(admin, wikiUser, wikiRole, false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + wikiUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = wikiUser;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = wikiRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = wiki.Save(wikiUser, wikiProject, wikiId, 2, "Revoked", "Must not persist");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Wiki ignored identity lock.");
    await wiki.Save(admin, wikiOther, null, 0, "Independent", "Other project").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked wiki editor saved."); } catch (UnauthorizedAccessException) { }
}
Check((await wiki.History(admin, wikiProject, wikiId)).Count == 2 && (await wiki.Pages(admin, wikiProject)).Count == 1, "Rejected save left revision/page.");
var wikiInput = (await projects.List(admin)).Single(p => p.Id == wikiProject).Input;
wikiInput.State = "Closed";
await projects.Save(admin, wikiProject, wikiInput);
try { await wiki.Save(admin, wikiProject, wikiId, 2, "Closed", "Forbidden"); throw new Exception("Closed wiki accepted edit."); } catch (UnauthorizedAccessException) { }
Check((await wiki.Revision(admin, wikiProject, wikiId, 1))!.Body == wikiBody, "Closed project lost history.");
Check(snapshots == 0, "Wiki loaded whole workspace.");
Console.WriteLine("PASS: Oracle wiki retains long Unicode bodies, authors and immutable history; concurrent stale edits, duplicate titles, project boundaries, queued revocation and closed-project rules are enforced.");

var forum = new ProjectForumStore(configuration, workspace);
var forumProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle forum " + suffix });
var forumOther = await projects.Save(admin, null, new ProjectInput { Name = "Other forum " + suffix });
var forumBody = string.Concat(Enumerable.Repeat("Ž č ć š đ 🚀 HR/EN\n", 180));
var forumId = (await forum.Create(admin, forumProject, "Tema / Topic", forumBody))!.Value;
Check((await forum.Topics(admin, forumProject, null)).Single().Body == "", "Forum list fetched body.");
Check((await forum.Topics(admin, forumProject, forumId)).Single().Body == forumBody, "Forum Unicode body changed.");
var forumRole = "forum" + suffix;
await permissions.Save(admin, forumRole, "Forum poster", ["issues.view", "forum.post"], true);
var forumName = "forum" + suffix;
await users.Create(forumName, "Forum poster", "", null, "SQL-test-password-2026", actorId: admin);
var forumUser = (await users.Find(forumName))!.Id;
Check((await forum.Topics(forumUser, forumProject, forumId)).Count == 0 && (await forum.Replies(forumUser, forumProject, forumId)).Count == 0, "Private forum leaked.");
await permissions.Assign(admin, forumUser, forumRole, false);
Check(await forum.Reply(forumUser, forumProject, forumId, "Odgovor čćž 🚀"), "Authorized reply failed.");
Check((await forum.Replies(admin, forumProject, forumId)).Single() is { Body: "Odgovor čćž 🚀", Author: "Forum poster" }, "Reply body/author relationship changed.");
try { await forum.SetLocked(forumUser, forumProject, forumId, true); throw new Exception("Poster moderated topic."); } catch (UnauthorizedAccessException) { }
Check((await forum.Topics(admin, forumOther, forumId)).Count == 0 && (await forum.Replies(admin, forumOther, forumId)).Count == 0 && !await forum.Reply(admin, forumOther, forumId, "Wrong project") && !await forum.SetLocked(admin, forumOther, forumId, true), "Forum crossed project boundary.");
var independentForum = (await forum.Create(admin, forumProject, "Independent topic", "Independent"))!.Value;
await using (var held = holder.BeginTransaction())
{
    await Hold(held, $"project-data:{forumProject}:forum-topic:{forumId}", 6);
    await using (var closeTopic = new OracleCommand($"UPDATE \"TMR_ForumTopics\" SET \"IsLocked\"=1 WHERE \"Id\"={forumId}", holder) { Transaction = held }) await closeTopic.ExecuteNonQueryAsync();
    var queued = forum.Reply(forumUser, forumProject, forumId, "Must see locked state");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Reply ignored topic lock.");
    await forum.Reply(admin, forumProject, independentForum, "Unrelated topic progresses").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    Check(!await queued, "Reply entered locked topic.");
}
Check((await forum.Replies(admin, forumProject, forumId)).Count == 1, "Rejected reply partially committed.");
Check(await forum.SetLocked(admin, forumProject, forumId, false), "Unlock failed.");
await permissions.Save(admin, forumRole, "Forum moderator", ["issues.view", "forum.post", "forum.moderate"], false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + forumUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = forumUser;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = forumRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queuedReply = forum.Reply(forumUser, forumProject, forumId, "Revoked reply");
    var queuedLock = forum.SetLocked(forumUser, forumProject, forumId, true);
    Check(await Task.WhenAny(queuedReply, Task.Delay(200)) != queuedReply && !queuedLock.IsCompleted, "Forum ignored identity lock.");
    await forum.Create(admin, forumOther, "Independent project", "Still writable").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queuedReply; throw new Exception("Revoked poster replied."); } catch (UnauthorizedAccessException) { }
    try { await queuedLock; throw new Exception("Revoked moderator locked topic."); } catch (UnauthorizedAccessException) { }
}
Check((await forum.Topics(admin, forumProject, forumId)).Single().IsLocked == false && (await forum.Replies(admin, forumProject, forumId)).Count == 1, "Rejected forum mutation changed data.");
var forumInput = (await projects.List(admin)).Single(p => p.Id == forumProject).Input;
forumInput.State = "Closed";
await projects.Save(admin, forumProject, forumInput);
Check(await forum.Create(admin, forumProject, "Closed", "No") is null && !await forum.Reply(admin, forumProject, forumId, "Closed") && !await forum.SetLocked(admin, forumProject, forumId, true), "Closed project allowed forum mutation.");
Check((await forum.Topics(admin, forumProject, forumId)).Single().Body == forumBody && (await forum.Replies(admin, forumProject, forumId)).Single().Body == "Odgovor čćž 🚀", "Closing project lost forum data.");
Check(snapshots == 0, "Forum loaded whole workspace.");
Console.WriteLine("PASS: Oracle forum preserves topics, ordered replies and authors; topic locking, independent-topic progress, scoped moderation, queued revocation and closed-project rules hold.");

var repositories = new ProjectRepositoryStore(configuration, projects, workspace);
var repoProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle repositories " + suffix });
var repoOther = await projects.Save(admin, null, new ProjectInput { Name = "Other repositories " + suffix });
Check(await repositories.Add(admin, repoProject, "Repo čćž", new Uri("https://example.test/project.git")), "Repository add failed.");
var repo = (await repositories.List(admin, repoProject)).Single();
Check(repo.Name == "Repo čćž" && repo.Url == "https://example.test/project.git", "Repository metadata changed.");
Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_AdministrationAudit\" WHERE \"Action\"='project.repository.add' AND \"Subject\"='Repo čćž' AND \"Details\"='#{repoProject}'")) == 1, "Repository add audit missing.");
try { await repositories.Add(admin, repoProject, "REPO ČĆŽ", new Uri("https://example.test/other.git")); throw new Exception("Duplicate repository name accepted."); } catch (InvalidOperationException error) when (error.Message == "Taj naziv već postoji.") { }
foreach (var badUrl in new[] { "http://example.test/repo", "https://user:secret@example.test/repo", "https://example.test/repo?token=secret", "https://example.test/repo#fragment" })
    try { await repositories.Add(admin, repoProject, "Invalid", new Uri(badUrl)); throw new Exception("Unsafe URL accepted."); } catch (ArgumentException) { }
Check(!await repositories.Remove(admin, repoOther, repo.Id) && await repositories.ForHistory(admin, repoOther, repo.Id) is null, "Repository crossed project boundary.");
var repoRole = "repo" + suffix;
await permissions.Save(admin, repoRole, "Repository viewer", ["issues.view"], true);
var repoName = "repo" + suffix;
await users.Create(repoName, "Repository viewer", "", null, "SQL-test-password-2026", actorId: admin);
var repoUser = (await users.Find(repoName))!.Id;
Check((await repositories.List(repoUser, repoProject)).Count == 0, "Private repository leaked.");
await permissions.Assign(admin, repoUser, repoRole, false);
Check((await repositories.List(repoUser, repoProject)).Count == 1 && await repositories.ForHistory(repoUser, repoProject, repo.Id) is null, "Project view granted Git history permission.");
try { await repositories.Remove(repoUser, repoProject, repo.Id); throw new Exception("Viewer removed repository."); } catch (UnauthorizedAccessException) { }
await permissions.Save(admin, repoRole, "Repository manager", ["issues.view", "projects.manage", "repositories.view"], false);
Check((await repositories.ForHistory(repoUser, repoProject, repo.Id))?.Id == repo.Id, "Repository history right ignored.");
var gitConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [$"RepositoryHistory:Paths:{repo.Id}"] = Directory.GetCurrentDirectory() }).Build();
var gitHistory = new RepositoryHistory(gitConfiguration);
var gitCommits = await gitHistory.Recent(repo.Id, CancellationToken.None);
Check(gitCommits.Count > 0 && (await gitHistory.Diff(repo.Id, gitCommits[0], CancellationToken.None)).Text.Length > 0, "Local Git history/diff missing.");
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + repoUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = repoUser;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = repoRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = repositories.Remove(repoUser, repoProject, repo.Id);
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Repository mutation ignored identity lock.");
    await repositories.Add(admin, repoOther, "Independent", new Uri("https://example.test/independent.git")).WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked manager removed repository."); } catch (UnauthorizedAccessException) { }
}
Check((await repositories.List(admin, repoProject)).Single().Id == repo.Id && await repositories.ForHistory(repoUser, repoProject, repo.Id) is null, "Revocation lost repository or exposed history.");
var repoInput = (await projects.List(admin)).Single(p => p.Id == repoProject).Input;
repoInput.State = "Closed";
await projects.Save(admin, repoProject, repoInput);
Check(!await repositories.Add(admin, repoProject, "Closed", new Uri("https://example.test/closed.git")) && !await repositories.Remove(admin, repoProject, repo.Id), "Closed project mutated repositories.");
Check((await repositories.ForHistory(admin, repoProject, repo.Id))?.Id == repo.Id, "Closed project lost history binding.");
repoInput = (await projects.List(admin)).Single(p => p.Id == repoProject).Input; repoInput.State = "Active";
await projects.Save(admin, repoProject, repoInput);
Check(await repositories.Remove(admin, repoProject, repo.Id) && !await repositories.Remove(admin, repoProject, repo.Id), "Repository removal not scoped/idempotent.");
Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM \"TMR_AdministrationAudit\" WHERE \"Action\"='project.repository.remove' AND \"Subject\"='#{repo.Id}'")) == 1, "Repository removal audit not atomic.");
if (Environment.GetEnvironmentVariable("TEAM_MANAGER_REPOSITORY_FIXTURE_OUT") is { Length: > 0 } repoFixtureOut)
{
    var demoName = "Oracle Git demo " + suffix;
    await repositories.Add(admin, 1, demoName, new Uri("https://example.test/team-manager.git"));
    var demoRepo = (await repositories.List(admin, 1)).Single(r => r.Name == demoName);
    await File.WriteAllTextAsync(repoFixtureOut, System.Text.Json.JsonSerializer.Serialize(new { ProjectId = 1, RepositoryId = demoRepo.Id, Path = Directory.GetCurrentDirectory() }));
}
Check(snapshots == 0, "Repositories loaded whole workspace.");
Console.WriteLine("PASS: Oracle repositories preserve URLs, project bindings and atomic audit; history rights, local Git commits/diff, queued revocation, validation and closed-project rules hold.");

var mailProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle mail " + suffix });
var mailOther = await projects.Save(admin, null, new ProjectInput { Name = "Other mail " + suffix });
byte[] MailHash(string id) => SHA256.HashData(Encoding.UTF8.GetBytes(id));
var mailHash = MailHash("oracle-mail-" + suffix);
IssueInput MailInput(string title = "Oracle mail čćž 🚀") => new() { Title = title, Description = "MIME decoded body čćž 🚀", IsPrivate = true };
var deliveries = await Task.WhenAll(issues.ImportMail(admin, mailProject, mailHash, MailInput()), issues.ImportMail(admin, mailProject, mailHash, MailInput()));
var importedId = deliveries.Single(id => id is not null)!.Value;
Check(deliveries.Count(id => id is null) == 1 && (await issues.Get(admin, importedId)) is { Input.IsPrivate: true }, "Concurrent mail deduplication failed.");
Check(await issues.ImportMail(admin, mailOther, mailHash, MailInput()) is not null, "Mail digest incorrectly global across projects.");
var invalidHash = MailHash("rollback-" + suffix);
try { var invalid = MailInput(); invalid.Type = "missing-type"; await issues.ImportMail(admin, mailProject, invalidHash, invalid); throw new Exception("Invalid mail created issue."); } catch (ArgumentException) { }
Check(await issues.ImportMail(admin, mailProject, invalidHash, MailInput("Retry after rollback")) is not null, "Failed import left a receipt.");
var mailRole = "mail" + suffix;
await permissions.Save(admin, mailRole, "Mail importer", ["issues.view", "issues.create"], true);
var mailName = "mail" + suffix;
await users.Create(mailName, "Mail importer", "", null, "SQL-test-password-2026", actorId: admin);
var mailUser = (await users.Find(mailName))!.Id;
await permissions.Assign(admin, mailUser, mailRole, false);
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "identity:" + mailUser, 6);
    await using (var revoke = new OracleCommand("DELETE FROM \"TMR_UserRoles\" WHERE \"UserId\"=:userId AND \"RoleName\"=:role", holder) { Transaction = held, BindByName = true })
    {
        revoke.Parameters.Add("userId", OracleDbType.Int32).Value = mailUser;
        revoke.Parameters.Add("role", OracleDbType.NVarchar2).Value = mailRole;
        await revoke.ExecuteNonQueryAsync();
    }
    var queued = issues.ImportMail(mailUser, mailProject, MailHash("revoked-" + suffix), MailInput());
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Mail importer ignored identity lock.");
    await issues.ImportMail(admin, mailOther, MailHash("independent-" + suffix), MailInput()).WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked mail importer created issue."); } catch (UnauthorizedAccessException) { }
}
try { await issues.ImportMail(mailUser, mailProject, mailHash, MailInput()); throw new Exception("Duplicate bypassed revoked rights."); } catch (UnauthorizedAccessException) { }
var mailProjectInput = (await projects.List(admin)).Single(p => p.Id == mailProject).Input;
mailProjectInput.State = "Closed";
await projects.Save(admin, mailProject, mailProjectInput);
try { await issues.ImportMail(admin, mailProject, MailHash("closed-" + suffix), MailInput()); throw new Exception("Closed project accepted mail."); } catch (UnauthorizedAccessException) { }
Console.WriteLine("PASS: Oracle mail import atomically creates private issues/events and receipts; concurrent deduplication, per-project digests, rollback/retry, queued revocation and closed-project checks hold.");

using (var certificateStore = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser))
{
    certificateStore.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
    var certificate = certificateStore.Certificates.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>()
        .Where(c => c.HasPrivateKey && c.NotAfter > DateTime.Now && c.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1")).OrderByDescending(c => c.NotAfter).First();
    using var mailbox = new TestImapMailbox(certificate);
    var imapProject = await projects.Save(admin, null, new ProjectInput { Name = "Oracle TLS IMAP " + suffix });
    var imapConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MailIntake:PollSeconds"] = "5", [$"MailIntake:Imap:{imapProject}:Enabled"] = "true",
        [$"MailIntake:Imap:{imapProject}:Host"] = "localhost", [$"MailIntake:Imap:{imapProject}:Port"] = mailbox.Port.ToString(),
        [$"MailIntake:Imap:{imapProject}:Username"] = "mailbox@example.test", [$"MailIntake:Imap:{imapProject}:Password"] = "local-test-only",
        [$"MailIntake:Imap:{imapProject}:Folder"] = "INBOX", [$"MailIntake:Imap:{imapProject}:EmployeeNumber"] = "admin"
    }).Build();
    var monitor = new ImapImportMonitor();
    await using var provider = new ServiceCollection().AddSingleton(users).AddSingleton(issues).BuildServiceProvider();
    using var importer = new ImapIssueImporter(imapConfig, provider.GetRequiredService<IServiceScopeFactory>(), monitor, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImapIssueImporter>.Instance);
    await importer.StartAsync(CancellationToken.None);
    for (var attempt = 0; attempt < 100 && !mailbox.Seen; attempt++) await Task.Delay(200);
    await importer.StopAsync(CancellationToken.None);
    Check(mailbox.Seen && monitor.Get(imapProject)?.LastIssueId is int, "TLS IMAP message was not committed/acknowledged.");
    var imapId = monitor.Get(imapProject)!.LastIssueId!.Value;
    Check((await issues.Get(admin, imapId)) is { Input.Title: "QA IMAP issue", Input.Description: "PLAIN text from IMAP", Input.IsPrivate: true }, "TLS IMAP MIME decoding changed.");
    Check(await issues.ImportMail(admin, imapProject, MailHash("<imap-test@example.test>"), MailInput()) is null, "IMAP receipt did not deduplicate gateway retry.");
    Console.WriteLine("PASS: real local TLS IMAP connector decodes MIME into an Oracle private issue and acknowledges only after the receipt commits.");
}

var apiTokens = new ApiTokenStore(configuration, workspace);
var apiName = "api" + suffix;
await users.Create(apiName, "API owner", "", null, "SQL-test-password-2026", actorId: admin);
var apiOwner = (await users.Find(apiName))!;
var apiSecret = (await apiTokens.Create(apiOwner.Id, apiOwner.SessionVersion, "Oracle API"))!;
Check(apiSecret.Length == 67 && await apiTokens.Authenticate(apiSecret, CancellationToken.None) == apiOwner.Id, "API token authentication failed.");
var apiInfo = (await apiTokens.List(apiOwner.Id)).Single();
Check(apiInfo.Active && !await apiTokens.Revoke(admin, apiInfo.Id), "API token ownership not enforced.");
Check(await apiTokens.Authenticate("invalid", CancellationToken.None) is null, "Malformed API token accepted.");
Check(await users.ChangePassword(apiOwner, "New-API-password-2026"), "API owner's password change failed.");
Check(await apiTokens.Authenticate(apiSecret, CancellationToken.None) is null && await apiTokens.Create(apiOwner.Id, apiOwner.SessionVersion, "Stale") is null, "Old session API token survived password reset.");
apiOwner = (await users.Find(apiName))!;
apiSecret = (await apiTokens.Create(apiOwner.Id, apiOwner.SessionVersion, "Current"))!;
await permissions.ManageAccount(admin, apiOwner.Id, false, null);
Check(await apiTokens.Authenticate(apiSecret, CancellationToken.None) is null, "Deactivated owner retained API access.");
await permissions.ManageAccount(admin, apiOwner.Id, true, null);
apiOwner = (await users.Find(apiName))!;
apiSecret = (await apiTokens.Create(apiOwner.Id, apiOwner.SessionVersion, "Revoke"))!;
var currentApi = (await apiTokens.List(apiOwner.Id)).First();
Check(await apiTokens.Revoke(apiOwner.Id, currentApi.Id) && await apiTokens.Authenticate(apiSecret, CancellationToken.None) is null, "Explicit API revocation failed.");
Console.WriteLine("PASS: Oracle API tokens enforce ownership, hash authentication, malformed-token rejection, password/session invalidation, deactivation and explicit revocation.");

var registrations = new RegistrationStore(configuration, new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>(), workspace);
var registrationName = "reg" + suffix;
await Task.WhenAll(registrations.Submit(registrationName, "Registration owner", "Registration-password-2026"), registrations.Submit(registrationName.ToUpperInvariant(), "Duplicate", "Registration-password-2026"));
var pendingRegistration = (await registrations.Pending(admin)).Single(r => r.EmployeeNumber.Equals(registrationName, StringComparison.OrdinalIgnoreCase));
Check(await users.Find(registrationName) is null, "Pending registration activated an account.");
try { await registrations.Pending(viewer); throw new Exception("Nonadmin read registrations."); } catch (UnauthorizedAccessException) { }
var decisions = await Task.WhenAll(registrations.Decide(admin, pendingRegistration.Id, true), registrations.Decide(admin, pendingRegistration.Id, true));
Check(decisions.Count(v => v) == 1 && await users.Authenticate(registrationName, "Registration-password-2026") is { IsAdministrator: false, IsActive: true }, "Registration approval not atomic or password changed.");
Check(!(await registrations.Pending(admin)).Any(r => r.Id == pendingRegistration.Id), "Approved request retained.");
await registrations.Submit("reject" + suffix, "Rejected", "Registration-password-2026");
var rejected = (await registrations.Pending(admin)).Single(r => r.EmployeeNumber == "reject" + suffix);
Check(await registrations.Decide(admin, rejected.Id, false) && await users.Find(rejected.EmployeeNumber) is null, "Rejected registration created account.");
await registrations.Submit("conflict" + suffix, "Conflict", "Registration-password-2026");
var conflict = (await registrations.Pending(admin)).Single(r => r.EmployeeNumber == "conflict" + suffix);
await users.Create(conflict.EmployeeNumber, "Existing", "", null, "SQL-test-password-2026", actorId: admin);
try { await registrations.Decide(admin, conflict.Id, true); throw new Exception("Conflicting registration approved."); } catch (InvalidOperationException error) when (error.Message == "Račun s tim brojem radnika već postoji. Odbij zahtjev ako više nije potreban.") { }
Check((await registrations.Pending(admin)).Any(r => r.Id == conflict.Id), "Conflict lost pending request.");
Check(await registrations.Decide(admin, conflict.Id, false), "Conflicting registration rejection failed.");
Console.WriteLine("PASS: Oracle registration serializes duplicate submissions/approvals, preserves password hashes, restricts pending access and retains conflicts without partial accounts/audit.");

var groupsStore = new GroupStore(configuration, workspace);
var groupName = "Group " + suffix;
await groupsStore.Create(admin, groupName);
var managedGroup = (await groupsStore.List(admin)).Groups.Single(g => g.Name == groupName).Id;
var groupedName = "grp" + suffix;
await users.Create(groupedName, "Grouped writer", "", null, "SQL-test-password-2026", actorId: admin);
var groupedUser = (await users.Find(groupedName))!.Id;
var groupedRole = "grp" + suffix;
await permissions.Save(admin, groupedRole, "Group test", ["issues.view", "issues.comment"], true);
await groupsStore.SetRole(admin, managedGroup, groupedRole, false);
await groupsStore.SetMember(admin, managedGroup, groupedUser, false);
await groupsStore.SetMember(admin, managedGroup, groupedUser, false);
Check((await permissions.Effective(groupedUser)).Contains("issues.comment"), "Group role not granted.");
try { await groupsStore.List(groupedUser); throw new Exception("Nonadmin read group directory."); } catch (UnauthorizedAccessException) { }
var groupProject = await projects.Save(admin, null, new ProjectInput { Name = "Group project " + suffix });
var groupIssue = await issues.Save(admin, groupProject, null, new IssueInput { Title = "Group permission dependency" });
var groupIndependentIssue = await issues.Save(admin, groupProject, null, new IssueInput { Title = "Independent group actor" });
await using (var held = holder.BeginTransaction())
{
    await Hold(held, "group:" + managedGroup, 6);
    await using (var revoke = new OracleCommand($"DELETE FROM \"TMR_UserGroupRoles\" WHERE \"GroupId\"={managedGroup}", holder) { Transaction = held }) await revoke.ExecuteNonQueryAsync();
    var queued = issues.Comment(groupedUser, groupIssue, "Revoked group writer");
    Check(await Task.WhenAny(queued, Task.Delay(200)) != queued, "Issue write ignored group dependency lock.");
    await issues.Comment(admin, groupIndependentIssue, "Independent actor").WaitAsync(TimeSpan.FromSeconds(5));
    await held.CommitAsync();
    try { await queued; throw new Exception("Revoked group writer commented."); } catch (UnauthorizedAccessException) { }
}
await groupsStore.SetRole(admin, managedGroup, groupedRole, false);
await groupsStore.SetMember(admin, managedGroup, groupedUser, true);
Check(!(await permissions.Effective(groupedUser)).Contains("issues.comment"), "Group removal retained rights.");
Check((await groupsStore.List(admin)).Assignments.All(a => a.GroupId != managedGroup || a.UserId != groupedUser), "Group membership removal failed.");
Console.WriteLine("PASS: Oracle groups retain members/roles/audit, enforce administrator access and serialize dependent writes with group revocation while unrelated actors progress.");

if (builder.UserID.Equals("TMR_MIGRATION_QA", StringComparison.OrdinalIgnoreCase))
{
    var fullGroupRole = (string)(await Scalar("SELECT r.\"RoleName\" FROM \"TMR_UserRoles\" r JOIN \"TMR_RolePermissions\" p ON p.\"RoleName\"=r.\"RoleName\" WHERE r.\"UserId\"=" + admin + " AND p.\"Permission\"='*' FETCH FIRST 1 ROW ONLY"))!;
    await groupsStore.SetMember(admin, managedGroup, groupedUser, false);
    await groupsStore.SetRole(admin, managedGroup, fullGroupRole, false);
    var activeGroupBefore = new List<int>();
    await using (var activeUsers = new OracleCommand($"SELECT \"Id\" FROM \"TMR_AppUsers\" WHERE \"IsActive\"=1 AND \"Id\"<>{groupedUser}", connection))
    await using (var activeRows = await activeUsers.ExecuteReaderAsync())
        while (await activeRows.ReadAsync()) activeGroupBefore.Add(activeRows.GetInt32(0));
    try
    {
        await FixtureSql($"UPDATE \"TMR_AppUsers\" SET \"IsActive\"=0 WHERE \"IsActive\"=1 AND \"Id\"<>{groupedUser}");
        try { await groupsStore.SetRole(groupedUser, managedGroup, fullGroupRole, true); throw new Exception("Last group administrator removed."); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Mora ostati")) { }
        Check((await permissions.Effective(groupedUser)).Contains("*"), "Last group administrator rollback failed.");
    }
    finally { foreach (var id in activeGroupBefore) await FixtureSql("UPDATE \"TMR_AppUsers\" SET \"IsActive\"=1 WHERE \"Id\"=:id", ("id", id)); }
    Console.WriteLine("PASS: removing the only full administrator granted through a group rolls back membership/role and audit changes.");
}

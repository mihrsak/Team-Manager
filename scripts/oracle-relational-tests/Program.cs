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

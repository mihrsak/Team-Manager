using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

internal sealed class RegistrationConflictException() : InvalidOperationException("Račun s tim brojem radnika već postoji. Odbij zahtjev ako više nije potreban.");

public sealed partial class OracleWorkspace
{
    internal Task SubmitRegistration(string employee, string name, string hash)
        => Run(async w =>
        {
            if (string.IsNullOrWhiteSpace(employee) || employee.Length > 30 || string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Provjeri unesene podatke.");
            if ((await w.Where("AppUsers", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).Any() ||
                (await w.Where("RegistrationRequests", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).Any()) return false;
            w.Add("RegistrationRequests", ("Id", await w.Next("RegistrationRequests")), ("EmployeeNumber", employee), ("DisplayName", name), ("PasswordHash", hash), ("CreatedAt", DateTime.UtcNow));
            return true;
        }, true, new(null, [], [], [], ["account-name:" + employee.Trim().ToUpperInvariant()]));

    internal Task<List<PendingRegistration>> PendingRegistrations(int actor) => Read(async w =>
    {
        if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
        return (await w.Rows("RegistrationRequests")).OrderBy(Id).Select(r => new PendingRegistration(Id(r), Text(r, "EmployeeNumber"), Text(r, "DisplayName"), Date(r, "CreatedAt")!.Value)).ToList();
    });

    internal async Task<bool> DecideRegistration(int actor, int id, bool approve)
    {
        var names = await Query("RegistrationReference", "SELECT \"EmployeeNumber\" FROM \"TMR_RegistrationRequests\" WHERE \"Id\"=:id", c => AddQueryParameter(c, "id", id), r => r.GetString(0));
        if (names.Count == 0) return false;
        return await Run(async w =>
        {
            if (!await Right(w, actor, "*")) throw new UnauthorizedAccessException();
            var request = await w.Find("RegistrationRequests", id);
            if (request is null) return false;
            var employee = Text(request, "EmployeeNumber");
            if (approve)
            {
                if ((await w.Where("AppUsers", "UPPER(t.\"EmployeeNumber\")=UPPER(:employee)", ("employee", employee))).Any()) throw new RegistrationConflictException();
                w.Add("AppUsers", ("Id", await w.Next("AppUsers")), ("EmployeeNumber", employee), ("DisplayName", Text(request, "DisplayName")),
                    ("PasswordHash", Text(request, "PasswordHash")), ("IsAdministrator", false), ("IsActive", true), ("SessionVersion", 0),
                    ("JobTitle", ""), ("OrganizationUnitId", null), ("FailedAttempts", 0), ("LockoutUntil", null), ("CreatedAt", DateTime.UtcNow), ("DisplayTheme", "light"), ("UiLanguage", "hr"));
            }
            w.Table("RegistrationRequests").Remove(request);
            await Audit(w, actor, approve ? "registration.approve" : "registration.reject", employee);
            return true;
        }, true, new(actor, [], [actor], [], ["account-name:" + names[0].ToUpperInvariant(), "registration:" + id]));
    }
}

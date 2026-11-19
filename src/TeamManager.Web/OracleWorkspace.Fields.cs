using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal static async Task<FieldTarget?> FieldTarget(Workspace w, int actor, string scope, int id)
    {
        if (scope == "project")
        {
            var p = (await w.Find("Projects", id));
            if (p is null || !(Flag(p, "IsPublic") || (await Manage(w, actor, id)) || (await Right(w, actor, "issues.view", id))))
                return null;
            return new(Text(p, "Name"), id, null, Text(p, "State") == "Active" && (await Manage(w, actor, id)));
        }

        if (scope == "user")
        {
            var person = (await w.Find("AppUsers", id));
            return person is not null && (actor == id || (await Right(w, actor, "users.manage"))) ? new(Text(person, "DisplayName"), null, null, true) : null;
        }

        var time = (await w.Find("TimeEntries", id));
        if (time is null || (await w.Find("Issues", Int(time, "IssueId"))) is not { } issue || !(await Visible(w, actor, issue)) || !(await Can(w, actor, Int(issue, "ProjectId"), "time.view")))
            return null;
        return new(Text(issue, "Title"), Int(issue, "ProjectId"), Id(issue), Int(time, "UserId") == actor && (await Can(w, actor, Int(issue, "ProjectId"), "time.log")));
    }

    internal Task<List<CustomFieldValue>> Fields(int actor, string scope, int? entity) => Read(async w =>
    {
        if (entity is int id && (await FieldTarget(w, actor, scope, id)) is null)
            throw new UnauthorizedAccessException();
        var table = scope switch
        {
            "user" => "UserFieldValues",
            "project" => "ProjectFieldValues",
            "time" => "TimeFieldValues",
            _ => throw new ArgumentException()
        };
        var values = await w.Rows(table);
        return (await w.Rows("CustomFields")).Where(f => Text(f, "Scope") == scope).Select(f =>
        {
            var value = values.FirstOrDefault(v => Int(v, "FieldId") == Id(f) && Int(v, "EntityId") == entity);
            return new CustomFieldValue(Id(f), Text(f, "Name"), value is null ? null : value["Value"]?.GetValue<string>(), value is null ? 0 : Int(value, "Revision"));
        }).ToList();
    });
    internal Task SetField(int actor, string scope, int entity, int field, int revision, string? value) => WriteFieldValue(actor, scope, entity, field, async w =>
    {
        if ((await FieldTarget(w, actor, scope, entity))?.CanEdit != true)
            throw new UnauthorizedAccessException();
        if ((await w.Find("CustomFields", field)) is not { } f || Text(f, "Scope") != scope)
            throw new ArgumentException("Odaberi polje te vrste.");
        value = value?.Trim();
        if (value?.Length > 500)
            throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");
        var table = scope switch
        {
            "user" => "UserFieldValues",
            "project" => "ProjectFieldValues",
            "time" => "TimeFieldValues",
            _ => throw new ArgumentException()
        };
        var row = (await w.Rows(table)).FirstOrDefault(r => Int(r, "EntityId") == entity && Int(r, "FieldId") == field);
        if ((row is null ? 0 : Int(row, "Revision")) != revision)
            throw new InvalidOperationException("Vrijednost je u međuvremenu izmijenjena. Osvježi stranicu.");
        row ??= w.Add(table, ("EntityId", entity), ("FieldId", field), ("Revision", 0));
        Set(row, "Value", string.IsNullOrEmpty(value) ? null : value);
        Revise(row);
        return true;
    });
}

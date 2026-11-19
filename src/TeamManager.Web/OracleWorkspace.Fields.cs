using System.Text.Json.Nodes;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal static FieldTarget? FieldTarget(Workspace w, int actor, string scope, int id)
    {
        if (scope == "project")
        {
            var p = w.Find("Projects", id);
            if (p is null || !(Flag(p, "IsPublic") || Manage(w, actor, id) || Right(w, actor, "issues.view", id)))
                return null;
            return new(Text(p, "Name"), id, null, Text(p, "State") == "Active" && Manage(w, actor, id));
        }

        if (scope == "user")
        {
            var person = w.Find("AppUsers", id);
            return person is not null && (actor == id || Right(w, actor, "users.manage")) ? new(Text(person, "DisplayName"), null, null, true) : null;
        }

        var time = w.Find("TimeEntries", id);
        if (time is null || w.Find("Issues", Int(time, "IssueId"))is not { } issue || !Visible(w, actor, issue) || !Can(w, actor, Int(issue, "ProjectId"), "time.view"))
            return null;
        return new(Text(issue, "Title"), Int(issue, "ProjectId"), Id(issue), Int(time, "UserId") == actor && Can(w, actor, Int(issue, "ProjectId"), "time.log"));
    }

    internal Task<List<CustomFieldValue>> Fields(int actor, string scope, int? entity) => Read(w =>
    {
        if (entity is int id && FieldTarget(w, actor, scope, id)is null)
            throw new UnauthorizedAccessException();
        var table = scope switch
        {
            "user" => "UserFieldValues",
            "project" => "ProjectFieldValues",
            "time" => "TimeFieldValues",
            _ => throw new ArgumentException()};
        return w.Rows("CustomFields").Where(f => Text(f, "Scope") == scope).Select(f =>
        {
            var value = w.Rows(table).FirstOrDefault(v => Int(v, "FieldId") == Id(f) && Int(v, "EntityId") == entity);
            return new CustomFieldValue(Id(f), Text(f, "Name"), value is null ? null : value["Value"]?.GetValue<string>(), value is null ? 0 : Int(value, "Revision"));
        }).ToList();
    });
    internal Task SetField(int actor, string scope, int entity, int field, int revision, string? value) => Write(w =>
    {
        if (FieldTarget(w, actor, scope, entity)?.CanEdit != true)
            throw new UnauthorizedAccessException();
        if (w.Find("CustomFields", field)is not { } f || Text(f, "Scope") != scope)
            throw new ArgumentException("Odaberi polje te vrste.");
        value = value?.Trim();
        if (value?.Length > 500)
            throw new ArgumentException("Vrijednost polja može imati najviše 500 znakova.");
        var table = scope switch
        {
            "user" => "UserFieldValues",
            "project" => "ProjectFieldValues",
            "time" => "TimeFieldValues",
            _ => throw new ArgumentException()};
        var row = w.Rows(table).FirstOrDefault(r => Int(r, "EntityId") == entity && Int(r, "FieldId") == field);
        if ((row is null ? 0 : Int(row, "Revision")) != revision)
            throw new InvalidOperationException("Vrijednost je u međuvremenu izmijenjena. Osvježi stranicu.");
        row ??= w.Add(table, ("EntityId", entity), ("FieldId", field), ("Revision", 0));
        Set(row, "Value", string.IsNullOrEmpty(value) ? null : value);
        Revise(row);
        return true;
    });
}

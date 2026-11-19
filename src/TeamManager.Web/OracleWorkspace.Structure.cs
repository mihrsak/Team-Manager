using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private Task<T> WriteLink<T>(int actor, int first, int second, string kind, Func<Workspace, Task<T>> action)
        => Run(action, true, new WriteScope(actor, new[] { first, second }.Distinct().Order().ToArray(), [actor], [], kind is "parent" or "blocks" ? ["link-graph:" + kind] : []));
    internal Task<T> WriteSavedQuery<T>(int actor, int? project, string? status, Func<Workspace, Task<T>> action)
        => Run(action, true, new WriteScope(actor, [], [actor], [actor], [])
        { Projects = project is int id ? [id] : [], CatalogReaders = status is null ? [] : ["value:IssueStatuses:" + status] });
    private async Task<T> WriteFieldValue<T>(int actor, string kind, int entity, int field, Func<Workspace, Task<T>> action)
    {
        if (kind == "user") return await WriteAccount(actor, entity, action);
        if (kind == "project") return await WriteProjectData(actor, entity, "field-value:" + field, action);
        if (kind != "time") throw new ArgumentException("Unknown field scope.");
        // Time entries and events retain their issue IDs for their lifetime.
        var references = await Query("TimeFieldReference", "SELECT \"IssueId\" FROM \"TMR_TimeEntries\" WHERE \"Id\"=:id", c => AddQueryParameter(c, "id", entity), r => r.GetInt32(0));
        if (references.Count == 0) throw new UnauthorizedAccessException();
        return await Run(action, true, new(actor, [references[0]], [actor], [], [$"time-field:{entity}:{field}"]));
    }
    internal async Task<bool> WriteNotification(int actor, int eventId, Func<Workspace, Task<bool>> action)
    {
        var references = await Query("NotificationReference", "SELECT \"IssueId\" FROM \"TMR_IssueEvents\" WHERE \"Id\"=:id", c => AddQueryParameter(c, "id", eventId), r => r.GetInt32(0));
        if (references.Count == 0) return false;
        return await Run(action, true, new(actor, [references[0]], [actor], [], [$"notification:{actor}:{eventId}"]));
    }
}

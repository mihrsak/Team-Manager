using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<int?> ImportIssueMail(int actor, int project, byte[] hash, IssueInput input)
    {
        if (hash.Length != 32) throw new ArgumentException("Invalid message digest.");
        return Run<int?>(async w =>
        {
            // Recheck authority even for an existing receipt; retries must not bypass revoked rights.
            if (!await Can(w, actor, project, "issues.create") || !await Can(w, actor, project, "issues.view")) throw new UnauthorizedAccessException();
            if ((await w.Where("ReceivedIssueMail", "t.\"ProjectId\"=:project AND t.\"MessageHash\"=:hash", ("project", project), ("hash", hash))).Any()) return null;
            input.IsPrivate = true;
            var id = await SaveIssue(w, actor, project, null, input);
            w.Add("ReceivedIssueMail", ("ProjectId", project), ("MessageHash", Convert.ToBase64String(hash)), ("IssueId", id), ("ReceivedAt", DateTime.UtcNow));
            return id;
        }, true, new WriteScope(actor, [], input.AssigneeId is int assigned ? [actor, assigned] : [actor], [], [$"mail:{project}:{Convert.ToHexString(hash)}"])
        { Projects = [project], CatalogReaders = IssueCatalogKeys(input) });
    }
}

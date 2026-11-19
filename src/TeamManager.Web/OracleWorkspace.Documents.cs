using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<List<ProjectDocument>> DocumentsList(int user, int project)
        => Query("ProjectDocumentMetadata", SecuritySql + $"""
            SELECT d."Id",d."Title",d."Description",d."FileName",DBMS_LOB.GETLENGTH(d."Content"),u."DisplayName",d."CreatedAt"
            FROM "TMR_ProjectDocuments" d JOIN "TMR_Projects" p ON p."Id"=d."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=d."AuthorId"
            WHERE p."Id"=:scope AND {ProjectContentVisibilitySql} ORDER BY d."Id" DESC
            """, c => Viewer(c, user, project),
            r => new ProjectDocument(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5), r.GetDateTime(6)));

    internal async Task<AttachmentDownload?> DocumentDownload(int user, int project, int document)
        => (await Query("ProjectDocumentDownload", SecuritySql + $"""
            SELECT d."FileName",d."Content" FROM "TMR_ProjectDocuments" d
            JOIN "TMR_Projects" p ON p."Id"=d."ProjectId"
            WHERE p."Id"=:scope AND d."Id"=:document AND {ProjectContentVisibilitySql}
            """, c => { Viewer(c, user, project); AddQueryParameter(c, "document", document); },
            r => new AttachmentDownload(r.GetString(0), r.GetFieldValue<byte[]>(1)))).FirstOrDefault();

    internal async Task AddDocument(int actor, int project, string title, string description, IFormFile? file, CancellationToken token)
    {
        if (!await ManageSql(actor, project)) throw new UnauthorizedAccessException();
        var upload = await UploadedFile.Read(file, token);
        await WriteProjectData(actor, project, "documents", async w =>
        {
            if (!await Manage(w, actor, project) || (await w.Find("Projects", project)) is not { } p || Text(p, "State") != "Active")
                throw new UnauthorizedAccessException();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 150 || description.Length > 2000)
                throw new ArgumentException("Unesi naslov do 150 znakova i opis do 2000 znakova.");
            token.ThrowIfCancellationRequested();
            w.Add("ProjectDocuments", ("Id", await w.Next("ProjectDocuments")), ("ProjectId", project), ("AuthorId", actor),
                ("Title", title), ("Description", description), ("FileName", upload.Name), ("Content", Convert.ToBase64String(upload.Content)), ("CreatedAt", DateTime.UtcNow));
            return true;
        });
    }

    internal Task<bool> RemoveDocument(int actor, int project, int document)
        => WriteProjectData(actor, project, "document:" + document, async w =>
        {
            if (!await Manage(w, actor, project)) throw new UnauthorizedAccessException();
            var p = await w.Find("Projects", project);
            if (p is null || Text(p, "State") != "Active") return false;
            var row = await w.Find("ProjectDocuments", document);
            if (row is null || Int(row, "ProjectId") != project) return false;
            w.Table("ProjectDocuments").Remove(row);
            return true;
        });
}

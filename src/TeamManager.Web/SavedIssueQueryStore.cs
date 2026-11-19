using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record SavedIssueQuery(int Id, string Name, int? ProjectId, string? Status, string? SearchText,
    string? SortOrder, string? GroupBy, string? Columns);

public sealed class SavedIssueQueryStore(IConfiguration configuration, OracleWorkspace oracle)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<SavedIssueQuery>> List(int user)
    {
        if (oracle.Enabled) return await oracle.Read(async w => (await w.Rows("SavedIssueQueries")).Where(r => OracleWorkspace.Workspace.Int(r, "UserId") == user)
            .Select(r => new SavedIssueQuery(OracleWorkspace.Workspace.Id(r), OracleWorkspace.Workspace.Text(r, "Name"), OracleWorkspace.Workspace.OptionalInt(r, "ProjectId"),
                r["Status"]?.GetValue<string>(), r["SearchText"]?.GetValue<string>(), r["SortOrder"]?.GetValue<string>(), r["GroupBy"]?.GetValue<string>(), r["Columns"]?.GetValue<string>())).ToList());
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Id,Name,ProjectId,Status,SearchText,SortOrder,GroupBy,Columns
            FROM dbo.SavedIssueQueries WHERE UserId=@user ORDER BY Name
            """, connection);
        command.Parameters.Add(P("@user", user));

        var queries = new List<SavedIssueQuery>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            queries.Add(new(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), ReadOptional(reader, 4), ReadOptional(reader, 5), ReadOptional(reader, 6), ReadOptional(reader, 7)));
        return queries;
    }

    private static string? ReadOptional(SqlDataReader reader, int index)
        => reader.IsDBNull(index) ? null : reader.GetString(index);

    public async Task Create(int user, string name, int? project, string? status, string? search, string? sortOrder, string? groupBy, string? columns)
    {
        if (oracle.Enabled)
        {
            await oracle.WriteSavedQuery(user, project, status, async w =>
            {
                if (project is int selected && !(await OracleWorkspace.Can(w, user, selected, "issues.view"))) throw new ArgumentException("Upit s tim nazivom već postoji ili odabrani filtar više nije dostupan.");
                if ((await w.By("SavedIssueQueries", "UserId", user)).Any(r => OracleWorkspace.Workspace.Int(r, "UserId") == user && OracleWorkspace.Workspace.Text(r, "Name") == name))
                    throw new ArgumentException("Upit s tim nazivom već postoji ili odabrani filtar više nije dostupan.");
                w.Add("SavedIssueQueries", ("Id", (await w.Next("SavedIssueQueries"))), ("UserId", user), ("Name", name), ("ProjectId", project),
                    ("Status", status), ("SearchText", search), ("SortOrder", sortOrder), ("GroupBy", groupBy), ("Columns", columns)); return true;
            }); return;
        }
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.SavedIssueQueries(UserId,Name,ProjectId,Status,SearchText,SortOrder,GroupBy,Columns)
            VALUES(@user,@name,@project,@status,@search,@sort,@group,@columns)
            """, connection);
        command.Parameters.AddRange([P("@user", user), P("@name", name), P("@project", project),
            P("@status", status), P("@search", search), P("@sort", sortOrder), P("@group", groupBy), P("@columns", columns)]);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> Delete(int user, int id)
    {
        if (oracle.Enabled) return await oracle.WriteSavedQuery(user, null, null, async w =>
        {
            var row = (await w.Find("SavedIssueQueries", id));
            if (row is null || OracleWorkspace.Workspace.Int(row, "UserId") != user) return false; w.Table("SavedIssueQueries").Remove(row); return true;
        });
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "DELETE dbo.SavedIssueQueries WHERE UserId=@user AND Id=@id", connection);
        command.Parameters.AddRange([P("@user", user), P("@id", id)]);
        return await command.ExecuteNonQueryAsync() == 1;
    }
}

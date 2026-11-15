using Microsoft.Data.SqlClient;
using static TeamManager.Web.UserStore;

namespace TeamManager.Web;

public sealed record SavedIssueQuery(int Id, string Name, int? ProjectId, string? Status, string? SearchText,
    string? SortOrder, string? GroupBy, string? Columns);

public sealed class SavedIssueQueryStore(IConfiguration configuration)
{
    private SqlConnection Connect() => new(configuration.GetConnectionString("TeamManager"));

    public async Task<List<SavedIssueQuery>> List(int user)
    {
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
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "DELETE dbo.SavedIssueQueries WHERE UserId=@user AND Id=@id", connection);
        command.Parameters.AddRange([P("@user", user), P("@id", id)]);
        return await command.ExecuteNonQueryAsync() == 1;
    }
}

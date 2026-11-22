using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
namespace TeamManager.Web;
public static class DatabaseInstaller
{
    public static readonly string[] SchemaFiles=["001-schema.sql","003-users.sql","004-roles.sql","005-issues.sql","007-workflow.sql","008-projects.sql","009-project-modules.sql","010-account-lifecycle.sql","011-membership-roles.sql","012-user-groups.sql","013-project-role-inheritance.sql","014-time-entry-activity.sql","015-issue-links.sql","016-saved-issue-queries.sql","017-saved-issue-query-layout.sql","018-issue-start-dates.sql","019-project-milestones.sql","020-issue-roadmap-items.sql","021-issue-watchers.sql","022-issue-notes-notifications.sql","023-administration-audit.sql","024-audit-details.sql","025-project-news.sql","026-project-wiki.sql","027-issue-attachments.sql","028-project-documents.sql","029-project-forums.sql","030-account-display-theme.sql","031-account-language.sql","032-project-issue-fields.sql","033-custom-fields.sql","034-registration-approval.sql","035-issue-field-visibility.sql","036-private-issues.sql","037-project-repositories.sql","038-api-tokens.sql","039-email-notification-queue.sql","040-smtp-delivery.sql","041-incoming-issue-mail.sql","042-issue-type-length.sql","043-catalog-order-activation.sql"];
    public static async Task Run(string connectionString,bool createDatabase,bool demo)
    {
        var settings=new SqlConnectionStringBuilder(connectionString);
        if(string.IsNullOrWhiteSpace(settings.InitialCatalog)||new[]{"master","tempdb","model","msdb"}.Contains(settings.InitialCatalog,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Configure a dedicated application database.");
        if(createDatabase)
        {
            var name=settings.InitialCatalog;var master=new SqlConnectionStringBuilder(connectionString){InitialCatalog="master"};
            await using var c=new SqlConnection(master.ConnectionString);await c.OpenAsync();
            await using var cmd=new SqlCommand("IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(max)=N'CREATE DATABASE '+QUOTENAME(@name); EXEC sys.sp_executesql @sql; END",c);cmd.Parameters.AddWithValue("@name",name);await cmd.ExecuteNonQueryAsync();
        }
        await using var connection=new SqlConnection(connectionString);await connection.OpenAsync();
        foreach(var file in demo?new[]{"002-demo.sql","006-demo-issues.sql","demo-issue-catalog.sql"}:SchemaFiles)
        {
            var path=Path.Combine(AppContext.BaseDirectory,"database",file);
            var sql=await File.ReadAllTextAsync(path);
            foreach(var batch in Regex.Split(sql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            {
                if(string.IsNullOrWhiteSpace(batch))continue;
                await using var cmd=new SqlCommand(batch,connection){CommandTimeout=120};await cmd.ExecuteNonQueryAsync();
            }
            Console.WriteLine($"Applied: {file}");
        }
    }
}

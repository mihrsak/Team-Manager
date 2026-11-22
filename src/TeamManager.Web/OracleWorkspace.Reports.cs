using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using System.Text.Json;
using Oracle.ManagedDataAccess.Client;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    private const string AllowedProjectsSql = ", allowed_projects AS (SELECT id FROM JSON_TABLE(:projects,'$[*]' COLUMNS(id NUMBER PATH '$'))) ";
    private static void ReportParameters(OracleCommand command, int user, IEnumerable<int> projects)
    {
        Viewer(command, user);
        command.Parameters.Add("projects", OracleDbType.Clob).Value = JsonSerializer.Serialize(projects.Distinct().ToArray());
    }
    internal Task<List<TimeReportRow>> TimeReport(int user, IEnumerable<int> projects, DateTime? from, DateTime? to, string? person, string? activity)
        => Query("TimeReport", SecuritySql + AllowedProjectsSql + $"""
            SELECT p."Name",u."DisplayName",u."EmployeeNumber",t."Activity",SUM(t."Hours")
            FROM "TMR_TimeEntries" t JOIN "TMR_Issues" i ON i."Id"=t."IssueId"
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" JOIN "TMR_AppUsers" u ON u."Id"=t."UserId"
            WHERE {IssueVisibilitySql} AND p."TimeEnabled"=1 AND p."Id" IN (SELECT id FROM allowed_projects)
              AND (:date_from IS NULL OR t."WorkDate">=:date_from) AND (:date_to IS NULL OR t."WorkDate"<=:date_to)
              AND (:person IS NULL OR INSTR(UPPER(u."DisplayName"||N' '||TO_NCHAR(u."EmployeeNumber")),UPPER(:person))>0)
              AND (:activity IS NULL OR INSTR(UPPER(t."Activity"),UPPER(:activity))>0)
            GROUP BY p."Id",p."Name",u."Id",u."DisplayName",u."EmployeeNumber",t."Activity"
            ORDER BY p."Name",u."DisplayName",t."Activity",p."Id",u."Id"
            """, c =>
            {
                ReportParameters(c, user, projects);
                c.Parameters.Add("date_from", OracleDbType.Date).Value = (object?)from ?? DBNull.Value;
                c.Parameters.Add("date_to", OracleDbType.Date).Value = (object?)to ?? DBNull.Value;
                c.Parameters.Add("person", OracleDbType.NVarchar2).Value = string.IsNullOrEmpty(person) ? DBNull.Value : person;
                c.Parameters.Add("activity", OracleDbType.NVarchar2).Value = string.IsNullOrEmpty(activity) ? DBNull.Value : activity;
            }, r => new TimeReportRow(r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4)));
    internal Task<List<ActivityRow>> RecentActivity(int user, IEnumerable<int> projects)
        => Query("RecentActivity", SecuritySql + AllowedProjectsSql + $"""
            SELECT e."Id",i."Id",i."Title",p."Name",u."DisplayName",e."Kind",e."CreatedAt"
            FROM "TMR_IssueEvents" e JOIN "TMR_Issues" i ON i."Id"=e."IssueId" JOIN "TMR_Projects" p ON p."Id"=i."ProjectId"
            JOIN "TMR_AppUsers" u ON u."Id"=e."UserId"
            WHERE {IssueVisibilitySql} AND p."Id" IN (SELECT id FROM allowed_projects) AND (e."Kind"<>'time' OR p."TimeEnabled"=1)
            ORDER BY e."CreatedAt" DESC,e."Id" DESC FETCH FIRST 50 ROWS ONLY
            """, c => ReportParameters(c, user, projects), r => new ActivityRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4), r.GetString(5), r.GetDateTime(6)));
    internal Task<List<IssueNotification>> Notifications(int user)
        => Query("Notifications", SecuritySql + $"""
            SELECT e."Id",i."Id",i."Title",p."Name",e."Kind",e."CreatedAt",n."IsRead"
            FROM "TMR_IssueNotifications" n JOIN "TMR_Issues" i ON i."Id"=n."IssueId" JOIN "TMR_IssueEvents" e ON e."Id"=n."EventId"
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId"
            WHERE n."UserId"=:viewer AND {IssueVisibilitySql} AND (e."Kind"<>'time' OR p."TimeEnabled"=1)
            ORDER BY n."IsRead",e."CreatedAt" DESC,e."Id" DESC FETCH FIRST 50 ROWS ONLY
            """, c => Viewer(c, user), r => new IssueNotification(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetInt32(6) != 0));
    internal async Task<ILookup<int, RoadmapStatusCount>> RoadmapProgressSql(int user, int project)
        => (await Query("RoadmapProgress", SecuritySql + $"""
            SELECT i."RoadmapItemId",i."Status",s."IsClosed",COUNT(*) FROM "TMR_Issues" i
            JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" JOIN "TMR_IssueStatuses" s ON s."Name"=i."Status"
            WHERE p."Id"=:scope AND i."RoadmapItemId" IS NOT NULL AND {IssueVisibilitySql}
            GROUP BY i."RoadmapItemId",i."Status",s."IsClosed" ORDER BY s."IsClosed",i."Status",i."RoadmapItemId"
            """, c => Viewer(c, user, project), r => new RoadmapStatusCount(r.GetInt32(0), r.GetString(1), r.GetInt32(2) != 0, r.GetInt32(3)))).ToLookup(r => r.ItemId);
    internal Task<List<AdministrationAuditEntry>> RecentAuditSql()
        => Query("AdministrationAudit", """
            SELECT "__EMPTY","Id","ActorId","ActorEmployeeNumber","Action","Subject","CreatedAt","Details"
            FROM "TMR_AdministrationAudit" ORDER BY "Id" DESC FETCH FIRST 100 ROWS ONLY
            """, _ => { }, r =>
            {
                var row = ReadTypedRow(Schema.Value.Tables.Single(t => t.Name == "AdministrationAudit"), r);
                return new AdministrationAuditEntry(Workspace.Date(row, "CreatedAt")!.Value, Workspace.Text(row, "ActorEmployeeNumber"), Workspace.Text(row, "Action"), Workspace.Text(row, "Subject"), Workspace.Text(row, "Details"));
            });
    internal Task<List<RoadmapMatch>> RoadmapSearchSql(IEnumerable<int> projects, string query,int offset=0,int take=20)
        => Query("RoadmapSearch", """
            SELECT m."ProjectId",p."Name",m."Id",m."Kind",m."Name" FROM "TMR_ProjectMilestones" m JOIN "TMR_Projects" p ON p."Id"=m."ProjectId"
            WHERE p."Id" IN (SELECT id FROM JSON_TABLE(:projects,'$[*]' COLUMNS(id NUMBER PATH '$')))
              AND (INSTR(UPPER(m."Name"),UPPER(:search))>0 OR INSTR(UPPER(m."Description"),UPPER(:search))>0)
            ORDER BY m."Name",p."Name",m."Id" OFFSET :offset ROWS FETCH NEXT :take ROWS ONLY
            """, c => { c.Parameters.Add("projects", OracleDbType.Clob).Value = JsonSerializer.Serialize(projects.Distinct().ToArray()); AddQueryParameter(c, "search", query);AddQueryParameter(c,"offset",offset);AddQueryParameter(c,"take",take); }, r => new RoadmapMatch(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
}

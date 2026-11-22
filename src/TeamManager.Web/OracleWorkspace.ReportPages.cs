namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal Task<List<TimeDetail>> TimeDetails(int user,IEnumerable<int> projects,DateTime? from,DateTime? to,string? person,string? activity,int offset)=>Query("TimeDetails",SecuritySql+AllowedProjectsSql+$"""
        SELECT t."Id",i."Id",i."Title",p."Name",u."DisplayName",t."WorkDate",t."Hours",t."Activity",t."Note"
        FROM "TMR_TimeEntries" t JOIN "TMR_Issues" i ON i."Id"=t."IssueId" JOIN "TMR_Projects" p ON p."Id"=i."ProjectId" JOIN "TMR_AppUsers" u ON u."Id"=t."UserId"
        WHERE {IssueVisibilitySql} AND p."TimeEnabled"=1 AND p."Id" IN (SELECT id FROM allowed_projects)
          AND (:date_from IS NULL OR t."WorkDate">=:date_from) AND (:date_to IS NULL OR t."WorkDate"<=:date_to)
          AND (:person IS NULL OR INSTR(UPPER(u."DisplayName"||N' '||TO_NCHAR(u."EmployeeNumber")),UPPER(:person))>0)
          AND (:activity IS NULL OR INSTR(UPPER(t."Activity"),UPPER(:activity))>0)
        ORDER BY t."WorkDate" DESC,t."Id" DESC OFFSET :offset ROWS FETCH NEXT 51 ROWS ONLY
        """,c=>{ReportParameters(c,user,projects);c.Parameters.Add("date_from",Oracle.ManagedDataAccess.Client.OracleDbType.Date).Value=(object?)from??DBNull.Value;c.Parameters.Add("date_to",Oracle.ManagedDataAccess.Client.OracleDbType.Date).Value=(object?)to??DBNull.Value;c.Parameters.Add("person",Oracle.ManagedDataAccess.Client.OracleDbType.NVarchar2).Value=string.IsNullOrEmpty(person)?DBNull.Value:person;c.Parameters.Add("activity",Oracle.ManagedDataAccess.Client.OracleDbType.NVarchar2).Value=string.IsNullOrEmpty(activity)?DBNull.Value:activity;AddQueryParameter(c,"offset",offset);},r=>new TimeDetail(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDateTime(5),r.GetDecimal(6),r.GetString(7),r.IsDBNull(8)?"":r.GetString(8)));
    internal Task<List<AdministrationAuditEntry>> AuditPage(int offset,int take,string? query)=>Query("AdministrationAudit","""
        SELECT "CreatedAt","ActorEmployeeNumber","Action","Subject","Details" FROM "TMR_AdministrationAudit"
        WHERE :search IS NULL OR INSTR(UPPER("ActorEmployeeNumber"),UPPER(:search))>0 OR INSTR(UPPER("Action"),UPPER(:search))>0 OR INSTR(UPPER("Subject"),UPPER(:search))>0 OR INSTR(UPPER("Details"),UPPER(:search))>0
        ORDER BY "Id" DESC OFFSET :offset ROWS FETCH NEXT :take ROWS ONLY
        """,c=>{c.Parameters.Add("search",Oracle.ManagedDataAccess.Client.OracleDbType.NVarchar2).Value=(object?)query??DBNull.Value;AddQueryParameter(c,"offset",offset);AddQueryParameter(c,"take",take);},r=>new AdministrationAuditEntry(r.GetDateTime(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?"":r.GetString(4)));
    internal Task<List<IssueNotification>> NotificationPage(int user,int offset,int take,string? query)=>Query("Notifications",SecuritySql+$"""
        SELECT e."Id",i."Id",i."Title",p."Name",e."Kind",e."CreatedAt",n."IsRead"
        FROM "TMR_IssueNotifications" n JOIN "TMR_Issues" i ON i."Id"=n."IssueId" JOIN "TMR_IssueEvents" e ON e."Id"=n."EventId"
        JOIN "TMR_Projects" p ON p."Id"=i."ProjectId"
        WHERE n."UserId"=:viewer AND {IssueVisibilitySql} AND (e."Kind"<>'time' OR p."TimeEnabled"=1)
          AND (:search IS NULL OR INSTR(UPPER(i."Title"),UPPER(:search))>0 OR INSTR(UPPER(p."Name"),UPPER(:search))>0)
        ORDER BY n."IsRead",e."CreatedAt" DESC,e."Id" DESC OFFSET :offset ROWS FETCH NEXT :take ROWS ONLY
        """,c=>{Viewer(c,user);c.Parameters.Add("search",Oracle.ManagedDataAccess.Client.OracleDbType.NVarchar2).Value=(object?)query??DBNull.Value;AddQueryParameter(c,"offset",offset);AddQueryParameter(c,"take",take);},r=>new IssueNotification(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDateTime(5),r.GetInt32(6)!=0));
}

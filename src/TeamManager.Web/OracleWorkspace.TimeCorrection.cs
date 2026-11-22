using static TeamManager.Web.OracleWorkspace.Workspace;
namespace TeamManager.Web;
public sealed partial class OracleWorkspace
{
    internal async Task<EditableTime?> TimeEntryFor(int user,int issue,int entry)
    {
        var rows=await Query("TimeEntry",SecuritySql+$"""
            SELECT t."Id",t."IssueId",t."UserId",t."WorkDate",t."Hours",t."Activity",t."Note"
            FROM "TMR_TimeEntries" t JOIN "TMR_Issues" i ON i."Id"=t."IssueId" JOIN "TMR_Projects" p ON p."Id"=i."ProjectId"
            WHERE t."Id"=:entry AND i."Id"=:issue AND p."TimeEnabled"=1 AND {IssueVisibilitySql}
            """,c=>{Viewer(c,user);AddQueryParameter(c,"entry",entry);AddQueryParameter(c,"issue",issue);},r=>new EditableTime(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetDateTime(3),r.GetDecimal(4),r.GetString(5),r.IsDBNull(6)?"":r.GetString(6)));
        return rows.SingleOrDefault();
    }
    internal Task CorrectTime(int user,int issue,int entry,string token,DateTime date,decimal hours,string activity,string note)=>WriteIssues(user,[issue],async w=>
    {
        var parent=await RequireIssue(w,user,issue,"time.log");
        var row=await w.Find("TimeEntries",entry);if(row is null||Int(row,"IssueId")!=issue)throw new UnauthorizedAccessException();
        if(Int(row,"UserId")!=user&&!await Manage(w,user,Int(parent,"ProjectId")))throw new UnauthorizedAccessException();
        var before=new EditableTime(entry,issue,Int(row,"UserId"),Date(row,"WorkDate")!.Value,Number(row,"Hours"),Text(row,"Activity"),Text(row,"Note"));
        if(before.Token!=token)throw new InvalidOperationException("Unos vremena je promijenjen. Ponovno učitaj stranicu.");
        Set(row,"WorkDate",date.Date);Set(row,"Hours",hours);Set(row,"Activity",activity);Set(row,"Note",note);
        return await Event(w,issue,user,"time",IssueStore.TimeCorrectionAudit(before,date,hours,activity,note));
    });
}

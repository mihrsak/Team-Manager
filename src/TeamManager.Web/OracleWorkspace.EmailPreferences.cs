using OracleConnection = TeamManager.Web.Data.RelationalConnection;
using OracleTransaction = TeamManager.Web.Data.RelationalTransaction;
using OracleCommand = TeamManager.Web.Data.RelationalCommand;
using OracleDataReader = TeamManager.Web.Data.RelationalReader;
using System.Net.Mail;
using static TeamManager.Web.OracleWorkspace.Workspace;

namespace TeamManager.Web;

public sealed partial class OracleWorkspace
{
    internal Task<EmailNotificationPreferences> MailPreferences(int user) => Read<EmailNotificationPreferences>(async w =>
    {
        var row = (await w.By("EmailNotificationPreferences", "UserId", user)).FirstOrDefault();
        return row is null ? new(null, false, false) : new(row["Address"]?.GetValue<string>(), Flag(row, "SendWatchedIssues"), Flag(row, "SendDueReminders"));
    });

    internal Task SaveMailPreferences(int user, string? address, bool watched, bool reminders) => WriteUser(user, async w =>
    {
        if (address is null && (watched || reminders) || address is not null && (address.Length > 254 || address.Any(char.IsControl) || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address))
            throw new ArgumentException("Unesi valjanu adresu e-pošte za obavijesti.");
        var row = (await w.By("EmailNotificationPreferences", "UserId", user)).FirstOrDefault();
        var oldAddress = row?["Address"]?.GetValue<string>();
        row ??= w.Add("EmailNotificationPreferences", ("UserId", user));
        Set(row, "Address", address); Set(row, "SendWatchedIssues", watched); Set(row, "SendDueReminders", reminders);
        if (!watched || oldAddress != address) await w.RemoveBy("IssueEmailQueue", "UserId", user, q => q["CompletedAt"] is null);
        if (!reminders || oldAddress != address) await w.RemoveBy("IssueReminderQueue", "UserId", user, q => q["CompletedAt"] is null);
        return true;
    });
}

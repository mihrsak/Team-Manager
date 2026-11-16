using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace TeamManager.Web;

public sealed record ImapImportStatus(DateTimeOffset CheckedAt, bool Healthy, int? LastIssueId);

public sealed class ImapImportMonitor
{
    private readonly ConcurrentDictionary<int,ImapImportStatus> statuses = new();

    public ImapImportStatus? Get(int project) => statuses.GetValueOrDefault(project);

    public void Record(int project, bool healthy, int? issue = null) =>
        statuses.AddOrUpdate(project,
            _ => new(DateTimeOffset.UtcNow,healthy,issue),
            (_, previous) => new(DateTimeOffset.UtcNow,healthy,issue ?? previous.LastIssueId));
}

public sealed record ImapMailbox(string Host,int Port,string Username,string Password,string Folder,string EmployeeNumber)
{
    public static ImapMailbox? Configured(IConfiguration section)
    {
        if (!section.GetValue<bool>("Enabled")) return null;
        var host = section["Host"];
        var username = section["Username"];
        var password = section["Password"];
        var folder = section["Folder"];
        var employee = section["EmployeeNumber"];
        var port = section.GetValue("Port",993);
        return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(folder) ||
            string.IsNullOrWhiteSpace(employee) || port is < 1 or > 65535
            ? null : new(host,port,username,password,folder,employee);
    }
}

public sealed class ImapIssueImporter(IConfiguration configuration, IServiceScopeFactory scopes,
    ImapImportMonitor monitor, ILogger<ImapIssueImporter> logger) : BackgroundService
{
    private const int MaxMessageBytes = 1024 * 1024;
    private const int BatchSize = 50;
    private readonly Dictionary<int,UniqueId> lastExamined = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var polling = configuration.GetSection("MailIntake:Imap");
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("MailIntake:PollSeconds",60),5,600));
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var section in polling.GetChildren())
            {
                if (!int.TryParse(section.Key,out var project) || project <= 0 ||
                    ImapMailbox.Configured(section) is not { } mailbox) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                try
                {
                    await Import(project,mailbox,timeout.Token);
                    monitor.Record(project,true);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    monitor.Record(project,false);
                    logger.LogWarning("IMAP issue import for project {ProjectId} failed ({FailureType}).",
                        project,ex.GetType().Name);
                }
            }
            await Task.Delay(interval,stoppingToken);
        }
    }

    private async Task Import(int project, ImapMailbox mailbox, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserStore>();
        var issues = scope.ServiceProvider.GetRequiredService<IssueStore>();
        var actor = await users.Find(mailbox.EmployeeNumber);
        if (actor is not { IsActive: true } ||
            !await issues.Can(actor.Id,project,"issues.create") || !await issues.Can(actor.Id,project,"issues.view"))
            throw new UnauthorizedAccessException();

        using var client = new ImapClient();
        await client.ConnectAsync(mailbox.Host,mailbox.Port,SecureSocketOptions.SslOnConnect,cancellationToken);
        await client.AuthenticateAsync(mailbox.Username,mailbox.Password,cancellationToken);
        var folder = mailbox.Folder.Equals("INBOX",StringComparison.OrdinalIgnoreCase)
            ? client.Inbox : await client.GetFolderAsync(mailbox.Folder,cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite,cancellationToken);
        var unread = await folder.SearchAsync(SearchQuery.NotSeen.And(SearchQuery.SmallerThan(MaxMessageBytes+1)),cancellationToken);
        var previous = lastExamined.GetValueOrDefault(project);
        var ordered = unread.OrderBy(uid => uid.Id);
        var batch = ordered.Where(uid => uid.Id > previous.Id)
            .Concat(ordered.Where(uid => uid.Id <= previous.Id)).Take(BatchSize);

        foreach (var uid in batch)
        {
            lastExamined[project] = uid;
            var message = await folder.GetMessageAsync(uid,cancellationToken);
            var input = ToIssue(message);
            if (input is null) continue;

            var messageId = message.Headers[HeaderId.MessageId]?.Trim();
            if (string.IsNullOrEmpty(messageId) || messageId.Length > 512)
                messageId = $"{mailbox.Host}:{mailbox.Port}:{mailbox.Username}:{folder.FullName}:{folder.UidValidity}:{uid.Id}";
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(messageId));
            var id = await issues.ImportMail(actor.Id,project,digest,input);
            if (id is not null) monitor.Record(project,true,id);
            await folder.AddFlagsAsync(uid,MessageFlags.Seen,true,cancellationToken);
        }
        await client.DisconnectAsync(true,cancellationToken);
    }

    internal static IssueInput? ToIssue(MimeMessage message)
    {
        var subject = message.Subject?.Trim();
        var body = message.TextBody?.Trim();
        if (subject is null or { Length: 0 or > 200 } || subject.Any(char.IsControl) ||
            body is null or { Length: > 4000 }) return null;
        return new IssueInput { Title=subject,Description=body,IsPrivate=true };
    }
}

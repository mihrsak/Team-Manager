param([string]$ApplicationDll='artifacts/smtp-preview/TeamManager.Web.dll')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dll=(Resolve-Path $ApplicationDll).Path
$db='TeamManager_SmtpTest_'+[guid]::NewGuid().ToString('N')
$process=$null
$log=Join-Path ([IO.Path]::GetTempPath()) "$db.log"
$previous=@{}
function Set-TestVariable($name,$value) {
    $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Sql($database,$query) {
    $output=sqlcmd -S 'localhost\SQLEXPRESS' -E -C -I -b -h -1 -W -d $database -Q "SET NOCOUNT ON; $query"
    if($LASTEXITCODE) { throw ($output -join [Environment]::NewLine) }
    $output
}
function Assert($condition,$message) {
    if(!$condition) { throw $message }
    "PASS: $message"
}
Add-Type @'
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class TestSmtpReceiver : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private volatile bool stopping;
    private int rejected;
    public readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public int Rejected { get { return rejected; } }

    public TestSmtpReceiver()
    {
        listener.Start();
        Task.Run(() => Serve());
    }

    private async Task Serve()
    {
        while (!stopping)
        {
            try
            {
                using (var client = await listener.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII))
                using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                {
                    await writer.WriteLineAsync("220 localhost Test SMTP");
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase) &&
                            Interlocked.CompareExchange(ref rejected, 1, 0) == 0)
                            await writer.WriteLineAsync("451 Temporary test failure");
                        else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("354 End with a dot");
                            var message = new StringBuilder();
                            while ((line = await reader.ReadLineAsync()) != null && line != ".")
                                message.AppendLine(line);
                            if (line == null) break;
                            Messages.Enqueue(message.ToString());
                            await writer.WriteLineAsync("250 Queued");
                        }
                        else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("221 Bye");
                            break;
                        }
                        else await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { if (stopping) return; throw; }
            catch (ObjectDisposedException) { if (stopping) return; throw; }
        }
    }

    public void Dispose() { stopping = true; listener.Stop(); }
}
'@
$smtp=[TestSmtpReceiver]::new()
try {
    Set-TestVariable 'ConnectionStrings__TeamManager' "Server=localhost\SQLEXPRESS;Database=$db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    Set-TestVariable 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-TestVariable 'Smtp__Enabled' 'true'
    Set-TestVariable 'Smtp__Host' '127.0.0.1'
    Set-TestVariable 'Smtp__Port' "$($smtp.Port)"
    Set-TestVariable 'Smtp__UseTls' 'false'
    Set-TestVariable 'Smtp__From' 'sender@example.test'
    Set-TestVariable 'Smtp__BaseUrl' 'https://localhost:7043'
    Set-TestVariable 'Smtp__ReminderTimeZone' 'UTC'
    Set-TestVariable 'Smtp__PollSeconds' '1'
    Set-TestVariable 'Smtp__RetryBaseSeconds' '1'
    dotnet $dll --apply-schema --create-database | Out-Null
    if($LASTEXITCODE){throw 'Migration failed'}
    Sql $db @'
        DECLARE @admin int=(SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=N'admin');
        INSERT dbo.AppUsers(EmployeeNumber,DisplayName,PasswordHash,UiLanguage)
        VALUES(N'smtp-watch',N'SMTP Watcher',N'unused','en'),(N'smtp-reminder',N'SMTP Assignee',N'unused','hr');
        DECLARE @watch int=(SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=N'smtp-watch');
        DECLARE @reminder int=(SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=N'smtp-reminder');
        INSERT dbo.EmailNotificationPreferences(UserId,Address,SendWatchedIssues,SendDueReminders)
        VALUES(@watch,N'watch@example.test',1,0),(@reminder,N'reminder@example.test',0,1);
        INSERT dbo.Projects(Name,IsPublic) VALUES(N'SMTP test project',1);
        DECLARE @project int=SCOPE_IDENTITY();
        INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,CreatedBy,AssigneeId,DueDate)
        VALUES(@project,N'Test delivery',N'',N'Zadatak',N'Normalan',N'Novo',@admin,@reminder,CONVERT(date,SYSUTCDATETIME()));
        DECLARE @issue int=SCOPE_IDENTITY();
        INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,CreatedBy,IsPrivate)
        VALUES(@project,N'Private test delivery',N'',N'Zadatak',N'Normalan',N'Novo',@admin,1);
        DECLARE @private int=SCOPE_IDENTITY();
        INSERT dbo.IssueWatchers(IssueId,UserId) VALUES(@issue,@watch),(@private,@watch);
        INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) VALUES(@issue,@admin,'comment',N'Test');
        DECLARE @event int=SCOPE_IDENTITY();
        INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) VALUES(@private,@admin,'comment',N'Private');
        DECLARE @privateEvent int=SCOPE_IDENTITY();
        INSERT dbo.IssueNotifications(EventId,IssueId,UserId) VALUES(@event,@issue,@watch),(@privateEvent,@private,@watch);
        INSERT dbo.IssueEmailQueue(UserId,EventId) VALUES(@watch,@event),(@watch,@privateEvent);
'@ | Out-Null
    $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('"'+$dll+'"','--urls','https://localhost:7543') -WorkingDirectory (Join-Path $root 'src/TeamManager.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput $log
    for($i=0;$i -lt 30 -and $smtp.Messages.Count -lt 2;$i++){Start-Sleep -Seconds 1}
    Assert ($smtp.Messages.Count -eq 2) 'SMTP receives watched-issue activity and a due-date reminder'
    $messages=$smtp.Messages.ToArray() -join [Environment]::NewLine
    Assert ($messages.Contains('Activity on a watched issue') -and $messages.Contains('Zadatak dospijeva danas')) 'SMTP subjects follow recipient language'
    $messageText=[regex]::Replace($messages,'=\r?\n','').Replace('=3D','=')
    Assert ($messageText.Contains('https://localhost:7043/Issue?id=')) 'SMTP messages use the configured application link'
    Assert ($smtp.Rejected -eq 1 -and (Sql $db 'SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE Attempts=1 AND CompletedAt IS NOT NULL') -eq '1') 'A temporary SMTP failure is retried and recorded'
    Start-Sleep -Seconds 3
    Assert ((Sql $db 'SELECT COUNT(*) FROM dbo.IssueReminderQueue WHERE CompletedAt IS NOT NULL') -eq '1' -and $smtp.Messages.Count -eq 2) 'Repeated polling does not duplicate reminders'
    Assert ((Sql $db 'SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE CompletedAt IS NOT NULL') -eq '2') 'Inaccessible queued notification is discarded without delivery'
} catch {
    if(Test-Path -LiteralPath $log){Get-Content -Tail 16 -LiteralPath $log}
    throw
} finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit()}
    $smtp.Dispose()
    if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log}
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    if($db -match '^TeamManager_SmtpTest_[0-9a-f]{32}$'){
        Sql master "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
    }
}

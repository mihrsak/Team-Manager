param([string]$ApplicationDll='artifacts/imap-preview/TeamManager.Web.dll')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dll=(Resolve-Path $ApplicationDll).Path
$db='TeamManager_ImapTest_'+[guid]::NewGuid().ToString('N')
$log=Join-Path ([IO.Path]::GetTempPath()) "$db.log"
$process=$null
$previous=@{}
function Set-TestVariable($name,$value) {
    $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Sql($database,$query) {
    $output=sqlcmd -S 'localhost\SQLEXPRESS' -E -C -I -b -h -1 -W -d $database -Q "SET NOCOUNT ON; $query"
    if($LASTEXITCODE){throw ($output -join [Environment]::NewLine)}
    $output
}

Add-Type @'
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

public sealed class TestImapMailbox : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly X509Certificate2 certificate;
    private volatile bool stopping;
    private volatile bool seen;
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public bool Seen { get { return seen; } }
    public string LastCommand { get; private set; }
    private const string Message =
        "From: sender@example.test\r\nTo: mailbox@example.test\r\nMessage-ID: <imap-test@example.test>\r\n" +
        "Subject: QA IMAP issue\r\nDate: Tue, 01 Dec 2026 12:00:00 +0000\r\n" +
        "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\nUExBSU4gdGV4dCBmcm9tIElNQVA=\r\n";

    public TestImapMailbox(X509Certificate2 certificate)
    {
        this.certificate = certificate;
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
                using (var stream = new SslStream(client.GetStream()))
                {
                    await stream.AuthenticateAsServerAsync(certificate);
                    using (var reader = new StreamReader(stream, Encoding.ASCII))
                    using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                    {
                        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1] Test IMAP");
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var space = line.IndexOf(' ');
                            if (space < 1) break;
                            var tag = line.Substring(0, space);
                            var command = line.Substring(space + 1).ToUpperInvariant();
                            LastCommand = command.Split(' ')[0];
                            if (command == "CAPABILITY") await writer.WriteLineAsync("* CAPABILITY IMAP4rev1");
                            else if (command.StartsWith("LIST"))
                            {
                                if (command.EndsWith("\"\""))
                                    await writer.WriteLineAsync("* LIST (\\Noselect) \"/\" \"\"");
                                else await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" INBOX");
                            }
                            else if (command.StartsWith("SELECT"))
                            {
                                await writer.WriteLineAsync("* FLAGS (\\Seen)");
                                await writer.WriteLineAsync("* 1 EXISTS");
                                await writer.WriteLineAsync("* 0 RECENT");
                                await writer.WriteLineAsync("* OK [UIDVALIDITY 1]");
                                await writer.WriteLineAsync("* OK [UIDNEXT 2]");
                                await writer.WriteLineAsync(tag + " OK [READ-WRITE] Selected");
                                continue;
                            }
                            else if (command.StartsWith("UID SEARCH")) await writer.WriteLineAsync(seen ? "* SEARCH" : "* SEARCH 1");
                            else if (command.StartsWith("UID FETCH"))
                            {
                                await writer.WriteLineAsync("* 1 FETCH (UID 1 BODY[] {" + Message.Length + "}");
                                await writer.WriteAsync(Message);
                                await writer.WriteLineAsync(")");
                            }
                            else if (command.StartsWith("UID STORE")) seen = true;
                            else if (command == "LOGOUT")
                            {
                                await writer.WriteLineAsync("* BYE");
                                await writer.WriteLineAsync(tag + " OK Logout");
                                break;
                            }
                            else if (!command.StartsWith("LOGIN"))
                            {
                                await writer.WriteLineAsync(tag + " BAD Unsupported test command");
                                continue;
                            }
                            await writer.WriteLineAsync(tag + " OK Done");
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (System.Security.Authentication.AuthenticationException) { }
            catch (SocketException) { if (stopping) return; throw; }
            catch (ObjectDisposedException) { if (stopping) return; throw; }
        }
    }
    public void Dispose() { stopping = true; listener.Stop(); }
}
'@

# Use the HTTPS development certificate already trusted on this workstation.
$cert=Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.HasPrivateKey -and $_.NotAfter -gt [DateTime]::Now -and
    ($_.Extensions | Where-Object {$_.Oid.Value -eq '1.3.6.1.4.1.311.84.1.1'})
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if(!$cert){throw 'A trusted local HTTPS development certificate is required'}
$mailbox=[TestImapMailbox]::new($cert)
try {
    Set-TestVariable 'ConnectionStrings__TeamManager' "Server=localhost\SQLEXPRESS;Database=$db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    Set-TestVariable 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-TestVariable 'MailIntake__PollSeconds' '5'
    Set-TestVariable 'MailIntake__Imap__1__Enabled' 'true'
    Set-TestVariable 'MailIntake__Imap__1__Host' 'localhost'
    Set-TestVariable 'MailIntake__Imap__1__Port' "$($mailbox.Port)"
    Set-TestVariable 'MailIntake__Imap__1__Username' 'mailbox@example.test'
    Set-TestVariable 'MailIntake__Imap__1__Password' 'local-test-only'
    Set-TestVariable 'MailIntake__Imap__1__Folder' 'INBOX'
    Set-TestVariable 'MailIntake__Imap__1__EmployeeNumber' 'admin'
    dotnet $dll --apply-schema --create-database | Out-Null
    if($LASTEXITCODE){throw 'Migration failed'}
    Sql $db "INSERT dbo.Projects(Name) VALUES(N'IMAP test project');" | Out-Null
    $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('"'+$dll+'"','--urls','https://localhost:7544') -WorkingDirectory (Join-Path $root 'src/TeamManager.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput $log
    for($i=0;$i -lt 25 -and !$mailbox.Seen;$i++){Start-Sleep -Seconds 1}
    if(!$mailbox.Seen){throw "IMAP message was not acknowledged. Last command: $($mailbox.LastCommand)"}
    $count=Sql $db "SELECT COUNT(*) FROM dbo.Issues WHERE ProjectId=1 AND IsPrivate=1 AND Title=N'QA IMAP issue' AND Description=N'PLAIN text from IMAP';"
    if($count -ne '1'){throw 'Message did not create the expected private issue'}
    'PASS: TLS IMAP mail creates a private issue with decoded MIME text'
    $count=Sql $db 'SELECT COUNT(*) FROM dbo.ReceivedIssueMail'
    if($count -ne '1'){throw 'Message receipt was not persisted'}
    'PASS: Mailbox acknowledges delivery after the issue and receipt commit'
} catch {
    if(Test-Path -LiteralPath $log){Get-Content -Tail 14 -LiteralPath $log}
    throw
} finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit()}
    $mailbox.Dispose()
    if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log}
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    if($db -match '^TeamManager_ImapTest_[a-f0-9]{32}$'){
        Sql master "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
    }
}

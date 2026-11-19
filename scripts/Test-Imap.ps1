param(
    [string]$ApplicationDll,
    [switch]$Preview
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$ApplicationDll){
    $outputDirectory=Join-Path $root 'artifacts/imap-test'
    dotnet build (Join-Path $root 'src/TeamManager.Web/TeamManager.Web.csproj') -o $outputDirectory
    if($LASTEXITCODE){throw 'Build failed'}
    $ApplicationDll=Join-Path $outputDirectory 'TeamManager.Web.dll'
}
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

Add-Type -Path (Join-Path $PSScriptRoot 'test-support/TestImapMailbox.cs')

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
    Set-TestVariable 'Smtp__Enabled' 'false'
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
    if($Preview){
        $url='https://localhost:7544/MailIntake?projectId=1'
        Invoke-WebRequest 'https://localhost:7544/health/ready' | Out-Null
        Write-Host "Local preview: $url"
        Write-Host 'Sign in to the temporary database with admin / admin.'
        Write-Host 'Open the last imported issue from the Incoming email page.'
        Start-Process $url
        [void](Read-Host 'Press Enter to stop the preview and remove the temporary database')
    }
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

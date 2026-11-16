param(
    [Parameter(Mandatory)][string]$PreviousPackage,
    [Parameter(Mandatory)][string]$CurrentPackage,
    [string]$BackupDirectory,
    [ValidateRange(1024,65535)][int]$Port=7545
)
$ErrorActionPreference='Stop'
$previous=(Resolve-Path -LiteralPath $PreviousPackage).Path
$current=(Resolve-Path -LiteralPath $CurrentPackage).Path
if($previous -eq $current){throw 'Specify two different packages.'}
$db='TeamManager_RollbackTest_'+[guid]::NewGuid().ToString('N')
$backup=$null
$base="https://localhost:$Port"
$variables=@{}
$server='Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$connection=[System.Data.SqlClient.SqlConnection]::new("$server;Database=master")

function Set-TestVariable($name,$value){
    $variables[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Sql($query){
    $command=$connection.CreateCommand()
    $command.CommandTimeout=120
    $command.CommandText=$query
    try { $command.ExecuteScalar() } finally { $command.Dispose() }
}
function Test-PackageProject($package){
    $process=Start-Process -FilePath (Join-Path $package 'TeamManager.Web.exe') -WorkingDirectory $package -ArgumentList @('--urls',$base) -PassThru -WindowStyle Hidden
    try {
        $session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
        $login=$null
        for($attempt=0;$attempt -lt 20 -and !$login;$attempt++){
            if($process.HasExited){throw "Package exited with code $($process.ExitCode)."}
            try { $login=Invoke-WebRequest "$base/Login" -WebSession $session -TimeoutSec 2 }
            catch { Start-Sleep -Seconds 1 }
        }
        if(!$login){throw 'Package did not serve its login page.'}
        $token=[regex]::Match($login.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
        if(!$token.Success){throw 'Login antiforgery token is missing.'}
        Invoke-WebRequest "$base/Login" -WebSession $session -Method Post -Body @{
            EmployeeNumber='admin'
            Password='admin'
            __RequestVerificationToken=[Net.WebUtility]::HtmlDecode($token.Groups[1].Value)
        } | Out-Null
        $projects=Invoke-WebRequest "$base/Projects" -WebSession $session
        if(!$projects.Content.Contains('Rollback project QA')){throw 'Signed-in project page does not show the test project.'}
    } finally {
        if(!$process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit()}
    }
}

try {
    if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw "Port $Port is in use."}
    Set-TestVariable 'ConnectionStrings__TeamManager' "$server;Database=$db"
    Set-TestVariable 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-TestVariable 'Smtp__Enabled' 'false'
    Set-TestVariable 'MailIntake__Imap__1__Enabled' 'false'
    $connection.Open()
    if(!$BackupDirectory){
        $defaultBackupDirectory=Sql "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));"
        if(!$defaultBackupDirectory -or $defaultBackupDirectory -is [DBNull]){throw 'SQL Server default backup directory is unavailable.'}
        $BackupDirectory=$defaultBackupDirectory
    }
    if(![IO.Path]::IsPathFullyQualified($BackupDirectory)){throw 'Use an absolute backup directory on the SQL Server machine.'}
    $backup=[IO.Path]::Combine($BackupDirectory,"$db.bak")
    $backupSql=$backup.Replace("'","''")
    & (Join-Path $previous 'Install-Database.ps1') -CreateDatabase
    Sql "INSERT [$db].dbo.Projects(Name) VALUES(N'Rollback project QA');" | Out-Null
    Test-PackageProject $previous
    Sql "BACKUP DATABASE [$db] TO DISK=N'$backupSql' WITH COPY_ONLY, CHECKSUM;
         RESTORE VERIFYONLY FROM DISK=N'$backupSql' WITH CHECKSUM;" | Out-Null
    & (Join-Path $current 'Install-Database.ps1')
    Test-PackageProject $current
    Sql "UPDATE [$db].dbo.Projects SET Name=N'After upgrade QA' WHERE Name=N'Rollback project QA';" | Out-Null
    Sql "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
         RESTORE DATABASE [$db] FROM DISK=N'$backupSql' WITH REPLACE, CHECKSUM;
         ALTER DATABASE [$db] SET MULTI_USER;" | Out-Null
    Test-PackageProject $previous
    if((Sql "SELECT COUNT(*) FROM [$db].dbo.Projects WHERE Name=N'After upgrade QA'") -ne 0){
        throw 'Restored database contains post-backup changes.'
    }
    Write-Host 'PASS: Upgraded package works, and the previous package signs in and displays the restored project.'
} finally {
    try {
        if($connection.State -eq 'Open'){
            Sql "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
        }
    } finally {
        $connection.Dispose()
        foreach($name in $variables.Keys){[Environment]::SetEnvironmentVariable($name,$variables[$name],'Process')}
        if($backup){
            try { [IO.File]::Delete($backup) }
            catch { Write-Warning "Could not remove the test backup at $backup. A user with access to the SQL backup directory can remove it." }
        }
    }
}

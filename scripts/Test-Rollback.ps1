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
$restoredDb=$db+'_Restored'
$timer=[Diagnostics.Stopwatch]::StartNew()
$backup=$null
$base="https://localhost:$Port"
$variables=@{}
$server='Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$connection=[System.Data.SqlClient.SqlConnection]::new("$server;Database=master")

function Set-TestVariable($name,$value){
    if(!$variables.ContainsKey($name)){$variables[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Data-Fingerprints($database){
    $result=@{}
    # Canonical row order and explicit NULLs; SQL JSON includes binary contents as base64.
    foreach($table in @('Projects','AppUsers','Issues','IssueEvents','TimeEntries','IssueAttachments')){
        $result[$table]=Sql "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM [$database].dbo.[$table] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES)),2)"
    }
    return $result
}
function Assert-Fingerprints($database,$expected){
    $actual=Data-Fingerprints $database
    foreach($table in $expected.Keys){if($actual[$table] -ne $expected[$table]){throw "Restored data mismatch: $table"}}
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
        if((Invoke-WebRequest "$base/health/ready" -TimeoutSec 5).StatusCode -ne 200){throw 'Readiness failed.'}
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
    Set-TestVariable 'Database__Provider' 'SqlServer'
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
    & (Join-Path $previous 'Install-Database.ps1') -CreateDatabase -Demo
    Sql "INSERT [$db].dbo.Projects(Name) VALUES(N'Rollback project QA');" | Out-Null
    Sql "DECLARE @issue int=(SELECT MIN(Id) FROM [$db].dbo.Issues), @user int=(SELECT MIN(Id) FROM [$db].dbo.AppUsers);
        INSERT [$db].dbo.IssueEvents(IssueId,UserId,Kind,Body) VALUES(@issue,@user,'comment',N'Obnova: čćžšđ — 日本語');
        INSERT [$db].dbo.TimeEntries(IssueId,UserId,WorkDate,Hours,Note) VALUES(@issue,@user,'2026-11-26',1.25,N'Prije sigurnosne kopije');
        INSERT [$db].dbo.IssueAttachments(IssueId,AuthorId,FileName,Content) VALUES(@issue,@user,N'obnova.bin',0x0001027F80FEFF);" | Out-Null
    Test-PackageProject $previous
    $expected=Data-Fingerprints $db
    Sql "BACKUP DATABASE [$db] TO DISK=N'$backupSql' WITH COPY_ONLY, CHECKSUM;
         RESTORE VERIFYONLY FROM DISK=N'$backupSql' WITH CHECKSUM;" | Out-Null
    Write-Host 'PASS: COPY_ONLY/CHECKSUM backup passes verification.'
    # Restore to a new database with separate physical files, leaving the source untouched.
    $command=$connection.CreateCommand();$command.CommandText="RESTORE FILELISTONLY FROM DISK=N'$backupSql'"
    $files=[Data.DataTable]::new()
    try{$reader=$command.ExecuteReader();try{$files.Load($reader)}finally{$reader.Dispose()}}finally{$command.Dispose()}
    $moves=@();$index=0
    foreach($file in $files.Rows){
        if($file.Type -notin @('D','L')){throw 'Only data and log files are supported by this local restore test.'}
        $index++
        $extension=if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}
        $physical=[IO.Path]::Combine([IO.Path]::GetDirectoryName($file.PhysicalName),"${restoredDb}_${index}$extension")
        $moves+="MOVE N'$($file.LogicalName.Replace("'","''"))' TO N'$($physical.Replace("'","''"))'"
    }
    Sql "RESTORE DATABASE [$restoredDb] FROM DISK=N'$backupSql' WITH CHECKSUM, $($moves -join ', ');" | Out-Null
    Assert-Fingerprints $restoredDb $expected
    Assert-Fingerprints $db $expected
    Write-Host 'PASS: Separate restore matches six table fingerprints, including Unicode, passwords, history, time and binary attachment; source is unchanged.'
    Set-TestVariable 'ConnectionStrings__TeamManager' "$server;Database=$restoredDb"
    Test-PackageProject $previous
    Set-TestVariable 'ConnectionStrings__TeamManager' "$server;Database=$db"
    Write-Host 'PASS: Previous package signs in and serves the separately restored database with readiness 200.'
    & (Join-Path $current 'Install-Database.ps1')
    Test-PackageProject $current
    Sql "UPDATE [$db].dbo.Projects SET Name=N'After upgrade QA' WHERE Name=N'Rollback project QA';" | Out-Null
    Sql "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
         RESTORE DATABASE [$db] FROM DISK=N'$backupSql' WITH REPLACE, CHECKSUM;
         ALTER DATABASE [$db] SET MULTI_USER;" | Out-Null
    Assert-Fingerprints $db $expected
    Test-PackageProject $previous
    if((Sql "SELECT COUNT(*) FROM [$db].dbo.Projects WHERE Name=N'After upgrade QA'") -ne 0){
        throw 'Restored database contains post-backup changes.'
    }
    Write-Host 'PASS: Upgraded package works, and the previous package signs in and displays the restored project.'
    Write-Host "Local recovery exercise completed in $([Math]::Round($timer.Elapsed.TotalSeconds,1)) seconds; not a production recovery-time estimate."
} finally {
    try {
        if($connection.State -eq 'Open'){
            foreach($testDb in @($restoredDb,$db)){
                if($testDb -notmatch '^TeamManager_RollbackTest_[0-9a-f]{32}(_Restored)?$'){throw 'Unsafe cleanup database name.'}
                Sql "IF DB_ID(N'$testDb') IS NOT NULL BEGIN ALTER DATABASE [$testDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$testDb]; END" | Out-Null
            }
        }
    } finally {
        $connection.Dispose()
        foreach($name in $variables.Keys){[Environment]::SetEnvironmentVariable($name,$variables[$name],'Process')}
        if($backup){
            try { [IO.File]::Delete($backup) }
            catch {
                # SQL owns its default backup directory. Delete only this run's exact file,
                # without wildcards or enabling an operating-system shell on the server.
                $cleanup=[System.Data.SqlClient.SqlConnection]::new("$server;Database=master")
                try{
                    if([IO.Path]::GetFileName($backup) -ne "$db.bak" -or $backup.IndexOfAny([char[]]'*?') -ge 0){throw 'Unsafe backup cleanup path.'}
                    $cleanup.Open();$command=$cleanup.CreateCommand()
                    try{
                        $command.CommandText='DECLARE @result int; EXEC @result=master.sys.xp_delete_files @path; SELECT @result;'
                        $null=$command.Parameters.AddWithValue('@path',$backup)
                        if([int]$command.ExecuteScalar() -ne 0){throw 'SQL backup cleanup failed.'}
                    }finally{$command.Dispose()}
                }catch{Write-Warning "Could not remove this run's test backup at $backup. A user with access to the SQL backup directory can remove it."}
                finally{$cleanup.Dispose()}
            }
        }
    }
}

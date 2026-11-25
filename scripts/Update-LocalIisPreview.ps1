#Requires -Version 5.1
#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][string]$PackagePath,[Parameter(Mandatory=$true)][string]$ResultPath)
$ErrorActionPreference='Stop'
Import-Module WebAdministration
$site='TeamManager-LocalTest'
$root=[IO.Path]::GetFullPath('C:\inetpub\TeamManager-LocalTest')
$package=(Resolve-Path -LiteralPath $PackagePath).Path
$result=[IO.Path]::GetFullPath($ResultPath)
$destination=Join-Path $root ('review-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$previous=$null
$backup=$null
$poolStopped=$false
$maintenanceStarted=$false
function Status($state,$message){@{State=$state;Message=$message;Previous=$previous;Current=$destination;Backup=$backup;Url='https://localhost:7443';At=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $result -Encoding UTF8}
function Sql($query){$answer=& sqlcmd -S 'localhost\SQLEXPRESS' -E -C -b -h -1 -W -Q "SET NOCOUNT ON; $query";if($LASTEXITCODE){throw 'SQL operation failed.'};return $answer}
try{
    Status 'checking' 'Checking dedicated local IIS site and package.'
    foreach($file in @('TeamManager.Web.exe','web.config','Install-Database.ps1')){if(!(Test-Path -LiteralPath (Join-Path $package $file))){throw "Missing package file: $file"}}
    if(Test-Path -LiteralPath (Join-Path $package 'appsettings.Local.json')){throw 'Publish package must exclude local configuration.'}
    $website=Get-Item "IIS:\Sites\$site"
    $previous=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($website.physicalPath))
    if($website.applicationPool -ne $site -or !$previous.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or !$destination.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected local IIS target.'}
    $bindings=@(Get-WebBinding -Name $site)
    if($bindings.Count -eq 0 -or @($bindings | Where-Object {$_.protocol -ne 'https' -or $_.bindingInformation -notmatch '^(127\.0\.0\.1|\[::1\]):7443:$'}).Count){throw 'Expected loopback-only HTTPS bindings on port 7443.'}
    $configuration=Get-Content -LiteralPath (Join-Path $previous 'appsettings.Local.json') -Raw | ConvertFrom-Json
    $connection=[System.Data.SqlClient.SqlConnectionStringBuilder]::new([string]$configuration.ConnectionStrings.TeamManager)
    if($connection.DataSource -ne 'localhost\SQLEXPRESS' -or $connection.InitialCatalog -ne 'TeamManager_IisTest' -or !$connection.IntegratedSecurity){throw 'Only the dedicated local SQL test database is supported.'}
    $backupDirectory=((Sql "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'));" | Where-Object {$_ -and $_.Trim()}) -join '').Trim()
    if(!$backupDirectory){throw 'SQL Server backup directory unavailable.'}
    $backup=Join-Path $backupDirectory ('TeamManager_IisTest_pre_review_'+[guid]::NewGuid().ToString('N')+'.bak')
    $literal=$backup.Replace("'","''")
    Status 'backup' 'Creating and verifying a copy-only backup before migration.'
    Sql "BACKUP DATABASE [TeamManager_IisTest] TO DISK=N'$literal' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$literal' WITH CHECKSUM;" | Out-Null
    if(Test-Path -LiteralPath $destination){throw 'New deployment directory already exists.'}
    New-Item -ItemType Directory -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $package | Copy-Item -Destination $destination -Recurse
    Copy-Item -LiteralPath (Join-Path $previous 'appsettings.Local.json') -Destination (Join-Path $destination 'appsettings.Local.json')
    # Preserve any locally configured relative data/key directory as well as the
    # original pool identity/profile. Never replace secrets with publish defaults.
    foreach($directory in @('App_Data','keys','.keys')){if(Test-Path -LiteralPath (Join-Path $previous $directory)){Copy-Item -LiteralPath (Join-Path $previous $directory) -Destination $destination -Recurse}}
    & icacls $destination /grant "IIS APPPOOL\${site}:(OI)(CI)RX" | Out-Null
    if($LASTEXITCODE){throw 'Application pool package read access failed.'}
    $maintenanceStarted=$true
    Stop-WebAppPool -Name $site
    $poolStopped=$true
    for($attempt=0;$attempt -lt 30 -and (Get-WebAppPoolState -Name $site).Value -ne 'Stopped';$attempt++){Start-Sleep -Milliseconds 200}
    if((Get-WebAppPoolState -Name $site).Value -ne 'Stopped'){throw 'Application pool did not stop.'}
    Status 'migration' 'Applying idempotent schema migrations; existing data is retained.'
    & (Join-Path $destination 'Install-Database.ps1')
    Set-ItemProperty "IIS:\Sites\$site" physicalPath $destination
    Start-WebAppPool -Name $site
    $poolStopped=$false
    $ready=$false
    for($attempt=0;$attempt -lt 30 -and !$ready;$attempt++){try{$ready=(Invoke-WebRequest 'https://localhost:7443/health/ready' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200}catch{Start-Sleep -Milliseconds 300}}
    if(!$ready){throw 'Updated IIS readiness failed.'}
    $deployed=(Get-FileHash -LiteralPath (Join-Path $destination 'TeamManager.Web.dll') -Algorithm SHA256).Hash
    if($deployed -ne (Get-FileHash -LiteralPath (Join-Path $package 'TeamManager.Web.dll') -Algorithm SHA256).Hash){throw 'Deployed assembly hash mismatch.'}
    Status 'complete' 'IIS package updated, verified backup retained, HTTPS readiness 200.'
}catch{
    $failure=$_.Exception.Message
    # Keep both package directories and the verified backup for inspection. Only
    # restore the site pointer; never silently restore/delete database contents.
    if($previous -and $maintenanceStarted){try{if((Get-WebAppPoolState -Name $site).Value -ne 'Stopped'){Stop-WebAppPool -Name $site};Set-ItemProperty "IIS:\Sites\$site" physicalPath $previous;Start-WebAppPool -Name $site;$poolStopped=$false}catch{}}
    Status 'failed' $failure
    throw
}finally{if($poolStopped){Start-WebAppPool -Name $site}}

#Requires -Version 7.0
param([Parameter(Mandatory=$true)][ValidateSet('Oracle','Db2')][string]$Provider,[Parameter(Mandatory=$true)][string]$SourceSettings,[string]$Snapshot='artifacts/db2-source-snapshot.json')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$fixtureDirectory=Join-Path $repoRoot ('artifacts/admin-fixtures/'+$Provider.ToLowerInvariant()+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
$outputSettings=Join-Path $fixtureDirectory 'appsettings.Local.json'
$sourceSettingsPath=(Resolve-Path -LiteralPath $SourceSettings).Path
if($Provider -eq 'Oracle'){
    $source=Get-Content -LiteralPath $sourceSettingsPath -Raw | ConvertFrom-Json -AsHashtable
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new();$builder.set_ConnectionString([string]$source.ConnectionStrings.TeamManagerOracle)
    $sourceData=[string]$builder.get_Item('Data Source')
    if($sourceData -notmatch '^(localhost|127\.0\.0\.1):\d+/FREEPDB1$'){throw 'Only the local Oracle FREEPDB1 test container is allowed.'}
    $fixtureUser='TMR_ADMINQA_'+[guid]::NewGuid().ToString('N').Substring(0,12).ToUpperInvariant()
    $fixturePassword='Q'+[guid]::NewGuid().ToString('N')
    foreach($key in @('User Id','UserID','User')){if($builder.ContainsKey($key)){[void]$builder.Remove($key)}}
    $builder.set_Item('User Id',$fixtureUser);$builder.set_Item('Password',$fixturePassword)
    $privateSource=Join-Path $fixtureDirectory 'oracle-source.private.json'
    @{ConnectionStrings=@{TeamManagerOracle=$builder.get_ConnectionString()}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $privateSource
    $ddl=@"
whenever sqlerror exit failure
set echo off
alter session set container=FREEPDB1;
create user $fixtureUser identified by "$fixturePassword";
grant create session, create table, create view, create trigger, create sequence, create procedure, unlimited tablespace to $fixtureUser;
grant execute on sys.dbms_lock to $fixtureUser;
exit
"@
    $answer=$ddl | podman --connection podman-machine-default exec -i team-manager-oracle sqlplus -s / as sysdba 2>&1
    $ddlExit=$LASTEXITCODE
    ($answer | Out-String).Replace($fixturePassword,'<redacted>') | Set-Content (Join-Path $fixtureDirectory 'oracle-create.log')
    if($ddlExit){throw 'Local Oracle isolated administrator fixture creation failed; inspect its redacted log.'}
    $sourceSettingsPath=$privateSource
}
$helper=Join-Path $repoRoot 'scripts/admin-fixture/bin/Release/net10.0/AdminFixture.dll'
& dotnet $helper $Provider $sourceSettingsPath ((Resolve-Path -LiteralPath $Snapshot).Path) $outputSettings *> (Join-Path $fixtureDirectory 'install.log')
if($LASTEXITCODE){throw ('Isolated administrator fixture import failed; log: '+(Join-Path $fixtureDirectory 'install.log'))}
Write-Output $outputSettings

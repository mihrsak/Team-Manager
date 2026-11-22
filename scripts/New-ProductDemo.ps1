#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ApplicationDll,[Parameter(Mandatory=$true)][string]$Db2SettingsPath,[int]$Port=7610,[int]$SeedPort=7614)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dll=(Resolve-Path $ApplicationDll).Path
$nativeSettings=Get-Content (Resolve-Path $Db2SettingsPath) -Raw | ConvertFrom-Json
foreach($candidate in @($Port,$SeedPort)){if(Get-NetTCPConnection -LocalPort $candidate -State Listen -ErrorAction SilentlyContinue){throw "Port $candidate is occupied."}}
$suffix=[guid]::NewGuid().ToString('N')
$database='TeamManager_ProductDemo_'+$suffix
$schema='TMR_DEMO_'+$suffix.Substring(0,12).ToUpperInvariant()
$directory=Join-Path $root ('artifacts/product-demo-'+$suffix)
[void](New-Item -ItemType Directory -Path $directory)
$settingsPath=Join-Path $directory 'appsettings.Local.json'
$manifest=Join-Path $directory 'manifest.json'
$snapshot=Join-Path $directory 'snapshot-private.json'
$password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$settings=@{Database=@{Provider='SqlServer'};ConnectionStrings=@{TeamManager="Server=localhost\SQLEXPRESS;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"};Smtp=@{Enabled=$false};DataProtection=@{KeyDirectory=(Join-Path $directory keys)};Bootstrap=@{RequirePassword=$false}}
function Save-Settings {$settings | ConvertTo-Json -Depth 8 | Set-Content $settingsPath}
function Run-Command([string[]]$Arguments,[string]$Log){Push-Location $directory;try{dotnet $dll @Arguments *> (Join-Path $directory $Log);if($LASTEXITCODE){throw "Demo command failed; inspect $Log in the ignored demo directory."}}finally{Pop-Location}}
function Start-Preview([int]$PreviewPort,[string]$Label){$webroot=Join-Path (Split-Path $dll) wwwroot;$instance=Start-Process dotnet -ArgumentList @(('"'+$dll+'"'),'--urls',('https://localhost:'+$PreviewPort),'--webroot',('"'+$webroot+'"')) -WorkingDirectory $directory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory ($Label+'.log')) -RedirectStandardError (Join-Path $directory ($Label+'.err'));for($attempt=0;$attempt -lt 60;$attempt++){try{if((Invoke-WebRequest ('https://localhost:'+$PreviewPort+'/health/ready') -TimeoutSec 2).StatusCode -eq 200){return $instance}}catch{};Start-Sleep -Milliseconds 250};if(!$instance.HasExited){Stop-Process -Id $instance.Id};throw 'Demo readiness failed.'}
Save-Settings
Run-Command @('--apply-schema','--create-database') 'sql-schema.log'
$seed=$null
try{
 $seed=Start-Preview $SeedPort 'sql-seed'
 $env:NODE_EXTRA_CA_CERTS=Join-Path (Split-Path $dll) 'dev-ca.pem'
 $env:TEAM_MANAGER_ORACLE_URL='https://localhost:'+$SeedPort
 $env:TEAM_MANAGER_DEMO_SEED='isolated-new-database'
 $env:TEAM_MANAGER_DEMO_PASSWORD=$password
 $env:TEAM_MANAGER_DEMO_MANIFEST=$manifest
 node (Join-Path $PSScriptRoot 'Seed-ProductDemo.cjs') *> (Join-Path $directory seed-browser.log)
 if($LASTEXITCODE){throw 'Demo seed failed; inspect seed-browser.log.'}
 Run-Command @('--export-sql-relational',$snapshot) 'sql-export.log'
}finally{if($seed -and !$seed.HasExited){Stop-Process -Id $seed.Id -Force};Remove-Item Env:TEAM_MANAGER_DEMO_PASSWORD -ErrorAction SilentlyContinue;Remove-Item Env:TEAM_MANAGER_DEMO_SEED -ErrorAction SilentlyContinue}
$settings.Database.Provider='Db2'
$settings.ConnectionStrings.TeamManager='Server=127.0.0.1,1;User Id=unavailable;Password=unavailable;Connect Timeout=1;Encrypt=True;TrustServerCertificate=True'
$settings.ConnectionStrings.TeamManagerDb2=$nativeSettings.ConnectionStrings.TeamManagerDb2.TrimEnd(';')+';CurrentSchema='+$schema+';'
Save-Settings
Run-Command @('--create-local-db2-demo-schema',$schema) 'db2-create.log'
Run-Command @('--import-db2-relational',$snapshot) 'db2-import.log'
$preview=Start-Preview $Port 'demo'
$preview.Id | Set-Content (Join-Path $directory 'demo.pid')
@{Url=('https://localhost:'+$Port);Schema=$schema;SourceDatabase=$database;Directory=$directory;Accounts=@{Administrator=@{EmployeeNumber='admin';Password='admin'};Demo=@{EmployeeNumbers=@('demo.lead','demo.dev','demo.viewer');Password=$password}}} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory 'LocalDemoAccess.private.json')
Write-Output ('Demo URL: https://localhost:'+$Port)
Write-Output ('Demo manifest: '+$manifest)
Write-Output ('Local access details remain in the ignored demo directory.')

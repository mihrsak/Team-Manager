#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$Settings,[Parameter(Mandatory=$true)][int]$Port,[ValidateSet('baseline','final')][string]$Label='baseline')
$ErrorActionPreference='Stop'
$packageDirectory=(Resolve-Path -LiteralPath $Package).Path
$settingsPath=(Resolve-Path -LiteralPath $Settings).Path
$fixture=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if(!$fixture.AdminFixture.Isolated -or $fixture.AdminFixture.Schema -notmatch '^TMR_ADMINQA_[A-F0-9]{12}$'){throw 'Only a generated isolated administrator fixture is allowed.'}
if(($fixture.Database.Provider -eq 'Db2' -and $Port -ne 7633) -or ($fixture.Database.Provider -eq 'Oracle' -and $Port -ne 7634) -or $fixture.Database.Provider -notin @('Db2','Oracle')){throw 'Use dedicated admin fixture ports7633/7634.'}
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw 'Fixture port occupied.'}
$directory=Split-Path $settingsPath -Parent
$environment=@{Database__Provider=$fixture.Database.Provider;ConnectionStrings__TeamManager=$fixture.ConnectionStrings.TeamManager;Smtp__Enabled='false';Registration__Enabled='true';ASPNETCORE_ENVIRONMENT='Production'}
if($fixture.Database.Provider -eq 'Db2'){$environment.ConnectionStrings__TeamManagerDb2=$fixture.ConnectionStrings.TeamManagerDb2}else{$environment.ConnectionStrings__TeamManagerOracle=$fixture.ConnectionStrings.TeamManagerOracle}
$instance=Start-Process dotnet -ArgumentList @(('"'+(Join-Path $packageDirectory 'TeamManager.Web.dll')+'"'),'--urls',('https://localhost:'+$Port),'--webroot',('"'+(Join-Path $packageDirectory 'wwwroot')+'"')) -Environment $environment -WorkingDirectory $directory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory ($Label+'-server.log')) -RedirectStandardError (Join-Path $directory ($Label+'-server.err'))
$instance.Id | Set-Content (Join-Path $directory ($Label+'.pid'))
for($attempt=0;$attempt -lt 40;$attempt++){try{if((Invoke-WebRequest ('https://localhost:'+$Port+'/health/ready')).StatusCode -eq 200){Write-Output ($fixture.Database.Provider+' administrator fixture HTTPS ready');exit 0}}catch{};Start-Sleep -Milliseconds 300}
throw 'Isolated administrator fixture readiness failed.'

#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ApplicationDll,[string]$BrowserScript='Test-OracleRegistration.cjs',[int]$Port=7580)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dll=(Resolve-Path $ApplicationDll).Path
$db='TeamManager_AccountTest_'+[guid]::NewGuid().ToString('N')
$process=$null
$previous=@{}
$variables=@{
    Database__Provider='SqlServer'
    ConnectionStrings__TeamManager="Server=localhost\SQLEXPRESS;Database=$db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    ASPNETCORE_ENVIRONMENT='Production'
    Registration__Enabled='true'
    Smtp__Enabled='false'
    TEAM_MANAGER_ORACLE_URL="https://localhost:$Port"
    NODE_EXTRA_CA_CERTS=(Join-Path (Split-Path $dll) 'dev-ca.pem')
}
try {
    if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw 'Test port already in use.'}
    foreach($key in $variables.Keys){$previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$variables[$key],'Process')}
    dotnet $dll --apply-schema --create-database *> (Join-Path $root 'artifacts/account-sql-migration.log')
    if($LASTEXITCODE){throw 'Account SQL test migration failed.'}
    $process=Start-Process dotnet -ArgumentList @('"'+$dll+'"','--urls',"https://localhost:$Port") -WorkingDirectory (Split-Path $dll) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'artifacts/account-sql-server.log') -RedirectStandardError (Join-Path $root 'artifacts/account-sql-error.log')
    $ready=$false
    for($attempt=0;$attempt -lt 30 -and !$ready;$attempt++){try{$ready=(Invoke-WebRequest "https://localhost:$Port/health/ready" -TimeoutSec 2).StatusCode -eq 200}catch{Start-Sleep -Milliseconds 200}}
    if(!$ready){throw 'Account SQL test server did not become ready.'}
    node (Join-Path $PSScriptRoot $BrowserScript)
    if($LASTEXITCODE){throw 'Account SQL browser check failed.'}
} finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id -Force}
    # The generated database name cannot refer to an application/user database.
    if($db -match '^TeamManager_AccountTest_[0-9a-f]{32}$') {sqlcmd -S 'localhost\SQLEXPRESS' -E -C -b -Q "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null}
    foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')}
}

#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [ValidateRange(1024,65535)][int]$Port=7558
)
$ErrorActionPreference='Stop'
$package=(Resolve-Path -LiteralPath $PackagePath).Path
$exe=Join-Path $package 'TeamManager.Web.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Use a published Windows package.'}
if(Test-Path (Join-Path $package 'appsettings.Local.json')){throw 'Use a clean package without local configuration for this isolated test.'}
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw "Port $Port is in use."}
$db='TeamManager_HealthTest_'+[guid]::NewGuid().ToString('N')
$server='Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$connection=[System.Data.SqlClient.SqlConnection]::new("$server;Database=master")
$variables=@{}
$process=$null
$client=[Net.Http.HttpClient]::new()
$client.Timeout=[TimeSpan]::FromSeconds(8)
$base="https://localhost:$Port"
function Set-TestVariable($name,$value){
    $variables[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Sql($query){
    $cmd=$connection.CreateCommand();$cmd.CommandText=$query;$cmd.CommandTimeout=30
    try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
}
function Get-Health($route){
    $response=$client.GetAsync("$base/health/$route").GetAwaiter().GetResult()
    try{
        $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if($body.Contains($db) -or $body.Contains('SQLEXPRESS') -or $body.Contains('Exception')){throw 'Health response exposes internal details.'}
        return [int]$response.StatusCode
    }finally{$response.Dispose()}
}
function Wait-Health($route,$expected){
    for($i=0;$i -lt 30;$i++){
        if($process.HasExited){throw "Application exited: $($process.ExitCode)"}
        if((Get-Health $route) -eq $expected){return}
        Start-Sleep -Milliseconds 500
    }
    throw "Health $route did not return $expected."
}
try{
    Set-TestVariable 'ConnectionStrings__TeamManager' "$server;Database=$db"
    Set-TestVariable 'Database__Provider' 'SqlServer'
    Set-TestVariable 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-TestVariable 'Smtp__Enabled' 'false'
    $connection.Open()
    & (Join-Path $package 'Install-Database.ps1') -CreateDatabase
    $process=Start-Process -FilePath $exe -WorkingDirectory $package -ArgumentList @('--urls',$base) -WindowStyle Hidden -PassThru
    $started=$false
    for($i=0;$i -lt 30;$i++){
        if($process.HasExited){throw 'Application failed to start.'}
        try{$started=(Get-Health 'live') -eq 200}catch{$started=$false}
        if($started){break};Start-Sleep -Milliseconds 500
    }
    if(!$started){throw 'Liveness did not become available over trusted HTTPS.'}
    Wait-Health 'ready' 200
    Write-Host 'PASS: Clean package starts with HTTPS liveness and readiness 200.'
    # Only the generated, dedicated test database is taken offline.
    Sql "ALTER DATABASE [$db] SET OFFLINE WITH ROLLBACK IMMEDIATE;"
    Wait-Health 'ready' 503
    if((Get-Health 'live') -ne 200){throw 'Database outage changed liveness.'}
    Write-Host 'PASS: Database outage returns readiness 503, liveness stays 200, responses disclose no database details.'
    Sql "ALTER DATABASE [$db] SET ONLINE;"
    Wait-Health 'ready' 200
    if((Get-Health 'live') -ne 200 -or $process.HasExited){throw 'Application did not survive database recovery.'}
    Write-Host 'PASS: The same application process recovers readiness 200 without restarting.'
}finally{
    try{
        if($process -and !$process.HasExited){Stop-Process -Id $process.Id;$process.WaitForExit()}
        if($connection.State -eq 'Open'){
            if($db -notmatch '^TeamManager_HealthTest_[0-9a-f]{32}$'){throw 'Unsafe cleanup database name.'}
            # Bring our database online first so SQL also removes its physical files.
            Sql "IF DB_ID(N'$db') IS NOT NULL BEGIN
                IF EXISTS(SELECT 1 FROM sys.databases WHERE name=N'$db' AND state_desc='OFFLINE') ALTER DATABASE [$db] SET ONLINE;
                ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END"
        }
    }finally{
        $connection.Dispose();$client.Dispose()
        foreach($name in $variables.Keys){[Environment]::SetEnvironmentVariable($name,$variables[$name],'Process')}
    }
}

#Requires -Version 5.1
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory,ParameterSetName='Install')][string]$PreviousPackage,
    [Parameter(Mandatory,ParameterSetName='Install')][string]$CurrentPackage,
    [Parameter(Mandatory,ParameterSetName='Resume')][switch]$Resume
)
$ErrorActionPreference='Stop'
Import-Module WebAdministration

$site='TeamManager-LocalTest'
$port=7443
$url="https://localhost:$port"
$db='TeamManager_IisTest'
$poolIdentity="IIS APPPOOL\$site"
$destination=Join-Path $env:SystemDrive "inetpub\$site"
$previous=Join-Path $destination 'previous'
$current=Join-Path $destination 'current'
$connection="Server=localhost\SQLEXPRESS;Database=$db;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$project='IIS rollback test project'

function Invoke-TestSql($query) {
    $result=& sqlcmd -S 'localhost\SQLEXPRESS' -E -C -b -h -1 -W -Q "SET NOCOUNT ON; $query"
    if($LASTEXITCODE){throw ($result -join [Environment]::NewLine)}
    $result | Where-Object {$_}
}

function Install-TestDatabase($package,[switch]$Create) {
    $originalConnection=$env:ConnectionStrings__TeamManager
    try {
        $env:ConnectionStrings__TeamManager=$connection
        & (Join-Path $package 'Install-Database.ps1') -CreateDatabase:$Create
    } finally {
        $env:ConnectionStrings__TeamManager=$originalConnection
    }
}

function Invoke-TestHttp($path,[string[]]$options=@()){
    $response=& curl.exe --silent --show-error --fail --max-time 30 --resolve "localhost:${port}:127.0.0.1" @options "$url$path"
    if($LASTEXITCODE){throw "IIS request failed for $path (curl exit $LASTEXITCODE)."}
    $response -join "`n"
}

function Test-Site([switch]$Health) {
    if($Health){
        foreach($endpoint in 'live','ready'){
            if((Invoke-TestHttp "/health/$endpoint") -ne 'Healthy'){throw "IIS health check failed for $endpoint."}
        }
    }
    $cookies=[IO.Path]::GetTempFileName()
    try {
        $login=Invoke-TestHttp '/Login' @('--cookie-jar',$cookies)
        $token=[regex]::Match($login,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
        if(!$token.Success){throw 'Missing login antiforgery token.'}
        $value=[Net.WebUtility]::HtmlDecode($token.Groups[1].Value)
        Invoke-TestHttp '/Login' @('--cookie',$cookies,'--cookie-jar',$cookies,
            '--data-urlencode','EmployeeNumber=admin','--data-urlencode','Password=admin',
            '--data-urlencode',"__RequestVerificationToken=$value") | Out-Null
        $projects=Invoke-TestHttp '/Projects' @('--cookie',$cookies)
        if(!$projects.Contains($project)){throw 'The signed-in user cannot see the test project.'}
    } finally { Remove-Item -LiteralPath $cookies }
}

if(!$Resume){
    $sources=@{}
    foreach($entry in @(@{Source=$PreviousPackage; Target=$previous},@{Source=$CurrentPackage; Target=$current})){
        $source=(Resolve-Path -LiteralPath $entry.Source).Path
        foreach($file in 'TeamManager.Web.exe','Install-Database.ps1','web.config'){
            if(!(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)){throw "Missing $file in $source."}
        }
        $sources[$entry.Target]=$source
    }
    if($sources[$previous] -eq $sources[$current]){throw 'Choose two different package folders.'}
    if((Test-Path -LiteralPath $destination) -or (Test-Path "IIS:\Sites\$site") -or (Test-Path "IIS:\AppPools\$site")){
        throw "The dedicated IIS test installation already exists. Inspect it before rerunning."
    }
    if((Get-WebBinding | Where-Object bindingInformation -match ":${port}:") -or
        (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)){
        throw "Port $port is already in use."
    }
    if(Invoke-TestSql "SELECT name FROM sys.databases WHERE name=N'$db'; SELECT name FROM sys.server_principals WHERE name=N'$poolIdentity';"){
        throw 'The dedicated SQL test database or login already exists.'
    }

    foreach($target in $sources.Keys){
        New-Item -Path $target -ItemType Directory -Force | Out-Null
        Get-ChildItem -LiteralPath $sources[$target] | Copy-Item -Destination $target -Recurse
        @{
            ConnectionStrings=@{TeamManager=$connection}
            Smtp=@{Enabled=$false}
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'appsettings.Local.json') -Encoding UTF8
    }
    New-WebAppPool -Name $site | Out-Null
    Set-ItemProperty "IIS:\AppPools\$site" managedRuntimeVersion ''
    Set-ItemProperty "IIS:\AppPools\$site" processModel.loadUserProfile $true
    & icacls $destination /grant "${poolIdentity}:(OI)(CI)RX" | Out-Null
    if($LASTEXITCODE){throw 'Could not grant IIS package read access.'}
    Invoke-TestSql "CREATE LOGIN [$poolIdentity] FROM WINDOWS;" | Out-Null

    Install-TestDatabase $previous -Create
    Invoke-TestSql "USE [$db];
        CREATE USER [$poolIdentity] FOR LOGIN [$poolIdentity];
        ALTER ROLE db_datareader ADD MEMBER [$poolIdentity];
        ALTER ROLE db_datawriter ADD MEMBER [$poolIdentity];
        INSERT dbo.Projects(Name) VALUES(N'$project');" | Out-Null

    $certificate=New-SelfSignedCertificate -DnsName 'localhost' -FriendlyName $site -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears(1)
    $trust=New-Object System.Security.Cryptography.X509Certificates.X509Store('Root','CurrentUser')
    try {
        $trust.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $trust.Add([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate.RawData))
    } finally { $trust.Close() }
    New-WebSite -Name $site -IPAddress '127.0.0.1' -Port $port -PhysicalPath $previous -ApplicationPool $site -Ssl | Out-Null
    New-WebBinding -Name $site -Protocol https -IPAddress '[::1]' -Port $port
    foreach($binding in Get-WebBinding -Name $site -Protocol https){
        $binding.AddSslCertificate($certificate.Thumbprint,'My')
    }
    Start-WebItem "IIS:\Sites\$site"
} elseif ((Get-Item "IIS:\Sites\$site").physicalPath -ne $previous -or
    (Get-Item "IIS:\Sites\$site").applicationPool -ne $site) {
    throw 'Resume requires the dedicated IIS site to point at the previous test package.'
}
Test-Site
Write-Host 'PASS: Previous package opens the project over IIS HTTPS.'

$backupDirectory=Invoke-TestSql "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'));"
if(!$backupDirectory){throw 'SQL Server backup directory is unavailable.'}
$backup=Join-Path $backupDirectory "$db-$(Get-Date -Format yyyyMMdd-HHmmss).bak"
$backupSql=$backup.Replace("'","''")

function Switch-TestPackage($path) {
    Set-ItemProperty "IIS:\Sites\$site" physicalPath $path
    Start-WebAppPool $site
}

function Stop-TestPool {
    Stop-WebAppPool $site
    for($attempt=0;$attempt -lt 30;$attempt++){
        if((Get-WebAppPoolState $site).Value -eq 'Stopped'){return}
        Start-Sleep -Seconds 1
    }
    throw 'The dedicated IIS application pool did not stop.'
}

Stop-TestPool
Invoke-TestSql "BACKUP DATABASE [$db] TO DISK=N'$backupSql' WITH COPY_ONLY,CHECKSUM;
    RESTORE VERIFYONLY FROM DISK=N'$backupSql' WITH CHECKSUM;" | Out-Null
Install-TestDatabase $current
Switch-TestPackage $current
Test-Site -Health
Write-Host 'PASS: Current package passes IIS login, project, liveness and readiness checks.'
Invoke-TestSql "UPDATE [$db].dbo.Projects SET Name=N'After IIS upgrade' WHERE Name=N'$project';" | Out-Null

Stop-TestPool
Invoke-TestSql "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    RESTORE DATABASE [$db] FROM DISK=N'$backupSql' WITH REPLACE,CHECKSUM;
    ALTER DATABASE [$db] SET MULTI_USER;" | Out-Null
Switch-TestPackage $previous
Test-Site
Write-Host 'PASS: Previous package signs in and displays the restored project on IIS.'

Stop-TestPool
Install-TestDatabase $current
Switch-TestPackage $current
Test-Site -Health
Write-Host "READY: $url"
Write-Host "Test database: $db. Pre-upgrade backup: $backup"

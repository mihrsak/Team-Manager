param([Parameter(Mandatory=$true)][string]$PackagePath)
$ErrorActionPreference='Stop'
$PackagePath=(Resolve-Path $PackagePath).Path
$dbName='TeamManager_PackageTest_'+[guid]::NewGuid().ToString('N')
$oldConnection=$env:ConnectionStrings__TeamManager
$oldEnvironment=$env:ASPNETCORE_ENVIRONMENT
$connection=New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
function Sql($sql){$cmd=$connection.CreateCommand();$cmd.CommandText=$sql;try{$cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
try{
    if(Test-Path (Join-Path $PackagePath 'appsettings.Local.json')){throw 'Package must not contain local configuration'}
    if(Test-Path (Join-Path $PackagePath 'appsettings.Development.json')){throw 'Package must not contain development configuration'}
    if(-not(Test-Path (Join-Path $PackagePath 'web.config'))){throw 'Missing IIS web.config'}
    foreach($document in @('README.md','INSTALL.md','docs/INSTALL.md','docs/BACKLOG.md','docs/RELEASE-STATUS.md','docs/DB2-LOCAL.md','docs/TARGET-ACCEPTANCE.md')){
        if(-not(Test-Path (Join-Path $PackagePath $document))){throw "Missing package documentation: $document"}
    }
    if(-not([IO.File]::ReadAllText((Join-Path $PackagePath 'INSTALL.md')).Contains('docs/INSTALL.md'))){throw 'Root install entry must link to bundled provider instructions'}
    Write-Host 'PASS: Package includes provider instructions, backlog and target acceptance plan.'
    $env:ConnectionStrings__TeamManager="Server=localhost\SQLEXPRESS;Database=$dbName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    $env:ASPNETCORE_ENVIRONMENT='Production'
    & (Join-Path $PackagePath 'Install-Database.ps1') -CreateDatabase
    $connection.Open();$connection.ChangeDatabase($dbName)
    if((Sql 'SELECT COUNT(*) FROM dbo.Projects') -ne 0){throw 'Standard installation loaded demo projects'}
    if((Sql 'SELECT COUNT(*) FROM dbo.IssueTypes') -ne 3){throw 'Standard installation loaded demo types'}
    if((Sql 'SELECT COUNT(*) FROM dbo.IssuePriorities') -ne 4){throw 'Standard installation loaded demo priorities'}
    if((Sql 'SELECT COUNT(*) FROM dbo.IssueStatuses') -ne 4){throw 'Standard installation loaded demo statuses'}
    Write-Host 'PASS: Standard installation leaves the demo catalog out.'
    & (Join-Path $PackagePath 'Install-Database.ps1') -Demo
    if((Sql 'SELECT COUNT(*) FROM dbo.Issues') -ne 80){throw 'Demo issues missing'}
    & (Join-Path $PackagePath 'Install-Database.ps1') -Demo
    if((Sql 'SELECT COUNT(*) FROM dbo.Issues') -ne 80){throw 'Demo duplicated'}
    if((Sql 'SELECT COUNT(*) FROM dbo.AppUsers') -ne 1){throw 'Bootstrap duplicated'}
    Write-Host 'PASS: Self-contained package installs a new database, schema, admin and optional demo data in Production mode.'
    Write-Host 'PASS: Repeated package installation preserves data.'
    Write-Host 'PASS: Package contains IIS config and excludes local/development settings.'
}finally{
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -ne 'Open'){$connection.Open()};$connection.ChangeDatabase('master')
    if($dbName -match '^TeamManager_PackageTest_[0-9a-f]{32}$'){Sql "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null};$connection.Dispose()
}

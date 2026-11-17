#Requires -Version 7.0
param([ValidateRange(1024,65535)][int]$IssuePort=7643)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$build=Join-Path $root 'artifacts/integration-publish'
$testSettings=@{
    ASPNETCORE_CONTENTROOT=$build
    Smtp__Enabled='false'
    RepositoryHistory__Paths__1=$root
    MailIntake__Routes__1__EmployeeNumber='admin'
    MailIntake__Routes__1__Secret=([guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N'))
}
$previous=@{}

Push-Location $root
try {
    & $dotnet publish src/TeamManager.Web -c Release -o $build
    if($LASTEXITCODE){throw 'Integration test publish failed.'}
    foreach($name in $testSettings.Keys){
        $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,$testSettings[$name],'Process')
    }
    $application=Join-Path $build 'TeamManager.Web.dll'
    $runtime=@{DotnetPath=$dotnet;ApplicationDll=$application}
    & "$PSScriptRoot/Test-Bootstrap.ps1" @runtime
    & "$PSScriptRoot/Test-Issues.ps1" @runtime -Port $IssuePort
    & "$PSScriptRoot/Test-Smtp.ps1" -ApplicationDll $application
    & "$PSScriptRoot/Test-Imap.ps1" -ApplicationDll $application
    Write-Host 'PASS: Bootstrap, project permissions and modules, SMTP and IMAP.'
} finally {
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    Pop-Location
}

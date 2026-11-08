param([switch]$CreateDatabase,[switch]$Demo)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $app=Join-Path $PSScriptRoot 'TeamManager.Web.exe'
    if(-not(Test-Path $app)){throw 'Run this script inside the published Windows package.'}
    $arguments=@('--apply-schema')
    if($CreateDatabase){$arguments+='--create-database'}
    if($Demo){$arguments+='--seed-demo'}
    & $app @arguments
    if($LASTEXITCODE -ne 0){throw 'Database installation failed. Check output above.'}
} finally {Pop-Location}

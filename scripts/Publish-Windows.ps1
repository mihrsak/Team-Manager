param([string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$local=Join-Path $root '.tools\dotnet\dotnet.exe'
$candidates=@($local)
$installed=Get-Command dotnet -ErrorAction SilentlyContinue
if($installed){$candidates+=$installed.Source}
$dotnet=$null
foreach($candidate in ($candidates | Select-Object -Unique)){
    if(-not(Test-Path $candidate)){continue}
    $sdks=@(& $candidate --list-sdks 2>$null)
    if($LASTEXITCODE -eq 0 -and ($sdks | Where-Object {$_ -match '^10\.'})){$dotnet=$candidate;break}
}
if(-not $dotnet){throw 'Install .NET 10 SDK. A runtime-only installation cannot build a Windows package.'}
$output=Join-Path $root ('artifacts\windows-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
& $dotnet publish (Join-Path $root 'src\TeamManager.Web') -c Release -r $Runtime --self-contained true -o $output
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Get-ChildItem (Join-Path $root 'deployment') -File | Copy-Item -Destination $output
Copy-Item (Join-Path $root 'README.md') $output
$packageDocs=Join-Path $output 'docs'
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Get-ChildItem (Join-Path $root 'docs') -Filter '*.md' -File | Copy-Item -Destination $packageDocs
# Keep a root entry point while resolving relative documentation links inside docs.
Set-Content -LiteralPath (Join-Path $output 'INSTALL.md') -Encoding UTF8 -Value '# Installation / Instalacija', '', 'See [installation instructions / upute za instalaciju](docs/INSTALL.md).'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath ($output+'.zip')
Write-Host "Package: $output.zip"

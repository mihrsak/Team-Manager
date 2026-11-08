param([string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$local=Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet=if(Test-Path $local){$local}else{(Get-Command dotnet -ErrorAction Stop).Source}
$output=Join-Path $root ('artifacts\windows-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
& $dotnet publish (Join-Path $root 'src\TeamManager.Web') -c Release -r $Runtime --self-contained true -o $output
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Copy-Item (Join-Path $root 'deployment\*') $output
Copy-Item (Join-Path $root 'docs\INSTALL.md') $output
Compress-Archive -Path (Join-Path $output '*') -DestinationPath ($output+'.zip')
Write-Host "Package: $output.zip"

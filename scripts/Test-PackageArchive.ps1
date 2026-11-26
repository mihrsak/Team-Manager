param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$path=(Resolve-Path -LiteralPath $PackagePath).Path
$archive=[IO.Compression.ZipFile]::OpenRead($path)
function Read-Entry($entry){
    $reader=[IO.StreamReader]::new($entry.Open())
    try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
try{
    $entries=@{}
    foreach($entry in $archive.Entries){
        $name=$entry.FullName.Replace('\','/')
        if($name.StartsWith('/') -or $name -match '(^|/)\.\.(/|$)|:'){throw "Unsafe ZIP entry: $name"}
        if($entries.ContainsKey($name)){throw "Duplicate ZIP entry: $name"}
        $entries[$name]=$entry
        if($name -match '(^|/)(appsettings\.Local\.json|appsettings\.Development\.json|\.env|\.git)(/|$)' -or $name -match '\.(pfx|p12|key|bak)$'){
            throw "Forbidden local configuration or private file: $name"
        }
    }
    foreach($name in @('TeamManager.Web.exe','TeamManager.Web.dll','TeamManager.Web.deps.json','TeamManager.Web.runtimeconfig.json','coreclr.dll','hostfxr.dll','web.config','Install-Database.ps1','appsettings.json','appsettings.Local.example.json','README.md','INSTALL.md','docs/INSTALL.md','docs/BACKLOG.md','docs/RELEASE-STATUS.md','docs/DB2-LOCAL.md','docs/TARGET-ACCEPTANCE.md')){
        if(!$entries.ContainsKey($name) -or $entries[$name].Length -eq 0){throw "Missing or empty required file: $name"}
    }
    Write-Host 'PASS: ZIP contains runtime, IIS installer and required documentation; rejects unsafe paths and forbidden local files.'
    $root=Split-Path $PSScriptRoot -Parent
    $databaseRoot=Join-Path $root 'database'
    $checked=0
    foreach($file in (Get-ChildItem $databaseRoot -Recurse -File | Where-Object {$_.Extension -in @('.sql','.json')})){
        $name='database/'+$file.FullName.Substring($databaseRoot.Length+1).Replace('\','/')
        if(!$entries.ContainsKey($name)){throw "Missing migration asset: $name"}
        $stream=$entries[$name].Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        if($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash){throw "Migration asset differs from source: $name"}
        $checked++
    }
    if($checked -eq 0){throw 'No source migration assets found.'}
    Write-Host "PASS: $checked migration assets match repository SHA-256 hashes."
    $config=Read-Entry $entries['appsettings.json'] | ConvertFrom-Json
    if(-not [string]::IsNullOrWhiteSpace($config.ConnectionStrings.TeamManager)){throw 'Default package must not include a SQL connection string.'}
    $runtime=Read-Entry $entries['TeamManager.Web.runtimeconfig.json'] | ConvertFrom-Json
    if($runtime.runtimeOptions.tfm -ne 'net10.0'){throw 'Unexpected target framework.'}
    $web=[xml](Read-Entry $entries['web.config'])
    $asp=$web.configuration.location.'system.webServer'.aspNetCore
    if(!$asp -or $asp.processPath -notmatch 'TeamManager\.Web\.exe$'){throw 'Missing self-contained IIS process configuration.'}
    if(!(Read-Entry $entries['INSTALL.md']).Contains('docs/INSTALL.md')){throw 'Root installation guide must link to packaged instructions.'}
    Write-Host 'PASS: Default SQL configuration is empty; runtime, IIS configuration and install entry point are valid.'
}finally{$archive.Dispose()}

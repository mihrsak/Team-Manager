param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source=(Resolve-Path -LiteralPath $PackagePath).Path
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $root ('artifacts/archive-guards-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$validator=Join-Path $PSScriptRoot 'Test-PackageArchive.ps1'
function Add-Entry($archive,$name,$body){
    $entry=$archive.CreateEntry($name);$writer=[IO.StreamWriter]::new($entry.Open())
    try{$writer.Write($body)}finally{$writer.Dispose()}
}
function Reject($path,$expected){
    $rejected=$false
    try{& $validator -PackagePath $path}catch{
        if(!$_.Exception.Message.Contains($expected)){throw}
        $rejected=$true
    }
    if(!$rejected){throw "Validation accepted fixture: $expected"}
    Write-Host "PASS: Archive guard rejects $expected"
}
try{
    foreach($case in @(
        @{File='private';Entry='appsettings.Local.json';Expected='Forbidden local configuration'},
        @{File='traversal';Entry='../outside.txt';Expected='Unsafe ZIP entry'},
        @{File='missing';Entry='README.md';Expected='Missing or empty required file'},
        @{File='duplicate';Entry='README.md';Expected='Duplicate ZIP entry'}
    )){
        $path=Join-Path $fixture ($case.File+'.zip')
        $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
        try{Add-Entry $zip $case.Entry 'fixture';if($case.File -eq 'duplicate'){Add-Entry $zip $case.Entry 'duplicate'}}finally{$zip.Dispose()}
        Reject $path $case.Expected
    }
    $tampered=Join-Path $fixture 'tampered.zip'
    Copy-Item -LiteralPath $source -Destination $tampered
    $zip=[IO.Compression.ZipFile]::Open($tampered,[IO.Compression.ZipArchiveMode]::Update)
    try{
        $entry=$zip.Entries | Where-Object {$_.FullName.Replace('\','/') -eq 'database/001-schema.sql'} | Select-Object -First 1
        if(!$entry){throw 'Source package has no baseline migration.'}
        $name=$entry.FullName;$entry.Delete();Add-Entry $zip $name '-- altered migration fixture'
    }finally{$zip.Dispose()}
    Reject $tampered 'Migration asset differs from source'
}finally{
    $resolved=[IO.Path]::GetFullPath($fixture)
    $allowed=[IO.Path]::GetFullPath((Join-Path $root 'artifacts'))+[IO.Path]::DirectorySeparatorChar
    if(!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^archive-guards-[0-9a-f]{32}$'){throw 'Unsafe fixture cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

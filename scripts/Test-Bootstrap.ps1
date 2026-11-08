$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dbName='TeamManager_BootstrapTest_'+[guid]::NewGuid().ToString('N')
$base='https://localhost:7343'
$connection=New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$process=$null
$oldConnection=$env:ConnectionStrings__TeamManager
$oldEnvironment=$env:ASPNETCORE_ENVIRONMENT
function Sql($sql){$cmd=$connection.CreateCommand();$cmd.CommandText=$sql;try{$cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Assert($ok,$message){if(-not $ok){throw $message};Write-Host "PASS: $message"}
function Start-App {
    $exe=Join-Path $root '.tools\dotnet\dotnet.exe'
    $dll=Join-Path $root 'artifacts\release-build\TeamManager.Web.dll'
    $script:process=Start-Process -FilePath $exe -ArgumentList @('"'+$dll+'"','--urls',$base) -WorkingDirectory (Join-Path $root 'src\TeamManager.Web') -WindowStyle Hidden -PassThru
    for($i=0;$i -lt 30;$i++){try{Invoke-WebRequest -UseBasicParsing "$base/Login" | Out-Null;return}catch{Start-Sleep -Milliseconds 500}}
    throw 'Test server failed to start'
}
function Stop-App{if($script:process -and -not $script:process.HasExited){Stop-Process -Id $script:process.Id;$script:process.WaitForExit()}}
function Post($path,$session,$data){
    $page=Invoke-WebRequest -UseBasicParsing "$base$path" -WebSession $session
    $token=[regex]::Match($page.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $data['__RequestVerificationToken']=[System.Net.WebUtility]::HtmlDecode($token)
    Invoke-WebRequest -UseBasicParsing "$base$path" -Method Post -WebSession $session -Body $data
}
try {
    $connection.Open();Sql "CREATE DATABASE [$dbName]" | Out-Null;$connection.ChangeDatabase($dbName)
    foreach($file in @('001-schema.sql','003-users.sql','004-roles.sql')){
        $sql=Get-Content (Join-Path $root "database\$file") -Raw -Encoding UTF8
        foreach($batch in [regex]::Split($sql,'(?im)^\s*GO\s*$')){if(-not [string]::IsNullOrWhiteSpace($batch)){Sql $batch | Out-Null}}
    }
    $env:ConnectionStrings__TeamManager="Server=localhost\SQLEXPRESS;Database=$dbName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    $env:ASPNETCORE_ENVIRONMENT='Development'
    Start-App
    Assert ((Sql "SELECT COUNT(*) FROM dbo.AppUsers WHERE EmployeeNumber='admin' AND PasswordHash<>'admin'") -eq 1) 'Empty database creates one hashed admin account'
    $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $session @{EmployeeNumber='admin';Password='admin'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/InitialPassword') 'Initial login offers password change'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Users" -WebSession $session
    Assert ($page.Content.Contains('Novi korisnik')) 'Skipping reminder allows full administration'
    $page=Post '/Logout' $session @{}
    $page=Post '/Login' $session @{EmployeeNumber='admin';Password='admin'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/InitialPassword') 'Reminder returns at next default-password login'
    $id=Sql "SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber='admin'"
    $password=[guid]::NewGuid().ToString('N')
    $page=Post "/Profile?id=$id&handler=Password" $session @{currentPassword='admin';newPassword=$password;confirmPassword=$password}
    Stop-App;Start-App
    $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $session @{EmployeeNumber='admin';Password=$password}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Changed password survives restart and reminder stops'
    $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $session @{EmployeeNumber='admin';Password='admin'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Old default password no longer works'
    Stop-App
    Sql "UPDATE dbo.AppUsers SET EmployeeNumber='existing-worker' WHERE EmployeeNumber='admin'" | Out-Null
    Start-App
    Assert ((Sql "SELECT COUNT(*) FROM dbo.AppUsers WHERE EmployeeNumber='admin'") -eq 0) 'Non-empty user list does not create default admin'
} finally {
    Stop-App
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -eq 'Open'){
        $connection.ChangeDatabase('master')
        if($dbName -match '^TeamManager_BootstrapTest_[0-9a-f]{32}$'){Sql "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]" | Out-Null}
    }
    $connection.Dispose()
}

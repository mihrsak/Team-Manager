$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dbName='TeamManager_IssueTest_'+[guid]::NewGuid().ToString('N')
$base='https://localhost:7443'
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
$dll=Join-Path $root 'artifacts\release-build\TeamManager.Web.dll'
$connection=New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$oldConnection=$env:ConnectionStrings__TeamManager
$oldEnvironment=$env:ASPNETCORE_ENVIRONMENT
$process=$null
function Sql($sql){$cmd=$connection.CreateCommand();$cmd.CommandText=$sql;try{$cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Assert($ok,$message){if(-not $ok){throw $message};Write-Host "PASS: $message"}
function Post($path,$session,$data){
    $page=Invoke-WebRequest -UseBasicParsing "$base$path" -WebSession $session
    $token=[regex]::Match($page.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $data['__RequestVerificationToken']=[System.Net.WebUtility]::HtmlDecode($token)
    Invoke-WebRequest -UseBasicParsing "$base$path" -Method Post -WebSession $session -Body $data
}
function Stop-App{if($process -and -not $process.HasExited){Stop-Process -Id $process.Id;$process.WaitForExit()}}
try {
    $connection.Open()
    $env:ConnectionStrings__TeamManager="Server=localhost\SQLEXPRESS;Database=$dbName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    $env:ASPNETCORE_ENVIRONMENT='Development'
    Push-Location (Join-Path $root 'src\TeamManager.Web')
    try{& $dotnet $dll --apply-schema --create-database --seed-demo;if($LASTEXITCODE -ne 0){throw 'Installer failed'}}finally{Pop-Location}
    $connection.ChangeDatabase($dbName)
    Assert ((Sql 'SELECT COUNT(*) FROM dbo.Issues') -eq 3) 'Fresh installation creates demo issues'
    Push-Location (Join-Path $root 'src\TeamManager.Web')
    try{& $dotnet $dll --apply-schema --seed-demo;if($LASTEXITCODE -ne 0){throw 'Repeated installation failed'}}finally{Pop-Location}
    Assert ((Sql 'SELECT COUNT(*) FROM dbo.Issues') -eq 3) 'Repeated migration/demo does not duplicate or reset data'
    $process=Start-Process -FilePath $dotnet -ArgumentList @('"'+$dll+'"','--urls',$base) -WorkingDirectory (Join-Path $root 'src\TeamManager.Web') -WindowStyle Hidden -PassThru
    for($i=0;$i -lt 30;$i++){try{Invoke-WebRequest -UseBasicParsing "$base/Login" | Out-Null;break}catch{Start-Sleep -Milliseconds 500}}
    $admin=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $admin @{EmployeeNumber='admin';Password='admin'}
    $project=Sql 'SELECT TOP(1) Id FROM dbo.Projects'
    $input=@{'Input.Title'='QA issue';'Input.Description'='QA description';'Input.Type'='Zadatak';'Input.Priority'='Normalan';'Input.Status'='Novo';'Input.DueDate'='2026-12-01';'Input.Revision'='0'}
    $page=Post "/Issue?projectId=$project&handler=Save" $admin $input
    $id=Sql "SELECT Id FROM dbo.Issues WHERE Title='QA issue'"
    Assert ($null -ne $id) 'Create project issue through authenticated form'
    $input['Input.Title']='QA changed';$input['Input.Revision']='1';$input['Input.Status']='U tijeku'
    $page=Post "/Issue?id=$id&handler=Save" $admin $input
    Assert ((Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id") -eq 2) 'Edit increments revision'
    $input['Input.Title']='stale overwrite'
    $page=Post "/Issue?id=$id&handler=Save" $admin $input
    Assert ((Sql "SELECT Title FROM dbo.Issues WHERE Id=$id") -eq 'QA changed') 'Stale edit cannot overwrite concurrent update'
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='<script>alert(1)</script>'}
    Assert ($page.Content.Contains('&lt;script&gt;')) 'Comment content is HTML encoded'
    $page=Post "/Issue?id=$id&handler=Time" $admin @{date='2026-11-08';hours='1.5';note='QA time'}
    Assert ((Sql "SELECT SUM(Hours) FROM dbo.TimeEntries WHERE IssueId=$id") -eq 1.5) 'Time entry persisted'
    $page=Post "/Issue?id=$id&handler=Time" $admin @{date='2026-11-08';hours='25';note='invalid'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.TimeEntries WHERE IssueId=$id") -eq 1) 'Invalid hours rejected'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id") -eq 4) 'Creation/edit/comment/time recorded atomically in history'
    $page=Post '/Users?handler=Create' $admin @{employee='qatest';name='QA reader';password='QA-password-123'}
    $regular=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $regular @{EmployeeNumber='qatest';Password='QA-password-123'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issues" -WebSession $regular
    Assert (-not $page.Content.Contains('QA changed')) 'Project issues hidden without permission'
    try{Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $regular | Out-Null;throw 'Unauthorized issue read succeeded'}catch{Assert ($_.Exception.Response.StatusCode.value__ -eq 404) 'Direct issue URL is protected'}
    $uid=Sql "SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber='qatest'"
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$project;role='Developer'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $regular
    Assert ($page.Content.Contains('QA changed')) 'Project role enables issue access'
    $page=Post '/Workflow?handler=Value' $admin @{kind='status';name='QAReview';closed='false'}
    Assert ($page.Content.Contains('QAReview')) 'Custom status is created through administration'
    $input['Input.Title']='QA changed';$input['Input.Revision']='2';$input['Input.Status']='QAReview'
    $page=Post "/Issue?id=$id&handler=Save" $regular $input
    Assert ((Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id") -eq 2) 'Missing workflow transition blocks status change'
    $page=Post '/Workflow?handler=Transition' $admin @{role='Developer';type='Zadatak';from='U tijeku';to='QAReview'}
    $page=Post "/Issue?id=$id&handler=Save" $regular $input
    Assert ((Sql "SELECT Status FROM dbo.Issues WHERE Id=$id") -eq 'QAReview') 'Configured workflow transition permits developer status change'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Workflow" -WebSession $regular
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Project developer cannot modify global workflow'
    try{Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=Comment" -WebSession $regular -Method Post -Body @{text='no token'}|Out-Null;throw 'CSRF accepted'}catch{Assert ($_.Exception.Response.StatusCode.value__ -eq 400) 'Missing antiforgery token rejected'}
} finally {
    Stop-App
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -eq 'Open'){$connection.ChangeDatabase('master');if($dbName -match '^TeamManager_IssueTest_[0-9a-f]{32}$'){Sql "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null}}
    $connection.Dispose()
}

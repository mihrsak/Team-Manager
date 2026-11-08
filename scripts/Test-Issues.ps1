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
    # Project lifecycle and hierarchy regression tests (PROJ-02).
    $projectInput=@{'Input.Name'='QA parent';'Input.Description'='<b>plain description</b>';'Input.State'='Active';'Input.IsPublic'='false';'Input.Revision'='0'}
    $page=Post '/Project' $admin $projectInput
    $parent=Sql "SELECT Id FROM dbo.Projects WHERE Name='QA parent'"
    Assert ($null -ne $parent) 'Create project through settings form'
    Assert ($page.Content.Contains('&lt;b&gt;plain description&lt;/b&gt;')) 'Project description is HTML encoded'
    $projectInput['Input.Name']='QA child';$projectInput['Input.ParentId']=$parent
    $page=Post '/Project' $admin $projectInput
    $child=Sql "SELECT Id FROM dbo.Projects WHERE Name='QA child'"
    Assert ((Sql "SELECT ParentId FROM dbo.Projects WHERE Id=$child") -eq $parent) 'Child project retains parent'
    $projectInput['Input.Name']='QA parent';$projectInput['Input.ParentId']=$child;$projectInput['Input.Revision']='1'
    $page=Post "/Project?id=$parent" $admin $projectInput
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Projects WHERE Id=$parent AND ParentId IS NULL AND Revision=1") -eq 1) 'Hierarchy cycle is rejected without modifying project'
    $projectInput['Input.ParentId']='';$projectInput['Input.Name']='QA parent updated'
    $page=Post "/Project?id=$parent" $admin $projectInput
    $projectInput['Input.Name']='Stale project overwrite'
    $page=Post "/Project?id=$parent" $admin $projectInput
    Assert ((Sql "SELECT Name FROM dbo.Projects WHERE Id=$parent") -eq 'QA parent updated') 'Concurrent project edit cannot overwrite newer revision'
    Sql "DELETE dbo.ProjectMembers WHERE ProjectId=$project AND UserId=$uid" | Out-Null
    try{Invoke-WebRequest -UseBasicParsing "$base/Project?id=$project" -WebSession $regular|Out-Null;throw 'Private project exposed'}catch{Assert ($_.Exception.Response.StatusCode.value__ -eq 404) 'Private project settings are hidden from nonmembers'}
    $name=Sql "SELECT Name FROM dbo.Projects WHERE Id=$project"
    $projectInput=@{'Input.Name'=$name;'Input.Description'='Public demo';'Input.State'='Active';'Input.IsPublic'='true';'Input.Revision'='1'}
    $page=Post "/Project?id=$project" $admin $projectInput
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $regular
    Assert ($page.Content.Contains('QA changed')) 'Public project grants authenticated nonmember read access'
    $page=Post "/Project?id=$project" $regular $projectInput
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Public visibility does not grant project administration'
    $before=Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id"
    $page=Post "/Issue?id=$id&handler=Comment" $regular @{text='forbidden public comment'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id") -eq $before) 'Public visibility does not grant commenting'
    $projectInput['Input.State']='Closed';$projectInput['Input.Revision']='2'
    $page=Post "/Project?id=$project" $admin $projectInput
    $input['Input.Revision']='3';$input['Input.Title']='forbidden closed edit'
    $page=Post "/Issue?id=$id&handler=Save" $admin $input
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='forbidden closed comment'}
    $page=Post "/Issue?id=$id&handler=Time" $admin @{date='2026-11-08';hours='2';note='forbidden closed time'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id") -eq $before) 'Closed project blocks edits comments and time even for administrator'
    $page=Post "/Issue?projectId=$project&handler=Save" $admin $input
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Title='forbidden closed edit'") -eq 0) 'Closed project blocks new issues'
    $projectInput['Input.State']='Archived';$projectInput['Input.Revision']='3'
    $page=Post "/Project?id=$project" $admin $projectInput
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $admin
    Assert ($page.Content.Contains('QA changed')) 'Archived project preserves readable issue history'
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='forbidden archived comment'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id") -eq $before) 'Archived project blocks comments'
    $projectInput['Input.State']='Active';$projectInput['Input.Revision']='4'
    $page=Post "/Project?id=$project" $admin $projectInput
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='reopened comment'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id") -eq ($before+1)) 'Reopening project restores editing without data loss'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$child;role='Voditelj'}
    $childInput=@{'Input.Name'='QA child managed';'Input.Description'='Scoped manager';'Input.ParentId'=$parent;'Input.State'='Active';'Input.IsPublic'='false';'Input.Revision'='1'}
    $page=Post "/Project?id=$child" $regular $childInput
    Assert ((Sql "SELECT Name FROM dbo.Projects WHERE Id=$child") -eq 'QA child managed') 'Scoped project manager can edit own project without changing hidden parent'
    $childInput['Input.ParentId']=$project;$childInput['Input.Revision']='2'
    $page=Post "/Project?id=$child" $regular $childInput
    Assert ((Sql "SELECT ParentId FROM dbo.Projects WHERE Id=$child") -eq $parent) 'Scoped manager cannot move project under unauthorized parent'
    $page=Post "/Project?id=$project" $regular $projectInput
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Project management permission does not leak to another project'
    Push-Location (Join-Path $root 'src\TeamManager.Web')
    try{& $dotnet $dll --apply-schema --seed-demo;if($LASTEXITCODE -ne 0){throw 'Post-edit migration failed'}}finally{Pop-Location}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Projects WHERE Id=$project AND IsPublic=1 AND Description='Public demo' AND Revision=5") -eq 1) 'Repeated installation preserves edited project settings'
    # PROJ-03: module settings, route protection and preservation of existing data.
    $page=Post "/Project?id=$project&handler=Modules" $regular @{issuesEnabled='false';timeEnabled='false';revision='5'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Nonmanager cannot change project modules'
    $page=Post "/Project?id=$project&handler=Modules" $admin @{issuesEnabled='true';timeEnabled='false';revision='5'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $admin
    Assert (-not $page.Content.Contains('QA time') -and -not $page.Content.Contains('name="hours"')) 'Disabled time module hides entries history and input form'
    $beforeTime=Sql "SELECT COUNT(*) FROM dbo.TimeEntries WHERE IssueId=$id"
    $page=Post "/Issue?id=$id&handler=Time" $admin @{date='2026-11-08';hours='2';note='disabled module write'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.TimeEntries WHERE IssueId=$id") -eq $beforeTime) 'Disabled time module rejects direct POST even from administrator'
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='Comment with time module disabled'}
    Assert ($page.Content.Contains('Comment with time module disabled')) 'Disabling time does not disable issue comments'
    $page=Post "/Project?id=$project&handler=Modules" $admin @{issuesEnabled='false';timeEnabled='true';revision='5'}
    Assert ((Sql "SELECT CAST(IssuesEnabled AS int) FROM dbo.Projects WHERE Id=$project") -eq 1) 'Stale module settings cannot overwrite newer configuration'
    $page=Post "/Project?id=$project&handler=Modules" $admin @{issuesEnabled='false';timeEnabled='true';revision='6'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issues" -WebSession $admin
    Assert (-not $page.Content.Contains('QA changed')) 'Disabled issue module removes project issues from search and listing'
    foreach($route in @("/Issue?id=$id","/Issue?projectId=$project")){
        try{Invoke-WebRequest -UseBasicParsing "$base$route" -WebSession $admin|Out-Null;throw 'Disabled module visible'}catch{Assert ($_.Exception.Response.StatusCode.value__ -eq 404) 'Disabled issue module blocks direct detail or create URL'}
    }
    # Obtain a valid antiforgery token from project settings to forge a disabled route POST.
    $page=Invoke-WebRequest -UseBasicParsing "$base/Project?id=$project" -WebSession $admin
    $token=[regex]::Match($page.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    foreach($handler in @('Comment','Time','Save')){
        try{Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=$handler" -WebSession $admin -Method Post -Body @{__RequestVerificationToken=[System.Net.WebUtility]::HtmlDecode($token);text='forbidden';date='2026-11-08';hours='1'}|Out-Null;throw 'Disabled route accepted POST'}catch{Assert ($_.Exception.Response.StatusCode.value__ -eq 404) 'Disabled issue module blocks forged write request'}
    }
    $page=Invoke-WebRequest -UseBasicParsing "$base/Project?id=$project" -WebSession $admin
    Assert ($page.Content.Contains('Moduli projekta')) 'Project settings remain accessible while all issue routes are disabled'
    Push-Location (Join-Path $root 'src\TeamManager.Web')
    try{& $dotnet $dll --apply-schema;if($LASTEXITCODE -ne 0){throw 'Module migration repeat failed'}}finally{Pop-Location}
    Assert ((Sql "SELECT CAST(IssuesEnabled AS int) FROM dbo.Projects WHERE Id=$project") -eq 0) 'Repeated migration preserves disabled module settings'
    $page=Post "/Project?id=$project&handler=Modules" $admin @{issuesEnabled='true';timeEnabled='true';revision='7'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $admin
    Assert ($page.Content.Contains('QA time') -and $page.Content.Contains('QA changed')) 'Reenabling modules restores existing issue and time records'
    $page=Post "/Project?id=$child&handler=Modules" $regular @{issuesEnabled='false';timeEnabled='false';revision='2'}
    Assert ((Sql "SELECT CAST(IssuesEnabled AS int) FROM dbo.Projects WHERE Id=$child") -eq 0) 'Scoped project manager can configure own project modules'
    Assert ((Sql "SELECT CAST(IssuesEnabled AS int) FROM dbo.Projects WHERE Id=$project") -eq 1) 'Module configuration is isolated between projects'
} finally {
    Stop-App
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -eq 'Open'){$connection.ChangeDatabase('master');if($dbName -match '^TeamManager_IssueTest_[0-9a-f]{32}$'){Sql "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null}}
    $connection.Dispose()
}

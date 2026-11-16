param([string]$DotnetPath, [string]$ApplicationDll)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dbName='TeamManager_IssueTest_'+[guid]::NewGuid().ToString('N')
$base='https://localhost:7443'
$dotnet=if($DotnetPath){$DotnetPath}else{Join-Path $root '.tools\dotnet\dotnet.exe'}
$dll=if($ApplicationDll){$ApplicationDll}else{Join-Path $root 'artifacts\release-build\TeamManager.Web.dll'}
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
    $body = foreach ($key in $data.Keys) {
        foreach ($value in @($data[$key])) {
            [uri]::EscapeDataString($key) + '=' + [uri]::EscapeDataString([string]$value)
        }
    }
    Invoke-WebRequest -UseBasicParsing "$base$path" -Method Post -WebSession $session -Body ($body -join '&') -ContentType 'application/x-www-form-urlencoded'
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
    $page=Post "/Issue?id=$id&handler=Time" $admin @{date='2026-11-08';hours='1.5';note='QA time';activity='QA analysis'}
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
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport?ProjectId=$project&Person=admin&Activity=QA%20analysis&From=2026-11-08&To=2026-11-08" -WebSession $admin
    Assert ($report.Content.Contains('QA analysis') -and $report.Content -match '1[.,]50 h') 'Time report filters and totals matching entries'
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport?ProjectId=$project&Activity=QA%20analysis&From=2026-11-09" -WebSession $admin
    Assert (-not $report.Content.Contains('<td>QA analysis</td>')) 'Time report excludes entries outside date range'
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport?ProjectId=$project&Person=unknownperson" -WebSession $admin
    Assert (-not $report.Content.Contains('<td>QA analysis</td>')) 'Time report excludes other people when filtered'
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
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport" -WebSession $regular
    Assert (-not $report.Content.Contains('QA analysis')) 'Time report hides private project entries from nonmembers'
    $export=Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Csv&ProjectId=$project" -WebSession $regular
    $csv=[Text.Encoding]::UTF8.GetString($export.RawContentStream.ToArray())
    Assert (($csv.TrimStart([char]0xFEFF) -split '\r?\n')[1] -eq '') 'CSV excludes private tasks for nonmembers'
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
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport" -WebSession $admin
    Assert (-not $report.Content.Contains('QA analysis')) 'Time report hides entries when the time module is disabled'
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
    $childInput['Input.ParentId']=$parent
    $childInput['Input.Revision']='3'
    $childInput['Input.InheritParentRoles']='true'
    $page=Post "/Project?id=$child" $regular $childInput
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Child manager cannot enable inheritance without parent authority'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$parent;role='Developer'}
    $inheritedInput=@{'Input.Name'='QA inherited';'Input.ParentId'=$parent;'Input.State'='Active';'Input.Revision'='0'}
    $page=Post '/Project' $admin $inheritedInput
    $inherited=Sql "SELECT Id FROM dbo.Projects WHERE Name='QA inherited'"
    $page=Invoke-WebRequest -UseBasicParsing "$base/Projects" -WebSession $regular
    Assert (-not $page.Content.Contains('QA inherited')) 'Private child is hidden before inheritance is enabled'
    $inheritedInput['Input.Revision']='1'
    $inheritedInput['Input.InheritParentRoles']='true'
    $page=Post "/Project?id=$inherited" $admin $inheritedInput
    $page=Invoke-WebRequest -UseBasicParsing "$base/Project?id=$inherited" -WebSession $regular
    Assert ($page.Content.Contains('QA inherited')) 'Parent role grants access to opted-in private child'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.ProjectMembers WHERE ProjectId=$inherited AND UserId=$uid") -eq 0) 'Inheritance keeps direct memberships separate'
    $inheritedInput['Input.Revision']='2'
    $inheritedInput['Input.InheritParentRoles']='false'
    $page=Post "/Project?id=$inherited" $admin $inheritedInput
    $page=Invoke-WebRequest -UseBasicParsing "$base/Projects" -WebSession $regular
    Assert (-not $page.Content.Contains('QA inherited')) 'Disabling inheritance immediately revokes inherited access'
    Sql "UPDATE dbo.Issues SET Title=N'=QA,'+NCHAR(34)+N'CSV'+NCHAR(34) WHERE Id=$id" | Out-Null
    $export=Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Csv&ProjectId=$project&Query=QA%2CCSV" -WebSession $admin
    $csv=[Text.Encoding]::UTF8.GetString($export.RawContentStream.ToArray())
    Assert (($csv.TrimStart([char]0xFEFF) -split '\r?\n')[1] -eq '') 'CSV applies text filter'
    $export=Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Csv&ProjectId=$project&Query=QA%2C" -WebSession $admin
    $csv=[Text.Encoding]::UTF8.GetString($export.RawContentStream.ToArray())
    $expected='"'+[char]39+'=QA,""CSV"""'
    Assert ($csv.Contains($expected)) 'CSV quotes delimiters and prevents spreadsheet formulas'
    Assert ($export.Headers['Content-Disposition'].Contains('zadaci.csv')) 'CSV is an attachment'
    $rev=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    $page=Post "/Issue?id=$id&handler=Transfer" $regular @{revision=$rev;destination=$parent;move='true'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Move requires management of both projects'
    $page=Post "/Issue?id=$id&handler=Transfer" $admin @{revision=$rev;destination=$parent}
    $copy=Sql "SELECT MAX(Id) FROM dbo.Issues WHERE ProjectId=$parent"
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$copy AND Status=N'Novo' AND AssigneeId IS NULL") -eq 1) 'Copy creates an unassigned issue in the initial status'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$copy") -eq 1 -and (Sql "SELECT COUNT(*) FROM dbo.TimeEntries WHERE IssueId=$copy") -eq 0) 'Copy does not duplicate history or time'
    $copyRev=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$copy"
    $page=Post '/Issues?handler=Bulk' $admin @{selection=@("${id}:$rev","${copy}:$($copyRev+1)");bulkPriority='Hitno'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id IN ($id,$copy) AND Priority=N'Hitno'") -eq 0) 'Bulk edit leaves both issues untouched on stale revision'
    $page=Post '/Issues?handler=Bulk' $admin @{selection=@("${id}:$rev","${copy}:$copyRev");bulkPriority='Hitno'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id IN ($id,$copy) AND Priority=N'Hitno'") -eq 2) 'Bulk edit updates both selected issues'
    $newRev=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    Assert ($newRev -eq ($rev+1)) 'Bulk edit increments revision'
    $page=Post "/Issue?id=$id&handler=Transfer" $admin @{revision=$rev;destination=$parent;move='true'}
    Assert ((Sql "SELECT ProjectId FROM dbo.Issues WHERE Id=$id") -eq $project) 'Stale revision blocks move'
    $page=Post "/Issue?id=$id&handler=Transfer" $admin @{revision=$newRev;destination=$parent;move='true'}
    Assert ((Sql "SELECT ProjectId FROM dbo.Issues WHERE Id=$id") -eq $parent) 'Move keeps the same issue in the destination'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEvents WHERE IssueId=$id AND Kind='moved'") -eq 1 -and (Sql "SELECT COUNT(*) FROM dbo.TimeEntries WHERE IssueId=$id") -eq 1) 'Move preserves recorded time and adds a history event'
    Sql "DELETE dbo.ProjectMembers WHERE ProjectId=$parent AND UserId=$uid" | Out-Null
    $publicIssue=Sql "SELECT MIN(Id) FROM dbo.Issues WHERE ProjectId=$project"
    $page=Post "/Issue?id=$publicIssue&handler=Link" $admin @{otherId=$copy;relation='related'}
    Assert ($page.Content.Contains("href=`"/Issue?id=$copy`"")) 'Related issue is linked from the source'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $regular
    Assert (-not $page.Content.Contains("href=`"/Issue?id=$copy`"")) 'Related issue in private project is hidden from nonmembers'
    $page=Post "/Issue?id=$publicIssue&handler=Link" $regular @{otherId=$copy;relation='related';remove='true'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Link changes require edit permission on both issues'
    $page=Post "/Issue?id=$copy&handler=Link" $admin @{otherId=$publicIssue;relation='related';remove='true'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueLinks WHERE Kind='related'") -eq 0) 'Symmetric link can be removed from either end'
    $page=Post "/Issue?id=$id&handler=Link" $admin @{otherId=$copy;relation='duplicate'}
    $page=Post "/Issue?id=$copy&handler=Link" $admin @{otherId=$id;relation='duplicate'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueLinks WHERE Kind='duplicate'") -eq 1) 'Reverse duplicate link is idempotent'
    $page=Post "/Issue?id=$id&handler=Link" $admin @{otherId=$copy;relation='parent'}
    $page=Post "/Issue?id=$publicIssue&handler=Link" $admin @{otherId=$copy;relation='parent'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueLinks WHERE Kind='parent' AND SourceId=$id AND TargetId=$copy") -eq 1) 'An issue keeps its existing parent'
    $page=Post "/Issue?id=$copy&handler=Link" $admin @{otherId=$id;relation='parent'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueLinks WHERE Kind='parent'") -eq 1) 'Subtask cycle is rejected'
    $page=Post "/Issue?id=$id&handler=Link" $admin @{otherId=$copy;relation='blocks'}
    $page=Post "/Issue?id=$copy&handler=Link" $admin @{otherId=$publicIssue;relation='blocks'}
    $page=Post "/Issue?id=$publicIssue&handler=Link" $admin @{otherId=$id;relation='blocks'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueLinks WHERE Kind='blocks'") -eq 2) 'Transitive dependency cycle is rejected'
    $page=Post "/Issues?handler=SaveQuery&ProjectId=$parent&Query=QA%2C&SortOrder=oldest&GroupBy=priority&Columns=title,priority" $admin @{name='QA saved filter'}
    Assert ($page.Content.Contains('QA saved filter')) 'Saved issue filter is listed for its owner'
    $savedId=Sql "SELECT Id FROM dbo.SavedIssueQueries WHERE Name=N'QA saved filter'"
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Saved&id=$savedId" -WebSession $admin
    Assert ($page.BaseResponse.ResponseUri.Query -match "ProjectId=$parent" -and [uri]::UnescapeDataString($page.BaseResponse.ResponseUri.Query) -match 'Query=QA,') 'Saved issue filter reapplies project and search'
    Assert ($page.BaseResponse.ResponseUri.Query -match 'SortOrder=oldest' -and $page.BaseResponse.ResponseUri.Query -match 'GroupBy=priority') 'Saved query restores sort and grouping'
    Assert ($page.Content.Contains('<th>Prioritet</th>') -and -not $page.Content.Contains('<th>Rok</th>')) 'Saved query restores selected columns'
    Assert ($page.Content -match '<caption>Hitno\s*.\s*2 zadataka</caption>') 'Grouped list shows the count for matching issues'
    $firstLink=$page.Content.IndexOf("href=`"/Issue?id=$id`"")
    $secondLink=$page.Content.IndexOf("href=`"/Issue?id=$copy`"")
    Assert ($firstLink -ge 0 -and $secondLink -gt $firstLink) 'Oldest-first ordering applies within a group'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issues" -WebSession $regular
    Assert (-not $page.Content.Contains('QA saved filter')) 'Saved issue filters are private'
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Saved&id=$savedId" -WebSession $regular | Out-Null
        throw 'Other user could open saved query'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Other user cannot open saved query by ID'
    }
    try {
        Post "/Issues?handler=DeleteQuery&id=$savedId" $regular @{} | Out-Null
        throw 'Other user could delete saved query'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Other user cannot delete saved query by ID'
    }
    $page=Post "/Issues?handler=DeleteQuery&id=$savedId" $admin @{}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.SavedIssueQueries WHERE Id=$savedId") -eq 0) 'Owner can delete saved query'
    $calendar=Invoke-WebRequest -UseBasicParsing "$base/Calendar?Month=2026-12&ProjectId=$parent" -WebSession $admin
    $issueLink="href=`"/Issue?id=$id`""
    Assert ($calendar.Content.Contains($issueLink)) 'Calendar shows an accessible issue in its deadline month'
    $nextMonth=Invoke-WebRequest -UseBasicParsing "$base/Calendar?Month=2027-01&ProjectId=$parent" -WebSession $admin
    Assert (-not $nextMonth.Content.Contains($issueLink)) 'Calendar excludes deadlines in previous months'
    $regularCalendar=Invoke-WebRequest -UseBasicParsing "$base/Calendar?Month=2026-12" -WebSession $regular
    Assert (-not $regularCalendar.Content.Contains($issueLink)) 'Calendar hides deadlines from inaccessible projects'
    $upperBoundary=Invoke-WebRequest -UseBasicParsing "$base/Calendar?Month=9999-12" -WebSession $admin
    Assert ($upperBoundary.StatusCode -eq 200) 'Calendar supports the final selectable month'
    $schedule=@{'Input.Title'='QA scheduled';'Input.Description'='Scheduled task';'Input.Type'='Zadatak';'Input.Priority'='Hitno';'Input.Status'='Novo';'Input.StartDate'='2026-12-04';'Input.DueDate'='2026-12-03';'Input.Revision'=(Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id")}
    $page=Post "/Issue?id=$id&handler=Save" $admin $schedule
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$id AND StartDate IS NULL") -eq 1) 'Issue rejects a start after its due date'
    $schedule['Input.StartDate']='2026-11-29'
    $page=Post "/Issue?id=$id&handler=Save" $admin $schedule
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$id AND StartDate='2026-11-29' AND DueDate='2026-12-03'") -eq 1) 'Issue saves its schedule'
    $gantt=Invoke-WebRequest -UseBasicParsing "$base/Gantt?Month=2026-12&ProjectId=$parent" -WebSession $admin
    Assert ($gantt.Content -match "<tr id=`"task-$id`">(?s:.*?)colspan=`"3`" class=`"gantt-period`"") 'Gantt clips the schedule at the month boundary'
    Assert ($gantt.Content.Contains("href=`"#task-$id`"")) 'Gantt shows blockers between displayed tasks'
    Assert ($gantt.Content -match "<tr id=`"task-$copy`">(?s:.*?)colspan=`"1`" class=`"gantt-period`"") 'A deadline alone is shown as a one-day marker'
    Sql "UPDATE dbo.Issues SET DueDate='2026-12-03' WHERE Id=$publicIssue" | Out-Null
    $gantt=Invoke-WebRequest -UseBasicParsing "$base/Gantt?Month=2026-12" -WebSession $regular
    Assert ($gantt.Content.Contains("id=`"task-$publicIssue`"") -and -not $gantt.Content.Contains("href=`"#task-$copy`"")) 'Gantt hides dependencies on inaccessible tasks'
    $roadmapData=@{kind='version';name='QA version';description='QA release plan';targetDate='2026-12-18'}
    $page=Post "/Roadmap?handler=Save&projectId=$parent" $admin $roadmapData
    Assert ($page.Content.Contains('QA version') -and $page.Content.Contains('18.12.2026')) 'Roadmap shows a saved version and target date'
    $itemId=Sql "SELECT Id FROM dbo.ProjectMilestones WHERE ProjectId=$parent AND Name=N'QA version'"
    $page=Post "/Roadmap?handler=Save&projectId=$parent" $admin @{kind='milestone';name='QA milestone'}
    Assert ($page.Content.Contains('QA milestone')) 'Roadmap displays milestones alongside versions'
    $roadmapData['itemId']=$itemId
    $roadmapData['revision']=0
    $roadmapData['isComplete']='true'
    $page=Post "/Roadmap?handler=Save&projectId=$parent" $admin $roadmapData
    Assert ((Sql "SELECT COUNT(*) FROM dbo.ProjectMilestones WHERE Id=$itemId AND IsComplete=1 AND Revision=1") -eq 1) 'Roadmap manager can complete a version'
    $roadmapData['name']='QA stale'
    $page=Post "/Roadmap?handler=Save&projectId=$parent" $admin $roadmapData
    Assert ((Sql "SELECT Name FROM dbo.ProjectMilestones WHERE Id=$itemId") -eq 'QA version') 'Roadmap rejects stale edits'
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Roadmap?projectId=$parent" -WebSession $regular | Out-Null
        throw 'Private roadmap was visible'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Private roadmap is hidden from nonmembers'
    }
    $page=Post "/Roadmap?handler=Save&projectId=$project" $regular @{kind='milestone';name='Unauthorized'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Public roadmap still requires management permission to edit'
    $schedule['Input.RoadmapItemId']=$itemId
    $schedule['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    $page=Post "/Issue?id=$id&handler=Save" $admin $schedule
    Assert ((Sql "SELECT RoadmapItemId FROM dbo.Issues WHERE Id=$id") -eq $itemId) 'Issue can be assigned to a version in its project'
    $closedStatus=Sql 'SELECT TOP (1) Name FROM dbo.IssueStatuses WHERE IsClosed=1 ORDER BY Name'
    $schedule['Input.Status']=$closedStatus
    $schedule['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$copy"
    $page=Post "/Issue?id=$copy&handler=Save" $admin $schedule
    $page=Invoke-WebRequest -UseBasicParsing "$base/Roadmap?projectId=$parent" -WebSession $admin
    Assert ($page.Content.Contains('1 od 2 zatvorenih zadataka (50 %)')) 'Version progress counts closed workflow statuses'
    Assert ($page.Content -match '<li>Novo\s*.+\s*1</li>') 'Roadmap shows counts by status'
    $milestoneId=Sql "SELECT Id FROM dbo.ProjectMilestones WHERE ProjectId=$parent AND Name=N'QA milestone'"
    $schedule['Input.RoadmapItemId']=$milestoneId
    $schedule['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$copy"
    $page=Post "/Issue?id=$copy&handler=Save" $admin $schedule
    $page=Invoke-WebRequest -UseBasicParsing "$base/Roadmap?projectId=$parent" -WebSession $admin
    Assert ($page.Content.Contains('0 od 1 zatvorenih zadataka (0 %)') -and $page.Content.Contains('1 od 1 zatvorenih zadataka (100 %)')) 'Reassigning an issue updates version and milestone progress'
    $schedule['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$publicIssue"
    $page=Post "/Issue?id=$publicIssue&handler=Save" $admin $schedule
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$publicIssue AND RoadmapItemId IS NULL") -eq 1) 'Issue cannot be linked to another project roadmap item'
    $copyRevision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$copy"
    $page=Post "/Issue?id=$copy&handler=Transfer" $admin @{revision=$copyRevision;destination=$project;move='true'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$copy AND ProjectId=$project AND RoadmapItemId IS NULL") -eq 1) 'Moving an issue clears its source roadmap assignment'
    $activity=Invoke-WebRequest -UseBasicParsing "$base/Activity?ProjectId=$parent" -WebSession $admin
    Assert ($activity.Content.Contains('QA scheduled') -and $activity.Content.Contains('Posljednjih')) 'Activity page lists recent project events'
    $atom=Invoke-WebRequest -UseBasicParsing "$base/Activity?handler=Atom&ProjectId=$parent" -WebSession $admin
    $atomXml=[xml][Text.Encoding]::UTF8.GetString($atom.RawContentStream.ToArray())
    Assert ($atom.Headers['Content-Type'] -match 'application/atom\+xml' -and $atomXml.feed.entry.Count -gt 0) 'Atom export is valid XML with entries'
    $rss=Invoke-WebRequest -UseBasicParsing "$base/Activity?handler=Rss" -WebSession $regular
    $rssXml=[xml][Text.Encoding]::UTF8.GetString($rss.RawContentStream.ToArray())
    $rssLinks=@($rssXml.rss.channel.item | ForEach-Object { $_.link })
    Assert ($rss.Headers['Content-Type'] -match 'application/rss\+xml' -and $rssLinks -match "/Issue\?id=$copy$") 'RSS export includes public issue activity'
    Assert (-not ($rssLinks -match "/Issue\?id=$id$")) 'RSS export excludes inaccessible issue activity'
    Assert ($rss.Headers['Cache-Control'] -match 'no-store') 'Personalized feed disables caching'
    $anon=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Invoke-WebRequest -UseBasicParsing "$base/Activity?handler=Atom" -WebSession $anon
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Anonymous clients cannot access activity feeds'
    Sql "UPDATE dbo.Issues SET AssigneeId=$uid WHERE Id IN ($id,$copy,$publicIssue); UPDATE dbo.Issues SET Status=N'Novo',DueDate='2000-01-01' WHERE Id=$publicIssue" | Out-Null
    $dashboard=Invoke-WebRequest -UseBasicParsing "$base/Dashboard" -WebSession $regular
    Assert ($dashboard.Content.Contains('Rok je pro') -and $dashboard.Content.Contains("href=`"/Issue?id=$publicIssue`"")) 'Dashboard includes an overdue assigned issue'
    Assert (-not $dashboard.Content.Contains("href=`"/Issue?id=$copy`"")) 'Dashboard excludes assigned issues in closed workflow statuses'
    Assert (-not $dashboard.Content.Contains("href=`"/Issue?id=$id`"")) 'Dashboard excludes assigned issues in inaccessible projects'
    Sql "UPDATE dbo.Issues SET AssigneeId=NULL WHERE Id=$publicIssue" | Out-Null
    $dashboard=Invoke-WebRequest -UseBasicParsing "$base/Dashboard" -WebSession $regular
    Assert (-not $dashboard.Content.Contains("href=`"/Issue?id=$publicIssue`"")) 'Dashboard excludes unassigned issues'
    $search=Invoke-WebRequest -UseBasicParsing "$base/Search?Query=QA" -WebSession $admin
    Assert ($search.Content.Contains('QA parent updated') -and $search.Content.Contains('QA scheduled') -and $search.Content.Contains('QA milestone') -and $search.Content.Contains('qatest')) 'Unified search finds projects, issues, roadmap items and users'
    $search=Invoke-WebRequest -UseBasicParsing "$base/Search?Query=QA&Module=roadmap" -WebSession $admin
    Assert ($search.Content.Contains("#plan-$milestoneId") -and -not $search.Content.Contains('<h2>Zadaci</h2>')) 'Module filter limits search and links to the roadmap item'
    $search=Invoke-WebRequest -UseBasicParsing "$base/Search?Query=QA" -WebSession $regular
    Assert (-not $search.Content.Contains("href=`"/Project?id=$parent`"") -and -not $search.Content.Contains("href=`"/Issue?id=$id`"") -and -not $search.Content.Contains("#plan-$milestoneId")) 'Unified search hides private projects and their content'
    $search=Invoke-WebRequest -UseBasicParsing "$base/Search?Query=qatest&Module=users" -WebSession $regular
    Assert ($search.Content.Contains("href=`"/Profile?id=$uid`"")) 'Directory search links to the matching profile'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='true'}
    Assert ($page.Content.Contains('Prestani pratiti')) 'Viewer can watch a visible issue'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='true'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueWatchers WHERE IssueId=$publicIssue AND UserId=$uid") -eq 1) 'Repeated watch request does not duplicate subscription'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Dashboard" -WebSession $regular
    Assert ($page.Content.Contains('Pratim (1)') -and $page.Content.Contains("href=`"/Issue?id=$publicIssue`"")) 'Personal dashboard includes watched issues'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Dashboard" -WebSession $admin
    Assert ($page.Content.Contains('Pratim (0)')) 'Watch list is personal'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='false'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueWatchers WHERE IssueId=$publicIssue AND UserId=$uid") -eq 0) 'Viewer can stop watching'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$parent;role='Developer'}
    $page=Post "/Issue?id=$id&handler=Watch" $regular @{watch='true'}
    Sql "DELETE dbo.ProjectMembers WHERE ProjectId=$parent AND UserId=$uid" | Out-Null
    $page=Invoke-WebRequest -UseBasicParsing "$base/Dashboard" -WebSession $regular
    Assert (-not $page.Content.Contains("href=`"/Issue?id=$id`"")) 'Watching does not preserve access after project permission is revoked'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='true'}
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $admin @{watch='true'}
    $page=Post "/Issue?id=$publicIssue&handler=PrivateNote" $regular @{text='QA private note <secret>'}
    Assert ($page.Content.Contains('QA private note &lt;secret&gt;')) 'Author sees the private note as escaped text'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $admin
    Assert (-not $page.Content.Contains('QA private note')) 'Other viewers including administrators cannot see the private note'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueNotifications WHERE IssueId=$publicIssue") -eq 0) 'Private notes do not generate watcher notifications'
    $page=Post "/Issue?id=$publicIssue&handler=Comment" $admin @{text='QA watcher notification'}
    $eventId=Sql "SELECT MAX(Id) FROM dbo.IssueEvents WHERE IssueId=$publicIssue"
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueNotifications WHERE EventId=$eventId AND UserId=$uid AND IsRead=0") -eq 1) 'Public activity notifies the other watcher'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueNotifications WHERE EventId=$eventId") -eq 1) 'Actor does not receive a notification for their own activity'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Notifications" -WebSession $regular
    Assert ($page.Content.Contains("href=`"/Issue?id=$publicIssue`"")) 'Watcher can open notification list'
    try {
        Post "/Notifications?handler=Read&eventId=$eventId" $admin @{} | Out-Null
        throw 'Other user changed notification state'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Another user cannot mark a notification as read'
    }
    $page=Post "/Notifications?handler=Read&eventId=$eventId" $regular @{}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueNotifications WHERE EventId=$eventId AND UserId=$uid AND IsRead=1") -eq 1) 'Watcher can mark a notification as read'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='false'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueNotifications WHERE EventId=$eventId AND UserId=$uid") -eq 0) 'Unwatching removes notifications for the task'
    $page=Post "/Issue?id=$id&handler=Comment" $admin @{text='QA hidden watcher event'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Notifications" -WebSession $regular
    Assert (-not $page.Content.Contains("href=`"/Issue?id=$id`"")) 'Notifications hide issues after access is revoked'
    Assert ((Sql "SELECT COUNT(DISTINCT Action) FROM dbo.AdministrationAudit WHERE ActorEmployeeNumber=N'admin' AND Action IN ('project.create','project.update','project.modules','workflow.value.create','workflow.transition.add','roadmap.create','roadmap.update')") -eq 7) 'Audit covers project settings, workflow, and roadmap administration'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE Action='roadmap.update' AND Subject=N'QA stale'") -eq 0) 'Stale administrative update leaves no audit entry'
    $page=Post "/ProjectNews?handler=Publish&projectId=$project" $admin @{title='QA public news';body='QA <b>release</b>'}
    Assert ($page.Content.Contains('QA &lt;b&gt;release&lt;/b&gt;')) 'Manager can publish news with HTML-encoded content'
    $newsId=Sql "SELECT Id FROM dbo.ProjectNews WHERE ProjectId=$project AND Title=N'QA public news'"
    $page=Invoke-WebRequest -UseBasicParsing "$base/ProjectNews?projectId=$project&newsId=$newsId" -WebSession $regular
    Assert ($page.Content.Contains('QA public news')) 'Authenticated user can read public project news'
    $page=Post "/ProjectNews?handler=Publish&projectId=$project" $regular @{title='Unauthorized';body='Forbidden'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Public project visibility does not grant publishing permission'
    $page=Post "/ProjectNews?handler=Comment&projectId=$project&newsId=$newsId" $regular @{body='Forbidden'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Comment submission requires the news permission'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$project;role='Developer'}
    $page=Post "/ProjectNews?handler=Comment&projectId=$project&newsId=$newsId" $regular @{body='QA <comment>'}
    Assert ($page.Content.Contains('QA &lt;comment&gt;')) 'Member with news permission can comment'
    $page=Post "/ProjectNews?handler=Publish&projectId=$parent" $admin @{title='QA private news';body='Private project news'}
    $privateNews=Sql "SELECT Id FROM dbo.ProjectNews WHERE ProjectId=$parent AND Title=N'QA private news'"
    try {
        Invoke-WebRequest -UseBasicParsing "$base/ProjectNews?projectId=$project&newsId=$privateNews" -WebSession $regular | Out-Null
        throw 'Private news was visible through the public project'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Private news cannot be read using a public project ID'
    }
    Sql "UPDATE dbo.Projects SET State='Closed' WHERE Id=$project" | Out-Null
    $page=Post "/ProjectNews?handler=Comment&projectId=$project&newsId=$newsId" $admin @{body='Forbidden on closed project'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Closed project blocks new comments even for administrator'
    $page=Post "/Wiki?handler=Save&projectId=$parent" $admin @{title='QA wiki first';body='QA <first> revision'}
    $wikiId=Sql "SELECT Id FROM dbo.ProjectWikiPages WHERE ProjectId=$parent AND Title=N'QA wiki first'"
    Assert ($page.Content.Contains('QA &lt;first&gt; revision')) 'Wiki creation stores plain text and displays it escaped'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$parent;role='Developer'}
    $page=Post "/Wiki?handler=Save&projectId=$parent&wikiId=$wikiId" $regular @{title='QA wiki second';body='QA second revision';revision='1'}
    Assert ($page.Content.Contains('QA second revision') -and $page.Content.Contains('Verzija 2')) 'Wiki editor can create a new revision'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Wiki?projectId=$parent&wikiId=$wikiId&revision=1&compare=2" -WebSession $admin
    Assert ($page.Content.Contains('QA &lt;first&gt; revision') -and $page.Content.Contains('QA second revision')) 'Wiki displays two historical revisions for comparison'
    $page=Post "/Wiki?handler=Save&projectId=$parent&wikiId=$wikiId" $admin @{title='QA stale wiki';body='Stale content';revision='1'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.ProjectWikiRevisions WHERE PageId=$wikiId") -eq 2) 'Stale wiki edit does not create a revision'
    Sql "DELETE dbo.ProjectMembers WHERE ProjectId=$parent AND UserId=$uid" | Out-Null
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Wiki?projectId=$parent&wikiId=$wikiId&revision=1&compare=2" -WebSession $regular | Out-Null
        throw 'Private wiki comparison was visible'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Wiki hides private page history after project access is revoked'
    }
    $page=Post "/Wiki?handler=Save&projectId=$project" $admin @{title='Forbidden';body='Closed project'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Closed project blocks wiki edits'
    Sql "UPDATE dbo.Projects SET State='Active' WHERE Id=$project; DELETE dbo.ProjectMembers WHERE ProjectId=$project AND UserId=$uid" | Out-Null
    $page=Post "/Wiki?handler=Save&projectId=$project" $regular @{title='Forbidden';body='Missing wiki permission'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Public project visibility does not grant wiki editing'
    $detail=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $admin
    $token=[regex]::Match($detail.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $boundary='TeamManagerQABoundary'
    $upload=@(
        "--$boundary",
        'Content-Disposition: form-data; name="__RequestVerificationToken"',
        '',
        [System.Net.WebUtility]::HtmlDecode($token),
        "--$boundary",
        'Content-Disposition: form-data; name="attachment"; filename="qa-attachment.txt"',
        'Content-Type: text/plain',
        '',
        'Team Manager QA attachment',
        "--$boundary--",
        ''
    ) -join "`r`n"
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=Upload" -Method Post -WebSession $admin -Body $upload -ContentType "multipart/form-data; boundary=$boundary" -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert ($page.StatusCode -eq 303 -and $page.Headers.Location -match "/Issue\?id=$id$") 'Upload redirects to the issue detail'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id" -WebSession $admin
    Assert ($page.Content.Contains('qa-attachment.txt')) 'Issue editor can upload an attachment'
    $attachmentId=Sql "SELECT Id FROM dbo.IssueAttachments WHERE IssueId=$id"
    $download=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=Attachment&attachmentId=$attachmentId" -WebSession $admin
    Assert ([Text.Encoding]::ASCII.GetString($download.RawContentStream.ToArray()) -eq 'Team Manager QA attachment') 'Attachment download preserves bytes'
    Assert ($download.Headers['Content-Disposition'] -match 'attachment' -and $download.Headers['Content-Type'] -match 'application/octet-stream' -and $download.Headers['X-Content-Type-Options'] -eq 'nosniff') 'Download uses attachment disposition and disables MIME sniffing'
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=Attachment&attachmentId=$attachmentId" -WebSession $regular | Out-Null
        throw 'Nonmember downloaded private attachment'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Private attachment is hidden from nonmembers'
    }
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue&handler=Attachment&attachmentId=$attachmentId" -WebSession $admin | Out-Null
        throw 'Attachment was served under another issue'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Attachment ID must belong to the requested issue'
    }
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$id&handler=RemoveAttachment&attachmentId=$attachmentId" -Method Post -WebSession $admin -Body @{__RequestVerificationToken=[System.Net.WebUtility]::HtmlDecode($token)} -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert ($page.StatusCode -eq 303) 'Removal redirects to the issue detail'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueAttachments WHERE Id=$attachmentId") -eq 0) 'Issue editor can remove attachment'
    $library=Invoke-WebRequest -UseBasicParsing "$base/ProjectDocuments?projectId=$parent" -WebSession $admin
    $token=[regex]::Match($library.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    $documentUpload=@(
        "--$boundary",
        'Content-Disposition: form-data; name="__RequestVerificationToken"',
        '',
        [System.Net.WebUtility]::HtmlDecode($token),
        "--$boundary",
        'Content-Disposition: form-data; name="title"',
        '',
        'QA project document',
        "--$boundary",
        'Content-Disposition: form-data; name="description"',
        '',
        'QA <document description>',
        "--$boundary",
        'Content-Disposition: form-data; name="attachment"; filename="qa-project-document.txt"',
        'Content-Type: text/plain',
        '',
        'Team Manager QA project document',
        "--$boundary--",
        ''
    ) -join "`r`n"
    $response=Invoke-WebRequest -UseBasicParsing "$base/ProjectDocuments?projectId=$parent&handler=Add" -Method Post -WebSession $admin -Body $documentUpload -ContentType "multipart/form-data; boundary=$boundary" -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert ($response.StatusCode -eq 303) 'Project manager can upload a document with a title and description'
    $library=Invoke-WebRequest -UseBasicParsing "$base/ProjectDocuments?projectId=$parent" -WebSession $admin
    Assert ($library.Content.Contains('QA project document') -and $library.Content.Contains('QA &lt;document description&gt;')) 'Project library displays document metadata safely'
    $documentId=Sql "SELECT Id FROM dbo.ProjectDocuments WHERE ProjectId=$parent AND Title=N'QA project document'"
    $documentUrl="$base/ProjectDocuments?projectId=$parent&handler=Download&documentId=$documentId"
    $response=Invoke-WebRequest -UseBasicParsing $documentUrl -WebSession $admin
    Assert ([Text.Encoding]::ASCII.GetString($response.RawContentStream.ToArray()) -eq 'Team Manager QA project document' -and $response.Headers['Content-Disposition'] -match 'attachment') 'Project file download returns the original bytes as an attachment'
    try {
        Invoke-WebRequest -UseBasicParsing $documentUrl -WebSession $regular | Out-Null
        throw 'Nonmember downloaded private project document'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Private project document is hidden from nonmembers'
    }
    try {
        Invoke-WebRequest -UseBasicParsing "$base/ProjectDocuments?projectId=$project&handler=Download&documentId=$documentId" -WebSession $admin | Out-Null
        throw 'Document was served under another project'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Project document ID cannot be used under another project'
    }
    Sql "UPDATE dbo.Projects SET State='Closed' WHERE Id=$parent" | Out-Null
    $response=Invoke-WebRequest -UseBasicParsing $documentUrl -WebSession $admin
    Assert ($response.StatusCode -eq 200) 'Closed project documents remain downloadable'
    $admin.MaximumRedirection=5
    $response=Post "/ProjectDocuments?projectId=$parent&handler=Remove&documentId=$documentId" $admin @{}
    Assert ($response.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Closed project blocks document removal'
    Sql "UPDATE dbo.Projects SET State='Active' WHERE Id=$parent" | Out-Null
    $response=Invoke-WebRequest -UseBasicParsing "$base/ProjectDocuments?projectId=$parent&handler=Remove&documentId=$documentId" -Method Post -WebSession $admin -Body @{__RequestVerificationToken=[System.Net.WebUtility]::HtmlDecode($token)} -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert ($response.StatusCode -eq 303 -and (Sql "SELECT COUNT(*) FROM dbo.ProjectDocuments WHERE Id=$documentId") -eq 0) 'Project manager can remove an active project document'
    $admin.MaximumRedirection=5
    $page=Post "/Forum?handler=Create&projectId=$project" $regular @{title='Forbidden';body='No posting permission'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Public forum access does not grant posting'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$project;role='Developer'}
    $page=Post "/Forum?handler=Create&projectId=$project" $regular @{title='QA forum topic';body='QA <forum topic>'}
    $topicId=Sql "SELECT Id FROM dbo.ForumTopics WHERE ProjectId=$project AND Title=N'QA forum topic'"
    Assert ($page.Content.Contains('QA &lt;forum topic&gt;')) 'Forum member can create a topic with escaped content'
    $page=Post "/Forum?handler=Reply&projectId=$project&topicId=$topicId" $regular @{body='QA forum reply'}
    Assert ($page.Content.Contains('QA forum reply')) 'Forum member can reply'
    $page=Post "/Forum?handler=Lock&projectId=$project&topicId=$topicId" $regular @{locked='true'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Posting role cannot lock topics'
    $page=Post "/Forum?handler=Lock&projectId=$project&topicId=$topicId" $admin @{locked='true'}
    Assert ((Sql "SELECT CAST(IsLocked AS int) FROM dbo.ForumTopics WHERE Id=$topicId") -eq 1) 'Moderator can lock a topic'
    $page=Post "/Forum?handler=Reply&projectId=$project&topicId=$topicId" $admin @{body='Forbidden while locked'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Locked topics reject new replies even from administrators'
    $page=Post "/Forum?handler=Lock&projectId=$project&topicId=$topicId" $admin @{locked='false'}
    $page=Post "/Forum?handler=Reply&projectId=$project&topicId=$topicId" $regular @{body='QA reopened reply'}
    Assert ($page.Content.Contains('QA reopened reply')) 'Unlocking restores posting'
    $page=Post "/Forum?handler=Create&projectId=$parent" $admin @{title='QA private forum topic';body='Private topic'}
    $privateTopic=Sql "SELECT Id FROM dbo.ForumTopics WHERE ProjectId=$parent AND Title=N'QA private forum topic'"
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Forum?projectId=$project&topicId=$privateTopic" -WebSession $regular | Out-Null
        throw 'Private forum topic was visible through public project'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Forum topic cannot be accessed under a different project'
    }
    $page=Post "/IssueFields?projectId=$parent&handler=Create" $admin @{name='QA reference field'}
    Assert ($page.Content.Contains('QA reference field')) 'Manager can define a text field for the project'
    $field=Sql "SELECT Id FROM dbo.ProjectIssueFields WHERE ProjectId=$parent AND Name=N'QA reference field'"
    $page=Post "/IssueFields?projectId=$project&handler=Create" $regular @{name='Unauthorized field'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Issue editor cannot define project fields without project management'
    $revision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    $page=Post "/Issue?id=$id&handler=SetField" $admin @{fieldId=$field;revision=$revision;fieldValue='QA <reference>'}
    Assert ($page.Content.Contains('QA &lt;reference&gt;')) 'Issue editor can set and read a safely encoded field value'
    $page=Post "/Issue?id=$id&handler=SetField" $admin @{fieldId=$field;revision=$revision;fieldValue='Stale value'}
    Assert ((Sql "SELECT Value FROM dbo.IssueFieldValues WHERE IssueId=$id AND FieldId=$field") -eq 'QA <reference>') 'Stale issue revision cannot overwrite field value'
    $page=Post '/Profile?handler=Language' $admin @{language='en'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/IssueFields?projectId=$parent" -WebSession $admin
    Assert ($page.Content.Contains('<h1>Issue fields</h1>') -and $page.Content.Contains('Add field')) 'Field administration has English labels'
    $otherRevision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$publicIssue"
    $page=Post "/Issue?id=$publicIssue&handler=SetField" $admin @{fieldId=$field;revision=$otherRevision;fieldValue='Wrong project'}
    Assert ($page.Content.Contains('Select a field from this project.') -and (Sql "SELECT COUNT(*) FROM dbo.IssueFieldValues WHERE IssueId=$publicIssue") -eq 0) 'Foreign field is rejected with localized validation'
    $revision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    $page=Post "/Issue?id=$id&handler=Transfer" $admin @{revision=$revision;destination=$project;move='true'}
    Assert (-not $page.Content.Contains('QA reference field') -and (Sql "SELECT COUNT(*) FROM dbo.IssueFieldValues WHERE IssueId=$id AND FieldId=$field") -eq 1) 'Moving an issue hides and preserves the original project field value'
    $revision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$id"
    $page=Post "/Issue?id=$id&handler=Transfer" $admin @{revision=$revision;destination=$parent;move='true'}
    Assert ($page.Content.Contains('QA &lt;reference&gt;')) 'Returning an issue to the original project restores its field value'
    foreach ($scope in @('user','project','time')) {
        $page=Post "/CustomFields?handler=Create&scope=$scope" $admin @{name="QA $scope field"}
        Assert ($page.Content.Contains("QA $scope field") -and $page.Content.Contains('Custom fields')) "$scope field definition is configurable in English"
    }
    $userField=Sql "SELECT Id FROM dbo.CustomFields WHERE Scope='user' AND Name=N'QA user field'"
    $projectField=Sql "SELECT Id FROM dbo.CustomFields WHERE Scope='project' AND Name=N'QA project field'"
    $timeField=Sql "SELECT Id FROM dbo.CustomFields WHERE Scope='time' AND Name=N'QA time field'"
    $page=Post "/CustomFields?handler=Value&scope=user&entityId=$uid" $regular @{fieldId=$userField;revision=0;fieldValue='QA personal field'}
    Assert ($page.Content.Contains('QA personal field') -and $page.Content -match '<html lang="hr"') 'User can set a profile field in Croatian'
    $page=Post "/CustomFields?handler=Value&scope=user&entityId=$uid" $regular @{fieldId=$userField;revision=0;fieldValue='QA stale field'}
    Assert ((Sql "SELECT Value FROM dbo.UserFieldValues WHERE EntityId=$uid AND FieldId=$userField") -eq 'QA personal field') 'Stale profile field revision is rejected'
    $administratorId=Sql "SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=N'admin'"
    try {
        Invoke-WebRequest -UseBasicParsing "$base/CustomFields?scope=user&entityId=$administratorId" -WebSession $regular | Out-Null
        throw 'Other profile fields were visible'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Profile field values are private to the owner and user managers'
    }
    $page=Post "/CustomFields?handler=Value&scope=project&entityId=$project" $admin @{fieldId=$projectField;revision=0;fieldValue='QA project value'}
    Assert ($page.Content.Contains('QA project value') -and $page.Content.Contains('<h1>Project fields</h1>')) 'Project manager can set a project field in English'
    $page=Invoke-WebRequest -UseBasicParsing "$base/CustomFields?scope=project&entityId=$project" -WebSession $regular
    Assert ($page.Content.Contains('QA project value') -and -not $page.Content.Contains('name="fieldValue"')) 'Ordinary project members can read project fields'
    $page=Post "/CustomFields?handler=Value&scope=project&entityId=$project" $regular @{fieldId=$projectField;revision=1;fieldValue='QA unauthorized'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Project field edits require project management permission'
    $page=Post "/CustomFields?handler=Value&scope=project&entityId=$project" $admin @{fieldId=$userField;revision=0;fieldValue='QA wrong scope'}
    Assert ($page.Content.Contains('Select a field of this type.')) 'Field from one scope cannot be saved in another'
    $timeId=Sql "SELECT TOP (1) Id FROM dbo.TimeEntries WHERE IssueId=$id ORDER BY Id"
    $page=Post "/CustomFields?handler=Value&scope=time&entityId=$timeId" $admin @{fieldId=$timeField;revision=0;fieldValue='QA time value'}
    Assert ($page.Content.Contains('<h1>Time entry fields</h1>') -and $page.Content.Contains('QA time value')) 'Time entry author can set a custom field'
    $page=Post "/Profile?id=$uid&handler=Membership" $admin @{kind='project';scopeId=$parent;role='Developer'}
    $page=Post "/CustomFields?handler=Value&scope=time&entityId=$timeId" $regular @{fieldId=$timeField;revision=1;fieldValue='QA wrong author'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Project member cannot edit a time entry field owned by another user'
    Sql "UPDATE dbo.Projects SET TimeEnabled=0 WHERE Id=$parent" | Out-Null
    try {
        Invoke-WebRequest -UseBasicParsing "$base/CustomFields?scope=time&entityId=$timeId" -WebSession $regular | Out-Null
        throw 'Time fields were visible while time module disabled'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Time fields respect the disabled time module'
    }
    $page=Post "/IssueFields?projectId=$project&handler=Create" $admin @{name='QA visibility field'}
    $visibilityField=Sql "SELECT Id FROM dbo.ProjectIssueFields WHERE ProjectId=$project AND Name=N'QA visibility field'"
    $revision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$publicIssue"
    $page=Post "/Issue?id=$publicIssue&handler=SetField" $admin @{fieldId=$visibilityField;revision=$revision;fieldValue='QA restricted value'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $regular
    Assert ($page.Content.Contains('QA restricted value')) 'Ordinary issue fields remain visible to issue editors by default'
    $page=Post "/IssueFields?projectId=$project&handler=Visibility&fieldId=$visibilityField" $admin @{managersOnly='true'}
    Assert ($page.Content.Contains('Project managers only')) 'Field visibility administration renders English labels'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $regular
    Assert (-not $page.Content.Contains('QA visibility field') -and -not $page.Content.Contains('QA restricted value')) 'Restricted field name and value are hidden from ordinary issue editors'
    $revision=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$publicIssue"
    $page=Post "/Issue?id=$publicIssue&handler=SetField" $regular @{fieldId=$visibilityField;revision=$revision;fieldValue='QA forbidden write'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Server rejects direct edits to a restricted field'
    $page=Post "/Issue?id=$publicIssue&handler=SetField" $regular @{fieldId=2147483647;revision=$revision;fieldValue='QA unknown field'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Ordinary editor cannot distinguish a restricted field from an unknown field'
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $admin
    Assert ($page.Content.Contains('QA restricted value')) 'Project manager can still read the restricted field'
    $page=Post "/IssueFields?projectId=$project&handler=Visibility&fieldId=$visibilityField" $admin @{managersOnly='false'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$publicIssue" -WebSession $regular
    Assert ($page.Content.Contains('QA restricted value')) 'Restoring standard visibility exposes the existing value'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE Subject=N'#$visibilityField' AND Action IN ('project.issue-field.restrict','project.issue-field.share')") -eq 2) 'Field visibility changes are audited'
    $page=Post "/Roadmap?handler=Save&projectId=$project" $admin @{kind='version';name='QA privacy roadmap'}
    $privacyItem=Sql "SELECT Id FROM dbo.ProjectMilestones WHERE ProjectId=$project AND Name=N'QA privacy roadmap'"
    $privacyInput=@{'Input.Title'='QA confidential issue';'Input.Description'='QA confidential detail';'Input.Type'='Zadatak';'Input.Priority'='Normalan';'Input.Status'='Novo';'Input.DueDate'='2026-12-20';'Input.RoadmapItemId'=$privacyItem}
    $page=Post "/Issue?handler=Save&projectId=$project" $admin $privacyInput
    $secretIssue=Sql "SELECT Id FROM dbo.Issues WHERE ProjectId=$project AND Title=N'QA confidential issue'"
    $page=Post "/Issue?id=$secretIssue&handler=Watch" $regular @{watch='true'}
    $page=Post "/Issue?id=$secretIssue&handler=Time" $admin @{date='2026-12-19';hours='2';note='QA private time note';activity='QA secret activity'}
    $secretTime=Sql "SELECT MAX(Id) FROM dbo.TimeEntries WHERE IssueId=$secretIssue"
    $page=Post "/Issue?id=$publicIssue&handler=Link" $admin @{otherId=$secretIssue;relation='related'}
    $privacyInput['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$secretIssue"
    $privacyInput['Input.IsPrivate']='true'
    $page=Post "/Issue?id=$secretIssue&handler=Save" $admin $privacyInput
    Assert ($page.Content.Contains('Private issue') -and (Sql "SELECT CAST(IsPrivate AS int) FROM dbo.Issues WHERE Id=$secretIssue") -eq 1) 'Manager can set private visibility using the English form'
    foreach ($path in @("/Issue?id=$secretIssue","/CustomFields?scope=time&entityId=$secretTime")) {
        try {
            Invoke-WebRequest -UseBasicParsing "$base$path" -WebSession $regular | Out-Null
            throw "Unauthorized read: $path"
        } catch [System.Net.WebException] {
            Assert ([int]$_.Exception.Response.StatusCode -eq 404) "$path hides private issue data"
        }
    }
    foreach ($path in @("/Issues?ProjectId=$project",'/Search?Query=QA%20confidential','/Calendar?Month=2026-12','/Gantt?Month=2026-12','/Activity','/Notifications','/Dashboard',"/Issue?id=$publicIssue")) {
        $page=Invoke-WebRequest -UseBasicParsing "$base$path" -WebSession $regular
        Assert (-not $page.Content.Contains('QA confidential issue')) "$path excludes the private issue"
    }
    $csv=Invoke-WebRequest -UseBasicParsing "$base/Issues?handler=Csv&ProjectId=$project" -WebSession $regular
    Assert (-not ([Text.Encoding]::UTF8.GetString($csv.RawContentStream.ToArray()).Contains('QA confidential issue'))) 'CSV excludes private issues'
    $report=Invoke-WebRequest -UseBasicParsing "$base/TimeReport?ProjectId=$project&Activity=QA%20secret%20activity" -WebSession $regular
    Assert (-not $report.Content.Contains('<td>QA secret activity</td>')) 'Time reports exclude private issue hours'
    $roadmap=Invoke-WebRequest -UseBasicParsing "$base/Roadmap?projectId=$project" -WebSession $regular
    Assert ($roadmap.Content.Contains('QA privacy roadmap') -and -not $roadmap.Content.Contains('0 od 1 zatvorenih zadataka')) 'Roadmap excludes private issues from status counts'
    $privacyInput['Input.AssigneeId']=$uid
    $privacyInput['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$secretIssue"
    $page=Post "/Issue?id=$secretIssue&handler=Save" $admin $privacyInput
    $page=Invoke-WebRequest -UseBasicParsing "$base/Issue?id=$secretIssue" -WebSession $regular
    Assert ($page.Content.Contains('Privatni zadatak')) 'Assigned user with project access can read the private issue in Croatian'
    $privacyInput['Input.IsPrivate']='false'
    $privacyInput['Input.Revision']=Sql "SELECT Revision FROM dbo.Issues WHERE Id=$secretIssue"
    $page=Post "/Issue?id=$secretIssue&handler=Save" $regular $privacyInput
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Assignee cannot make another author private issue public'
    $page=Post "/Issue?handler=Save&projectId=$project" $regular @{'Input.Title'='QA private by author';'Input.Description'='Author test';'Input.Type'='Zadatak';'Input.Priority'='Normalan';'Input.Status'='Novo';'Input.IsPrivate'='true'}
    Assert ($page.Content.Contains('QA private by author') -and $page.Content.Contains('Privatni zadatak')) 'Issue creator can read their private unassigned issue'
    $page=Post "/Repositories?projectId=$project&handler=Add" $admin @{name='QA repository';url='https://example.com/engineering/team-manager'}
    Assert ($page.Content.Contains('<h1>Git repositories</h1>') -and $page.Content.Contains('https://example.com/engineering/team-manager')) 'Project manager can link a repository in English'
    $repositoryId=Sql "SELECT Id FROM dbo.ProjectRepositories WHERE ProjectId=$project AND Name=N'QA repository'"
    $page=Invoke-WebRequest -UseBasicParsing "$base/Repositories?projectId=$project" -WebSession $regular
    Assert ($page.Content.Contains('<h1>Git repozitoriji</h1>') -and $page.Content.Contains('QA repository')) 'Project viewer can see linked repositories in Croatian'
    $page=Post "/Repositories?projectId=$project&handler=Add" $regular @{name='QA forbidden repository';url='https://example.com/other'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Repository management requires project management permission'
    $page=Post "/Repositories?projectId=$project&handler=Add" $admin @{name='QA invalid repository';url='https://example.com/repo?token=example'}
    Assert ($page.Content.Contains('without credentials, query parameters, or a fragment') -and (Sql "SELECT COUNT(*) FROM dbo.ProjectRepositories WHERE Name=N'QA invalid repository'") -eq 0) 'Repository rejects URLs containing query parameters'
    $page=Post "/Repositories?projectId=$parent&handler=Add" $admin @{name='QA private repository';url='https://example.com/private/repo'}
    Sql "DELETE dbo.ProjectMembers WHERE ProjectId=$parent AND UserId=$uid" | Out-Null
    try {
        Invoke-WebRequest -UseBasicParsing "$base/Repositories?projectId=$parent" -WebSession $regular | Out-Null
        throw 'Private project repositories were visible'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Repository list respects private project access'
    }
    try {
        Post "/Repositories?projectId=$parent&handler=Remove&repositoryId=$repositoryId" $admin @{} | Out-Null
        throw 'Repository could be removed from another project'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Repository removal checks project ownership'
    }
    $historyPath=[Environment]::GetEnvironmentVariable("RepositoryHistory__Paths__$repositoryId")
    if ($historyPath) {
        $page=Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$project&repositoryId=$repositoryId" -WebSession $admin
        $head=(& git -C $historyPath rev-parse HEAD).Trim()
        Assert ($page.Content.Contains('<h1>Commit history</h1>') -and $page.Content.Contains($head.Substring(0,12))) 'Commit history lists real Git commits in English'
        $page=Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$project&repositoryId=$repositoryId&commit=$head" -WebSession $admin
        Assert ($page.Content.Contains('Commit diff') -and $page.Content.Contains('diff --git')) 'Selected commit shows a real Git diff'
        $page=Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$project&repositoryId=$repositoryId" -WebSession $regular
        Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Git source requires explicit repository permission'
        try {
            Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$parent&repositoryId=$repositoryId" -WebSession $admin | Out-Null
            throw 'History was accessible with wrong project'
        } catch [System.Net.WebException] {
            Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Git history checks repository ownership'
        }
        try {
            Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$project&repositoryId=$repositoryId&commit=HEAD" -WebSession $admin | Out-Null
            throw 'Git accepted an arbitrary ref'
        } catch [System.Net.WebException] {
            Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Only listed commit IDs are accepted'
        }
        $page=Post '/Profile?handler=Language' $admin @{language='hr'}
        $page=Invoke-WebRequest -UseBasicParsing "$base/RepositoryHistory?projectId=$project&repositoryId=$repositoryId&commit=$head" -WebSession $admin
        Assert ($page.Content.Contains('Pregled commitova') -and $page.Content.Contains('Razlike u commitu')) 'History and diff render Croatian labels'
    }
    $page=Post "/Repositories?projectId=$project&handler=Remove&repositoryId=$repositoryId" $admin @{}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.ProjectRepositories WHERE Id=$repositoryId") -eq 0) 'Project manager can unlink a repository'
    Assert ((Sql "SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE (Action='project.repository.add' AND Subject=N'QA repository') OR (Action='project.repository.remove' AND Subject=N'#$repositoryId')") -eq 2) 'Repository linking and unlinking are audited'
    $page=Post '/Profile?handler=Language' $admin @{language='en'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/ApiTokens" -WebSession $admin
    Assert ($page.Content.Contains('<h1>API tokens</h1>')) 'API token settings are available in English'
    $page=Post '/ApiTokens?handler=Create' $regular @{name='QA read token'}
    $apiToken=[regex]::Match($page.Content,'value="(tm_[a-f0-9]{64})"').Groups[1].Value
    Assert ($apiToken.Length -eq 67 -and $page.Content.Contains('<h1>API tokeni</h1>') -and $page.Headers['Cache-Control'] -match 'no-store') 'Token is shown once in the Croatian creation response'
    $page=Invoke-WebRequest -UseBasicParsing "$base/ApiTokens" -WebSession $regular
    Assert (-not $page.Content.Contains('tm_') -and $page.Content.Contains('QA read token')) 'Token list omits the secret'
    try {
        Invoke-WebRequest -UseBasicParsing "$base/api/v1/projects" -WebSession $regular | Out-Null
        throw 'API accepted a session cookie'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 401) 'API requires a bearer token even with a signed-in web session'
    }
    $bearer=@{Authorization="Bearer $apiToken"}
    $api=Invoke-WebRequest -UseBasicParsing "$base/api/v1/projects" -Headers $bearer
    Assert ($api.Content.Contains('"id":'+$project) -and -not $api.Content.Contains('QA parent updated')) 'Valid token lists only accessible projects'
    Sql "UPDATE dbo.Issues SET AssigneeId=NULL WHERE Id=$secretIssue" | Out-Null
    $api=Invoke-WebRequest -UseBasicParsing "$base/api/v1/projects/$project/issues" -Headers $bearer
    Assert ($api.Content.Contains('"id":'+$publicIssue) -and -not $api.Content.Contains('QA confidential issue')) 'Issue API excludes private issues without access'
    try {
        Invoke-WebRequest -UseBasicParsing "$base/api/v1/issues/$secretIssue" -Headers $bearer | Out-Null
        throw 'API disclosed a private issue'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Issue detail API enforces privacy'
    }
    $tokenId=Sql "SELECT Id FROM dbo.ApiTokens WHERE UserId=$uid AND Name=N'QA read token'"
    $page=Post "/ApiTokens?handler=Revoke&id=$tokenId" $regular @{}
    try {
        Invoke-WebRequest -UseBasicParsing "$base/api/v1/projects" -Headers $bearer | Out-Null
        throw 'API accepted a revoked token'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 401) 'Revoked token is rejected'
    }
    $page=Post '/ApiTokens?handler=Create' $regular @{name='QA session token'}
    $sessionToken=[regex]::Match($page.Content,'value="(tm_[a-f0-9]{64})"').Groups[1].Value
    Assert ($sessionToken.Length -eq 67) 'Account can create a replacement token'
    $page=Post "/Profile?id=$uid&handler=Account" $admin @{accountAction='revoke'}
    try {
        Invoke-WebRequest -UseBasicParsing "$base/api/v1/projects" -Headers @{Authorization="Bearer $sessionToken"} | Out-Null
        throw 'API accepted an invalidated session version'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 401) 'Revoking account sessions also invalidates existing tokens'
    }
    $regular=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post '/Login' $regular @{EmployeeNumber='qatest';Password='QA-password-123'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/EmailNotifications" -WebSession $admin
    Assert ($page.Content.Contains('<h1>Email notifications</h1>')) 'Email settings are available in English'
    $page=Post '/EmailNotifications' $regular @{address='not-an-email';sendWatchedIssues='true'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.EmailNotificationPreferences WHERE UserId=$uid") -eq 0) 'Invalid addresses cannot enable email notifications'
    $page=Post '/EmailNotifications' $regular @{address='qa@example.test';sendWatchedIssues='true'}
    Assert ($page.Content.Contains('qa@example.test') -and $page.Content -match '<html lang="hr"') 'User can save personal email settings in Croatian'
    $page=Invoke-WebRequest -UseBasicParsing "$base/EmailNotifications" -WebSession $admin
    Assert (-not $page.Content.Contains('qa@example.test')) 'Email address preference is private to its owner'
    $page=Post "/Issue?id=$publicIssue&handler=Watch" $regular @{watch='true'}
    $page=Post "/Issue?id=$publicIssue&handler=Comment" $admin @{text='QA email event'}
    $emailEvent=Sql "SELECT MAX(Id) FROM dbo.IssueEvents WHERE IssueId=$publicIssue"
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE UserId=$uid AND EventId=$emailEvent") -eq 1) 'Watched issue activity for opted-in recipient is queued'
    $page=Post "/Issue?id=$secretIssue&handler=Comment" $admin @{text='QA forbidden email event'}
    $privateEvent=Sql "SELECT MAX(Id) FROM dbo.IssueEvents WHERE IssueId=$secretIssue"
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE EventId=$privateEvent") -eq 0) 'Activity on an inaccessible private issue is not queued'
    $page=Post '/EmailNotifications' $regular @{address='qa@example.test'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE UserId=$uid") -eq 0) 'Disabling email preferences clears pending deliveries'
    $page=Post "/Issue?id=$publicIssue&handler=Comment" $admin @{text='QA event after opt-out'}
    Assert ((Sql "SELECT COUNT(*) FROM dbo.IssueEmailQueue WHERE UserId=$uid") -eq 0) 'Opted-out watcher receives no new email jobs'
    $page=Invoke-WebRequest -UseBasicParsing "$base/MailIntake?projectId=$project" -WebSession $admin
    Assert ($page.Content.Contains('<h1>Incoming email</h1>')) 'Email intake settings are visible in English to project managers'
    $page=Post '/Profile?handler=Language' $admin @{language='hr'}
    $page=Invoke-WebRequest -UseBasicParsing "$base/MailIntake?projectId=$project" -WebSession $admin
    Assert ($page.Content -match '<html lang="hr"' -and $page.Content.Contains("POST /mail/intake/$project")) 'Email intake settings render in Croatian'
    $intakeSecret=[Environment]::GetEnvironmentVariable("MailIntake__Routes__${project}__Secret")
    if ($intakeSecret) {
        $endpoint="$base/mail/intake/$project"
        $payload=@{messageId='<qa-inbound@example.test>';subject='QA received email';text='QA plain email body'} | ConvertTo-Json -Compress
        try {
            Invoke-WebRequest -UseBasicParsing $endpoint -Method Post -ContentType 'application/json' -Body $payload | Out-Null
            throw 'Unauthenticated intake accepted the message'
        } catch [System.Net.WebException] {
            Assert ([int]$_.Exception.Response.StatusCode -eq 401) 'Email intake requires a separate gateway secret'
        }
        $gatewayHeader=@{Authorization="Bearer $intakeSecret"}
        $response=Invoke-WebRequest -UseBasicParsing $endpoint -Method Post -ContentType 'application/json' -Body $payload -Headers $gatewayHeader
        $issueFromMail=($response.Content | ConvertFrom-Json).issueId
        Assert ($response.StatusCode -eq 201 -and (Sql "SELECT COUNT(*) FROM dbo.Issues WHERE Id=$issueFromMail AND ProjectId=$project AND IsPrivate=1 AND Description=N'QA plain email body'") -eq 1) 'Gateway email creates a private issue in the configured project'
        $response=Invoke-WebRequest -UseBasicParsing $endpoint -Method Post -ContentType 'application/json' -Body $payload -Headers $gatewayHeader
        Assert ($response.StatusCode -eq 200 -and ($response.Content | ConvertFrom-Json).duplicate -and (Sql "SELECT COUNT(*) FROM dbo.ReceivedIssueMail WHERE IssueId=$issueFromMail") -eq 1) 'Replaying a message does not duplicate the issue'
        $page=Invoke-WebRequest -UseBasicParsing "$base/Issues?ProjectId=$project" -WebSession $regular
        Assert (-not $page.Content.Contains('QA received email')) 'Imported email content respects private issue rules'
        try {
            Invoke-WebRequest -UseBasicParsing "$base/mail/intake/$parent" -Method Post -ContentType 'application/json' -Body $payload -Headers $gatewayHeader | Out-Null
            throw 'Gateway credential could be reused for another project'
        } catch [System.Net.WebException] {
            Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Unconfigured project route is unavailable'
        }
    }
} finally {
    Stop-App
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -eq 'Open'){$connection.ChangeDatabase('master');if($dbName -match '^TeamManager_IssueTest_[0-9a-f]{32}$'){Sql "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null}}
    $connection.Dispose()
}

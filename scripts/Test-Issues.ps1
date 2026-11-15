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
} finally {
    Stop-App
    $env:ConnectionStrings__TeamManager=$oldConnection;$env:ASPNETCORE_ENVIRONMENT=$oldEnvironment
    if($connection.State -eq 'Open'){$connection.ChangeDatabase('master');if($dbName -match '^TeamManager_IssueTest_[0-9a-f]{32}$'){Sql "IF DB_ID('$dbName') IS NOT NULL BEGIN ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]; END" | Out-Null}}
    $connection.Dispose()
}

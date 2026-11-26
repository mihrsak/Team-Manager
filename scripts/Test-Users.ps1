param([string]$BaseUrl='https://localhost:7043',[string]$AdminEmployee='admin',[Parameter(Mandatory=$true)][string]$AdminPassword,[string]$Database='TeamManager')
$ErrorActionPreference='Stop'
function Assert($condition,$message){if(-not $condition){throw $message};Write-Host "PASS: $message"}
function Invoke-Page($parameters,$expectedStatus=200){
    try { $page=Invoke-WebRequest @parameters }
    catch {
        $response=$_.Exception.Response
        if($expectedStatus -ne 403 -or !$response -or [int]$response.StatusCode -ne 403){throw}
        if($response.PSObject.Methods.Name -contains 'GetResponseStream'){
            $reader=New-Object IO.StreamReader($response.GetResponseStream())
            try{$content=$reader.ReadToEnd()}finally{$reader.Dispose()}
            $uri=$response.ResponseUri
        }else{
            $content=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $uri=$response.RequestMessage.RequestUri
        }
        $page=[pscustomobject]@{Content=$content;StatusCode=403;BaseResponse=[pscustomobject]@{ResponseUri=$uri}}
    }
    if([int]$page.StatusCode -ne $expectedStatus){throw "Unexpected HTTP status: $($page.StatusCode), expected $expectedStatus"}
    return $page
}
function Get-Page($path,$session,$expectedStatus=200){Invoke-Page @{UseBasicParsing=$true;Uri="$BaseUrl$path";WebSession=$session} $expectedStatus}
function Post-Page($path,$session,$data,$expectedStatus=200){
    $page=Get-Page $path $session
    $match=[regex]::Match($page.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if(-not $match.Success){throw "No antiforgery token: $path"}
    $data['__RequestVerificationToken']=[System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    Invoke-Page @{UseBasicParsing=$true;Uri="$BaseUrl$path";Method='Post';Body=$data;WebSession=$session} $expectedStatus
}
$admin=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$regular=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$suffix=[guid]::NewGuid().ToString('N').Substring(0,8)
$employee="qa$suffix"
$role="QA$suffix"
$approvedEmployee="ra$suffix"
$rejectedEmployee="rr$suffix"
$testPassword=[guid]::NewGuid().ToString('N')
$db=New-Object System.Data.SqlClient.SqlConnection "Server=localhost\SQLEXPRESS;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$db.Open()
try {
    $page=Get-Page '/Users' $regular
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Anonymous access redirects to login'
    $page=Post-Page '/Login' $admin @{EmployeeNumber=$AdminEmployee;Password=$AdminPassword}
    $page=Get-Page '/Users' $admin
    Assert ($page.Content.Contains('Novi korisnik')) 'Administrator login'
    $page=Post-Page '/Roles' $admin @{name='Administrator';description='';rights='users.manage';create='false'}
    Assert ($page.Content.Contains('posljednjeg')) 'Last full administrator cannot be removed'
    $page=Post-Page '/Users?handler=Create' $admin @{employee=$employee;name='QA user';job='Tester';password=$testPassword}
    Assert ($page.Content.Contains($employee)) 'Create user'
    $page=Post-Page '/Login' $regular @{EmployeeNumber=$employee;Password=$testPassword}
    Assert (-not $page.Content.Contains('Novi korisnik')) 'Regular user cannot see user administration'
    $page=Get-Page '/Roles' $regular 403
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Role administration denied for regular user'
    # Profile forms normally include id; obtain it for subsequent authenticated actions.
    $cmd=$db.CreateCommand();$cmd.CommandText='SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=@e';[void]$cmd.Parameters.AddWithValue('@e',$employee);$id=[int]$cmd.ExecuteScalar()
    $page=Post-Page "/Profile?id=$id&handler=Save" $regular @{name='QA updated';job='Developer'}
    Assert ($page.Content.Contains('QA updated')) 'Own profile can be edited'
    $page=Post-Page '/Roles' $admin @{name=$role;description='QA permission test';rights='users.manage';create='true'}
    Assert ($page.Content.Contains($role)) 'Custom role created'
    $page=Post-Page "/Profile?id=$id&handler=GlobalRole" $admin @{role=$role;remove='false'}
    $page=Get-Page '/Users' $regular
    Assert ($page.Content.Contains('Novi korisnik')) 'New permission takes effect without logging in again'
    $page=Post-Page "/Profile?id=$id&handler=GlobalRole" $admin @{role=$role;remove='true'}
    $page=Get-Page '/Users' $regular
    Assert (-not $page.Content.Contains('Novi korisnik')) 'Revoked permission takes effect immediately'
    $page=Post-Page '/Users?handler=Create' $regular @{employee="blocked$suffix";name='Forbidden';password=$testPassword} 403
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Forged user-create request is rejected by server'
    $page=Get-Page '/Groups' $regular 403
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Regular user cannot manage groups'
    $page=Post-Page '/Groups?handler=Create' $admin @{name=$role}
    Assert ($page.Content.Contains($role)) 'Administrator can create a group'
    $cmd=$db.CreateCommand();$cmd.CommandText='SELECT Id FROM dbo.UserGroups WHERE Name=@name';[void]$cmd.Parameters.AddWithValue('@name',$role);$groupId=[int]$cmd.ExecuteScalar()
    $page=Post-Page "/Groups?handler=Role&groupId=$groupId" $admin @{role=$role}
    $page=Post-Page "/Groups?handler=Member&groupId=$groupId" $admin @{userId=$id}
    $page=Get-Page '/Users' $regular
    Assert ($page.Content.Contains('Novi korisnik')) 'Group membership grants global role permission'
    $page=Post-Page "/Groups?handler=Role&groupId=$groupId" $admin @{role=$role;remove='true'}
    $page=Get-Page '/Users' $regular
    Assert (-not $page.Content.Contains('Novi korisnik')) 'Removing group role revokes permission'
    $page=Post-Page "/Groups?handler=Role&groupId=$groupId" $admin @{role=$role}
    $page=Post-Page "/Groups?handler=Member&groupId=$groupId" $admin @{userId=$id;remove='true'}
    $page=Get-Page '/Users' $regular
    Assert (-not $page.Content.Contains('Novi korisnik')) 'Removing group member revokes permission'
    $cmd=$db.CreateCommand();$cmd.CommandText="INSERT dbo.Projects(Name) OUTPUT inserted.Id VALUES(@name)";[void]$cmd.Parameters.AddWithValue('@name',$role);$projectId=[int]$cmd.ExecuteScalar()
    $page=Post-Page "/Profile?id=$id&handler=Membership" $admin @{kind='project';scopeId=$projectId;role='Moderator'}
    $page=Post-Page "/Profile?id=$id&handler=Membership" $regular @{kind='project';scopeId=$projectId;role='Moderator'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Profile') 'Scoped membership permission works in assigned project'
    $page=Post-Page "/Profile?id=$id&handler=Membership" $regular @{kind='project';scopeId=$projectId;role='Administrator'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Scoped manager cannot grant stronger permissions'
    $page=Post-Page "/Profile?id=$id&handler=Membership" $regular @{kind='project';scopeId=2147483647;role='Moderator'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Scoped permission does not leak to another project'
    $page=Get-Page '/Roles' $regular
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Scoped role does not grant global role administration'
    $page=Get-Page '/Issues' $regular
    Assert (-not $page.Content.Contains('Novi zadatak')) 'Base project role cannot create issues'
    $page=Post-Page "/Profile?id=$id&handler=Membership" $admin @{kind='project';scopeId=$projectId;role='Developer'}
    Assert ($page.Content.Contains('Moderator') -and $page.Content.Contains('Developer')) 'Additional role preserves base membership'
    $page=Get-Page '/Issues' $regular
    Assert ($page.Content.Contains('Novi zadatak')) 'Additional role grants issue creation'
    $page=Post-Page "/Profile?id=$id&handler=Membership" $admin @{kind='project';scopeId=$projectId;role='Developer';removeRole='true'}
    $page=Get-Page '/Issues' $regular
    Assert (-not $page.Content.Contains('Novi zadatak')) 'Removing additional role revokes issue creation'
    $page=Post-Page "/Profile?id=$id&handler=Password" $regular @{currentPassword=$testPassword;newPassword="New$testPassword";confirmPassword="New$testPassword"}
    $fresh=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Changed password can log in'
    $page=Post-Page "/Profile?id=$id&handler=Account" $fresh @{accountAction='revoke'}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Regular user cannot revoke sessions'
    $page=Post-Page "/Profile?id=$id&handler=Account" $admin @{accountAction='revoke'}
    $page=Get-Page '/Users' $fresh
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Revoked cookie is rejected on next request'
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Revocation preserves credentials'
    $page=Post-Page "/Profile?id=$id&handler=Account" $admin @{accountAction='deactivate'}
    $page=Get-Page '/Users' $fresh
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Deactivation revokes existing sessions'
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Deactivated account cannot sign in'
    $page=Post-Page "/Profile?id=$id&handler=Account" $admin @{accountAction='activate'}
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Reactivated account can sign in'
    $page=Post-Page "/Profile?id=$id&handler=Account" $admin @{accountAction='reset';password="Reset$testPassword"}
    $page=Get-Page '/Users' $fresh
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Password reset revokes existing sessions'
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Old password is rejected after reset'
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="Reset$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Reset password works'
    $page=Get-Page '/AdministrationAudit' $admin
    Assert ($page.Content.Contains($employee) -and $page.Content.Contains('Reset lozinke')) 'Full administrator can view account audit'
    $page=Get-Page '/AdministrationAudit' $fresh
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Regular account cannot read the audit log'
    $cmd=$db.CreateCommand()
    $cmd.CommandText="SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE ActorEmployeeNumber=@actor AND Subject=@subject AND Action IN ('account.reset','account.activate','account.deactivate','account.revoke')"
    [void]$cmd.Parameters.AddWithValue('@actor',$AdminEmployee)
    [void]$cmd.Parameters.AddWithValue('@subject',$employee)
    Assert ($cmd.ExecuteScalar() -eq 4) 'Exactly one record per successful administrator action; rejected actions leave no entry'
    $cmd=$db.CreateCommand()
    $cmd.CommandText="SELECT COUNT(DISTINCT Action) FROM dbo.AdministrationAudit WHERE ActorEmployeeNumber=@actor AND Action IN ('account.create','role.create','role.assign','role.revoke','group.create','group.role.add','group.role.remove','group.member.add','group.member.remove','membership.role.add','membership.role.remove')"
    [void]$cmd.Parameters.AddWithValue('@actor',$AdminEmployee)
    Assert ($cmd.ExecuteScalar() -eq 11) 'Audit covers account creation, global roles, groups, and scoped roles'
    $cmd=$db.CreateCommand()
    $cmd.CommandText="SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE Action='role.update' AND Subject=N'Administrator' AND Details=N'users.manage'"
    Assert ($cmd.ExecuteScalar() -eq 0) 'Rejected last-administrator change rolls back its audit entry'
    $page=Get-Page '/Profile' $fresh
    Assert ($page.Content.Contains('data-theme="light"')) 'Existing account uses the default light theme'
    $page=Post-Page '/Profile?handler=Theme' $fresh @{theme='dark'}
    Assert ($page.Content.Contains('data-theme="dark"')) 'Theme selection takes effect immediately'
    $page=Get-Page '/Users' $admin
    Assert ($page.Content.Contains('data-theme="light"')) 'Theme selection is isolated per account'
    $themed=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post-Page '/Login' $themed @{EmployeeNumber=$employee;Password="Reset$testPassword"}
    Assert ($page.Content.Contains('data-theme="dark"')) 'Theme survives a new login session'
    $page=Post-Page '/Profile?handler=Theme' $themed @{theme='system'}
    Assert ($page.Content.Contains('data-theme="system"') -and $page.Content.Contains('prefers-color-scheme: dark')) 'Device theme selection uses the browser preference'
    try {
        Post-Page '/Profile?handler=Theme' $themed @{theme='invalid'} | Out-Null
        throw 'Invalid theme was accepted'
    } catch [System.Net.WebException] {
        Assert ([int]$_.Exception.Response.StatusCode -eq 400) 'Invalid theme is rejected'
    }
    $page=Post-Page '/Profile?handler=Language' $themed @{language='en'}
    Assert ($page.Content.Contains('<html lang="en"') -and $page.Content.Contains('Save language')) 'User can choose English UI'
    $page=Get-Page '/Dashboard' $themed
    Assert ($page.Content.Contains('<h1>My tasks</h1>') -and $page.Content.Contains('Upcoming deadlines')) 'Dashboard renders localized interface text'
    $page=Get-Page '/Dashboard' $admin
    Assert ($page.Content.Contains('<html lang="hr"') -and $page.Content.Contains('<h1>Moji zadaci</h1>')) 'User language does not change other accounts'
    $newLanguageSession=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post-Page '/Login' $newLanguageSession @{EmployeeNumber=$employee;Password="Reset$testPassword"}
    Assert ($page.Content.Contains('<html lang="en"')) 'Language preference survives a new session'
    if ($env:ProjectExtensions__Links__0__EnglishLabel) {
        $page=Get-Page "/Project?id=$projectId" $newLanguageSession
        Assert ($page.Content.Contains($env:ProjectExtensions__Links__0__EnglishLabel)) 'Configured project extension renders localized navigation'
        Assert (-not $page.Content.Contains('QA rejected extension')) 'Unsafe URL scheme is omitted from navigation'
    }
    $englishPages=@{
        '/Projects'='Projects'
        '/Issues'='Project issues'
        '/Calendar'='Deadline calendar'
        '/Gantt'='Gantt view'
        '/Activity'='Activity'
        '/Notifications'='Notifications'
        '/TimeReport'='Time spent report'
        '/Users'='Users'
        "/Roadmap?projectId=$projectId"='Roadmap'
        "/ProjectNews?projectId=$projectId"='Project news'
        "/ProjectDocuments?projectId=$projectId"='Project documents'
        "/Wiki?projectId=$projectId"='Project wiki'
        "/Forum?projectId=$projectId"='Project forum'
    }
    foreach ($path in $englishPages.Keys) {
        $page=Get-Page $path $newLanguageSession
        Assert ($page.Content.Contains("<h1>$($englishPages[$path])</h1>")) "$path renders its English heading"
    }
    $page=Post-Page '/Profile?handler=Language' $admin @{language='en'}
    $page=Get-Page '/Roles' $admin
    Assert ($page.Content.Contains('<h1>Roles and permissions</h1>') -and $page.Content.Contains('Edit project wiki')) 'Administration shows English headings and permissions'
    $page=Get-Page '/AdministrationAudit' $admin
    Assert ($page.Content.Contains('Audit log') -and $page.Content.Contains('Password reset')) 'Audit event labels use English'
    $page=Post-Page '/Profile?handler=Language' $admin @{language='hr'}
    $browserEnglish=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $browserEnglish.Headers['Accept-Language']='en-US'
    $page=Get-Page '/Login' $browserEnglish
    Assert ($page.Content.Contains('<h1>Sign in</h1>')) 'Sign-in uses the preferred browser language before authentication'
    $page=Post-Page '/Login' $browserEnglish @{EmployeeNumber=$employee;Password='invalid-password'}
    Assert ($page.Content.Contains('Sign-in failed or the account is temporarily locked.')) 'Form validation displays a translated error'
    $page=Post-Page '/Profile?handler=Language' $newLanguageSession @{language='hr'}
    Assert ($page.Content.Contains('<html lang="hr"')) 'User can restore Croatian UI'
    if ($env:Registration__Enabled -eq 'true') {
        $applicant=New-Object Microsoft.PowerShell.Commands.WebRequestSession
        $applicant.Headers['Accept-Language']='en-US'
        $page=Get-Page '/Register' $applicant
        Assert ($page.Content.Contains('<h1>Registration request</h1>')) 'Registration form supports English'
        $croatianApplicant=New-Object Microsoft.PowerShell.Commands.WebRequestSession
        $croatianApplicant.Headers['Accept-Language']='hr-HR'
        $page=Get-Page '/Register' $croatianApplicant
        Assert ($page.Content.Contains('<h1>Zahtjev za registraciju</h1>')) 'Registration form supports Croatian'
        $page=Post-Page '/Register' $applicant @{employee=$approvedEmployee;name='QA registration';password=$testPassword;confirm=$testPassword}
        Assert ($page.Content.Contains('an administrator will review the request')) 'Applicant receives a generic registration response'
        $page=Post-Page '/Register' $applicant @{employee=$approvedEmployee;name='QA duplicate submission';password="New$testPassword";confirm="New$testPassword"}
        $cmd=$db.CreateCommand()
        $cmd.CommandText="SELECT Id FROM dbo.RegistrationRequests WHERE EmployeeNumber=@e AND DisplayName=N'QA registration' AND PasswordHash<>@password"
        [void]$cmd.Parameters.AddWithValue('@e',$approvedEmployee)
        [void]$cmd.Parameters.AddWithValue('@password',$testPassword)
        $registrationId=[int]$cmd.ExecuteScalar()
        Assert ($registrationId -gt 0) 'Duplicate submission preserves the first pending request and password hash'
        $page=Post-Page '/Login' $applicant @{EmployeeNumber=$approvedEmployee;Password=$testPassword}
        Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Pending applicant cannot sign in'
        $page=Get-Page '/RegistrationRequests' $fresh
        Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Regular user cannot review requests'
        $page=Post-Page "/RegistrationRequests?handler=Decision&id=$registrationId" $admin @{approve='true'}
        $page=Post-Page '/Login' $applicant @{EmployeeNumber=$approvedEmployee;Password=$testPassword}
        Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users' -and -not $page.Content.Contains('Novi korisnik')) 'Approved applicant can sign in without administrative rights'
        $page=Post-Page '/Register' $croatianApplicant @{employee=$rejectedEmployee;name='QA rejected';password=$testPassword;confirm=$testPassword}
        $cmd=$db.CreateCommand();$cmd.CommandText='SELECT Id FROM dbo.RegistrationRequests WHERE EmployeeNumber=@e';[void]$cmd.Parameters.AddWithValue('@e',$rejectedEmployee)
        $rejectedId=[int]$cmd.ExecuteScalar()
        $page=Post-Page "/RegistrationRequests?handler=Decision&id=$rejectedId" $admin @{approve='false'}
        $page=Post-Page '/Login' $croatianApplicant @{EmployeeNumber=$rejectedEmployee;Password=$testPassword}
        Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Login') 'Rejected applicant cannot sign in'
        $cmd=$db.CreateCommand()
        $cmd.CommandText="SELECT COUNT(*) FROM dbo.AdministrationAudit WHERE (Subject=@approved AND Action='registration.approve') OR (Subject=@rejected AND Action='registration.reject')"
        [void]$cmd.Parameters.AddWithValue('@approved',$approvedEmployee)
        [void]$cmd.Parameters.AddWithValue('@rejected',$rejectedEmployee)
        Assert ($cmd.ExecuteScalar() -eq 2) 'Approval and rejection are recorded in the audit log'
    }
} finally {
    $cmd=$db.CreateCommand();$cmd.CommandText='DELETE dbo.RegistrationRequests WHERE EmployeeNumber IN (@approved,@rejected); DELETE dbo.AppUsers WHERE EmployeeNumber IN (@approved,@rejected); DELETE dbo.UserGroupMembers WHERE GroupId IN (SELECT Id FROM dbo.UserGroups WHERE Name=@r); DELETE dbo.UserGroupRoles WHERE GroupId IN (SELECT Id FROM dbo.UserGroups WHERE Name=@r); DELETE dbo.UserGroups WHERE Name=@r; DELETE dbo.ProjectMembers WHERE UserId IN (SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=@e); DELETE dbo.Projects WHERE Name=@r; DELETE dbo.UserRoles WHERE UserId IN (SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=@e); DELETE dbo.AppUsers WHERE EmployeeNumber=@e; DELETE dbo.RolePermissions WHERE RoleName=@r; DELETE dbo.Roles WHERE Name=@r';[void]$cmd.Parameters.AddWithValue('@e',$employee);[void]$cmd.Parameters.AddWithValue('@r',$role);[void]$cmd.Parameters.AddWithValue('@approved',$approvedEmployee);[void]$cmd.Parameters.AddWithValue('@rejected',$rejectedEmployee);[void]$cmd.ExecuteNonQuery();$db.Dispose()
}

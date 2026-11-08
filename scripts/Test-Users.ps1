param([string]$BaseUrl='https://localhost:7043',[string]$AdminEmployee='admin',[Parameter(Mandatory=$true)][string]$AdminPassword)
$ErrorActionPreference='Stop'
function Assert($condition,$message){if(-not $condition){throw $message};Write-Host "PASS: $message"}
function Get-Page($path,$session){Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl$path" -WebSession $session}
function Post-Page($path,$session,$data){
    $page=Get-Page $path $session
    $match=[regex]::Match($page.Content,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if(-not $match.Success){throw "No antiforgery token: $path"}
    $data['__RequestVerificationToken']=[System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl$path" -Method Post -Body $data -WebSession $session
}
$admin=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$regular=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$suffix=[guid]::NewGuid().ToString('N').Substring(0,8)
$employee="qa$suffix"
$role="QA$suffix"
$testPassword=[guid]::NewGuid().ToString('N')
$db=New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=TeamManager;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
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
    $page=Get-Page '/Roles' $regular
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
    $page=Post-Page '/Users?handler=Create' $regular @{employee="blocked$suffix";name='Forbidden';password=$testPassword}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Denied') 'Forged user-create request is rejected by server'
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
    $page=Post-Page "/Profile?id=$id&handler=Password" $regular @{currentPassword=$testPassword;newPassword="New$testPassword";confirmPassword="New$testPassword"}
    $fresh=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page=Post-Page '/Login' $fresh @{EmployeeNumber=$employee;Password="New$testPassword"}
    Assert ($page.BaseResponse.ResponseUri.AbsolutePath -eq '/Users') 'Changed password can log in'
} finally {
    $cmd=$db.CreateCommand();$cmd.CommandText='DELETE dbo.ProjectMembers WHERE UserId IN (SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=@e); DELETE dbo.Projects WHERE Name=@r; DELETE dbo.UserRoles WHERE UserId IN (SELECT Id FROM dbo.AppUsers WHERE EmployeeNumber=@e); DELETE dbo.AppUsers WHERE EmployeeNumber=@e; DELETE dbo.RolePermissions WHERE RoleName=@r; DELETE dbo.Roles WHERE Name=@r';[void]$cmd.Parameters.AddWithValue('@e',$employee);[void]$cmd.Parameters.AddWithValue('@r',$role);[void]$cmd.ExecuteNonQuery();$db.Dispose()
}

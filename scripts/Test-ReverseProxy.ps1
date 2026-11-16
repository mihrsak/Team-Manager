param(
    [string]$ApplicationDll,
    [switch]$Preview
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$backend=7546
$frontend=7547
$url="https://localhost:$frontend"
$artifacts=Join-Path $root 'artifacts'
if(!$ApplicationDll){
    $build=Join-Path $artifacts 'ocp-proxy-build'
    dotnet build (Join-Path $root 'src/TeamManager.Web/TeamManager.Web.csproj') -o $build
    if($LASTEXITCODE){throw 'Build failed.'}
    $ApplicationDll=Join-Path $build 'TeamManager.Web.dll'
}
$dll=(Resolve-Path -LiteralPath $ApplicationDll).Path
if(Get-NetTCPConnection -LocalPort $backend,$frontend -State Listen -ErrorAction SilentlyContinue){
    throw 'A local proxy test port is already in use.'
}
$temp=Join-Path $artifacts ('proxy-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$pfx=Join-Path $temp 'localhost.pfx'
$cookies=Join-Path $temp 'cookies.txt'
$variables=@{}
$app=$proxy=$null
$keepRunning=$false

function Set-TestVariable($name,$value){
    $variables[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$value,'Process')
}
function Request([string[]]$options){
    $response=& curl.exe --silent --show-error --fail --max-time 15 @options
    if($LASTEXITCODE){throw "HTTP request failed (curl exit $LASTEXITCODE)."}
    $response -join "`n"
}

try {
    $password=[guid]::NewGuid().ToString('N')
    dotnet dev-certs https --export-path $pfx --password $password | Out-Null
    if($LASTEXITCODE){throw 'Export failed. Prepare a trusted HTTPS certificate with dotnet dev-certs https --trust.'}
    Set-TestVariable 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-TestVariable 'ASPNETCORE_FORWARDEDHEADERS_ENABLED' 'false'
    Set-TestVariable 'ConnectionStrings__TeamManager' 'Server=localhost\SQLEXPRESS;Database=TeamManager_IisTest;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
    Set-TestVariable 'ReverseProxy__TrustedNetwork' '127.0.0.1/32'
    Set-TestVariable 'ReverseProxy__HttpsPort' "$frontend"
    Set-TestVariable 'Smtp__Enabled' 'false'
    Set-TestVariable 'MailIntake__Imap__1__Enabled' 'false'
    Set-TestVariable 'MailIntake__Imap__3__Enabled' 'false'
    Set-TestVariable 'Bootstrap__RequirePassword' 'true'
    $app=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('"'+$dll+'"','--urls',"http://localhost:$backend") -WorkingDirectory (Split-Path $dll -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts 'local-proxy-app.log')
    Set-TestVariable 'TEAM_MANAGER_PROXY_PFX_PASSWORD' $password
    $proxy=Start-Process -FilePath (Get-Command node).Source -ArgumentList @('"'+(Join-Path $PSScriptRoot 'local-https-proxy.cjs')+'"','"'+$pfx+'"',$frontend,$backend) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $artifacts 'local-proxy-error.log')
    for($attempt=0;$attempt -lt 20;$attempt++){
        if($proxy.HasExited -or $app.HasExited){throw 'The backend or proxy exited before becoming ready.'}
        $ready=& curl.exe --silent --fail --max-time 2 "$url/health/ready"
        if($LASTEXITCODE -eq 0 -and $ready -eq 'Healthy'){break}
        Start-Sleep -Seconds 1
    }
    if($ready -ne 'Healthy'){throw 'The proxied application did not become ready.'}
    if((Request @("$url/health/live")) -ne 'Healthy'){throw 'Proxied liveness failed.'}
    $redirect=Request @('--include','--header','X-Forwarded-Proto: http',"$url/Projects")
    if($redirect -notmatch "(?im)^location: https://localhost:${frontend}/Login\?"){throw 'Incorrect scheme on proxied authentication redirect.'}
    $direct=Request @('--include','--resolve',"localhost:${backend}:[::1]",'--header','X-Forwarded-Proto: https',"http://localhost:$backend/Projects")
    if($direct -notmatch "(?im)^location: https://localhost:${frontend}/Projects\s*$"){throw 'The backend accepted a forwarded scheme from an untrusted peer.'}
    Write-Host 'PASS: Proxied health, HTTPS authentication redirect and rejection of an untrusted forwarded scheme.'
    $login=Request @('--cookie-jar',$cookies,"$url/Login")
    $token=[regex]::Match($login,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if(!$token.Success){throw 'Missing login antiforgery token.'}
    $value=[Net.WebUtility]::HtmlDecode($token.Groups[1].Value)
    $signedIn=Request @('--include','--cookie',$cookies,'--cookie-jar',$cookies,
        '--data-urlencode','EmployeeNumber=admin','--data-urlencode','Password=admin',
        '--data-urlencode',"__RequestVerificationToken=$value","$url/Login")
    if($signedIn -notmatch '(?im)^set-cookie: TeamManager\.Session=.*; secure(?:;|\s*$)'){throw 'Missing secure session cookie.'}
    $project=Request @('--cookie',$cookies,"$url/Projects")
    if(!$project.Contains('IIS rollback test project')){throw 'The signed-in user cannot see the test project.'}
    Write-Host 'PASS: Login through the proxy sets a Secure cookie and displays the test project.'
    if($Preview){
        $keepRunning=$true
        Write-Host "READY: $url (backend PID $($app.Id), proxy PID $($proxy.Id))"
    }
} finally {
    if(!$keepRunning){
        foreach($process in @($proxy,$app)){
            if($process -and !$process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit()}
        }
    }
    foreach($name in $variables.Keys){[Environment]::SetEnvironmentVariable($name,$variables[$name],'Process')}
    foreach($file in $pfx,$cookies){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file}}
    Remove-Item -LiteralPath $temp
}

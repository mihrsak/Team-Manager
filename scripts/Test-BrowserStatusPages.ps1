#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root ('artifacts/status-pages-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$source=[System.Security.SecurityElement]::Escape((Join-Path $root 'src/TeamManager.Web/BrowserStatusPages.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><Compile Include="$source" Link="BrowserStatusPages.cs" /></ItemGroup></Project>
"@ | Set-Content (Join-Path $directory 'StatusPagesTest.csproj')
@'
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TeamManager.Web;

var services=new ServiceCollection().AddLogging().BuildServiceProvider();
foreach(var status in new[]{200,204,400,401,403,404,405,409,429,500,503})
foreach(var originalBody in new[]{"","Existing error body"})
{
    var calls=0;
    var app=new ApplicationBuilder(services);
    app.UseBrowserNotFoundPage();
    app.Run(async context=>
    {
        calls++;
        if(context.Request.Path=="/NotFound")
        {
            context.Response.StatusCode=404;
            context.Response.ContentType="text/html; charset=utf-8";
            await context.Response.WriteAsync("Generic missing page");
            return;
        }
        context.Response.StatusCode=status;
        context.Response.Headers.RetryAfter="17";
        context.Response.Headers["X-Preserved"]="original";
        if(originalBody.Length>0)
        {
            context.Response.ContentType="text/plain; charset=utf-8";
            await context.Response.WriteAsync(originalBody);
        }
    });
    var ctx=new DefaultHttpContext{RequestServices=services};
    ctx.Request.Method="GET";ctx.Request.Path="/original";
    ctx.Response.Body=new MemoryStream();
    await app.Build()(ctx);
    ctx.Response.Body.Position=0;
    var body=await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    var render=status==404 && originalBody.Length==0;
    if(ctx.Response.StatusCode!=status || calls!=(render?2:1) || body!=(render?"Generic missing page":originalBody) ||
        ctx.Response.Headers.RetryAfter!="17" || ctx.Response.Headers["X-Preserved"]!="original" || ctx.Request.Path!="/original")
        throw new Exception($"Status contract failed: {status}, body={originalBody.Length}, calls={calls}, response={ctx.Response.StatusCode}");
}
Console.WriteLine("PASS: only empty 404 renders the missing page; 200/204/400/401/403/405/409/429/500/503, existing bodies, original path and Retry-After headers are preserved.");
'@ | Set-Content (Join-Path $directory 'Program.cs')
dotnet run --project (Join-Path $directory 'StatusPagesTest.csproj') --configuration Release
if($LASTEXITCODE){throw 'Browser status middleware contract failed.'}

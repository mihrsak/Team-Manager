using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using TeamManager.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddScoped<TeamStore>();
builder.Services.AddScoped<IssueStore>();
builder.Services.AddScoped<ProjectStore>();
builder.Services.AddScoped<WorkflowStore>();
builder.Services.AddScoped<UserStore>();
builder.Services.AddScoped<Permissions>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/Denied";
    options.Cookie.Name = "TeamManager.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnValidatePrincipal = async context => {
        var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if(!int.TryParse(id,out var userId)){context.RejectPrincipal();return;}
        var rights = await context.HttpContext.RequestServices.GetRequiredService<Permissions>().Effective(userId);
        var identity = (System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!;
        foreach(var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToList())identity.RemoveClaim(claim);
        foreach(var right in rights.Contains("*") ? Permissions.Catalog.Keys.ToList() : rights)
            identity.AddClaim(new(System.Security.Claims.ClaimTypes.Role,right));
    };
});
builder.Services.AddAuthorization();

if (args.Contains("--bootstrap-local-user"))
{
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Local bootstrap requires Development.");
    using var bootstrapApp = builder.Build();
    using var scope = bootstrapApp.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<UserStore>().Bootstrap();
    Console.WriteLine("Local account prepared. Existing passwords are preserved.");
    return;
}

// Database installation is explicit; web startup never migrates or seeds demo data.
if (args.Contains("--apply-schema") || args.Contains("--seed-demo"))
{
    using var installer = builder.Build();
    using var scope = installer.Services.CreateScope();
    var connection = builder.Configuration.GetConnectionString("TeamManager");
    if(string.IsNullOrWhiteSpace(connection))throw new InvalidOperationException("Configure ConnectionStrings:TeamManager first.");
    if(args.Contains("--apply-schema"))await DatabaseInstaller.Run(connection,args.Contains("--create-database"),false);
    await scope.ServiceProvider.GetRequiredService<UserStore>().Bootstrap();
    if(args.Contains("--seed-demo"))await DatabaseInstaller.Run(connection,false,true);
    return;
}
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<UserStore>().Bootstrap();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.Run();

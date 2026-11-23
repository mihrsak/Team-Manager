using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using TeamManager.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
if (!databaseProvider.Equals("Oracle", StringComparison.OrdinalIgnoreCase) &&
    !databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) && !databaseProvider.Equals("Db2", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Database:Provider must be SqlServer, Oracle or Db2.");
var oracleMode = databaseProvider.Equals("Oracle", StringComparison.OrdinalIgnoreCase) || databaseProvider.Equals("Db2", StringComparison.OrdinalIgnoreCase);
var proxy = builder.Configuration.GetSection("ReverseProxy");
var trustedProxyNetwork = proxy["TrustedNetwork"];
if (!string.IsNullOrWhiteSpace(trustedProxyNetwork))
{
    var network = System.Net.IPNetwork.Parse(trustedProxyNetwork);
    if (network.PrefixLength == 0)
        throw new InvalidOperationException("Configure a trusted ingress network with a nonzero prefix length.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownIPNetworks.Add(network);
    });
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = proxy.GetValue("HttpsPort", 443));
}
if (builder.Configuration["DataProtection:KeyDirectory"] is { Length: > 0 } keyDirectory)
    builder.Services.AddDataProtection().SetApplicationName("TeamManager")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddScoped<LocalizedValidationFilter>();
builder.Services.AddScoped<ReturnNavigationFilter>();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton<IProjectNavigationExtension, ConfiguredProjectLinks>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Registration", context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5)
        })
        : RateLimitPartition.GetNoLimiter("read"));
    options.AddPolicy("LdapLogin", context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5)
        })
        : RateLimitPartition.GetNoLimiter("read"));
});
builder.Services.AddRazorPages(options => {
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(LocalizedValidationFilter)));
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(ReturnNavigationFilter)));
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/LdapLogin");
    options.Conventions.AllowAnonymousToPage("/Register");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/NotFound");
}).AddDataAnnotationsLocalization(options=>options.DataAnnotationLocalizerProvider=(_,factory)=>factory.Create(typeof(UiText)));
builder.Services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>,LocalizedValidationOptions>();
builder.Services.AddScoped<TeamStore>();
builder.Services.AddScoped<IssueStore>();
builder.Services.AddScoped<ProjectContentSearchStore>();
builder.Services.AddScoped<IssueLinkStore>();
builder.Services.AddScoped<IssuePersonalStore>();
builder.Services.AddScoped<EmailNotificationStore>();
builder.Services.AddScoped<EmailDispatchStore>();
builder.Services.AddHostedService<SmtpNotificationWorker>();
builder.Services.AddScoped<IssueAttachmentStore>();
builder.Services.AddScoped<IssueFieldStore>();
builder.Services.AddScoped<CustomFieldStore>();
builder.Services.AddScoped<ProjectDocumentStore>();
builder.Services.AddScoped<ProjectForumStore>();
builder.Services.AddScoped<SavedIssueQueryStore>();
builder.Services.AddScoped<ProjectStore>();
builder.Services.AddScoped<ProjectRepositoryStore>();
builder.Services.AddScoped<RepositoryHistory>();
builder.Services.AddScoped<RoadmapStore>();
builder.Services.AddScoped<ProjectNewsStore>();
builder.Services.AddScoped<ProjectWikiStore>();
builder.Services.AddScoped<WorkflowStore>();
builder.Services.AddScoped<UserStore>();
builder.Services.AddSingleton<LdapDirectory>();
builder.Services.AddScoped<ApiTokenStore>();
builder.Services.AddScoped<RegistrationStore>();
builder.Services.AddSingleton<ImapImportMonitor>();
builder.Services.AddHostedService<ImapIssueImporter>();
builder.Services.AddScoped<Permissions>();
builder.Services.AddScoped<AdministrationAuditStore>();
builder.Services.AddScoped<GroupStore>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/Denied";
    options.Cookie.Name = "TeamManager.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnValidatePrincipal = async context => {
        if (!context.HttpContext.RequestServices.GetRequiredService<LdapDirectory>().AcceptsSession(context.Principal))
        {
            context.RejectPrincipal();
            return;
        }
        var issuer = context.Principal?.FindFirstValue(SsoLogin.IssuerClaim);
        if (issuer is not null && !string.Equals(
            SsoLogin.LinkedEmployee(builder.Configuration, issuer, context.Principal?.FindFirstValue(SsoLogin.SubjectClaim)),
            context.Principal?.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            context.RejectPrincipal();
            return;
        }
        var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if(!int.TryParse(id,out var userId)){context.RejectPrincipal();return;}
        var version = context.Principal?.FindFirst("session_version")?.Value;
        var store = context.HttpContext.RequestServices.GetRequiredService<UserStore>();
        var settings = int.TryParse(version, out var sessionVersion) ? await store.SessionSettings(userId, sessionVersion) : null;
        if (settings is null)
        {
            context.RejectPrincipal();
            return;
        }
        var rights = await context.HttpContext.RequestServices.GetRequiredService<Permissions>().Effective(userId);
        var identity = (System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!;
        foreach (var claim in identity.FindAll(UserStore.ThemeClaim).ToList()) identity.RemoveClaim(claim);
        identity.AddClaim(new(UserStore.ThemeClaim, settings.Theme));
        foreach (var claim in identity.FindAll(UserStore.LanguageClaim).ToList()) identity.RemoveClaim(claim);
        identity.AddClaim(new(UserStore.LanguageClaim, settings.Language));
        foreach(var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToList())identity.RemoveClaim(claim);
        foreach(var right in rights.Contains("*") ? Permissions.Catalog.Keys.ToList() : rights)
            identity.AddClaim(new(System.Security.Claims.ClaimTypes.Role,right));
    };
});
SsoLogin.AddProvider(authentication, builder.Configuration);
builder.Services.AddAuthorization();
var healthChecks = builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddScoped<OracleOverviewStore>();
builder.Services.AddScoped<OracleProjectImport>();
builder.Services.AddSingleton<OracleWorkspace>();
if (!oracleMode && OracleOverviewStore.Enabled(builder.Configuration))
    healthChecks.AddCheck<OracleOverviewStore>("oracle-overview", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IHealthCheckPublisher, DatabaseHealthPublisher>();

if(Array.IndexOf(args,"--export-sql-relational") is int sqlExportIndex&&sqlExportIndex>=0)
{
    if(sqlExportIndex+1>=args.Length)throw new ArgumentException("Supply the snapshot output file.");
    await new OracleWorkspace(builder.Configuration).ExportSqlRelational(args[sqlExportIndex+1]);return;
}
if(Array.IndexOf(args,"--create-local-db2-demo-schema") is int demoSchemaIndex&&demoSchemaIndex>=0)
{
    if(demoSchemaIndex+1>=args.Length)throw new ArgumentException("Supply a generated demo schema name.");
    await new OracleWorkspace(builder.Configuration).CreateLocalDb2DemoSchema(args[demoSchemaIndex+1]);return;
}
if (Array.IndexOf(args, "--export-relational") is int exportIndex && exportIndex >= 0)
{
    await new OracleWorkspace(builder.Configuration).ExportRelational(args[exportIndex + 1]); return;
}
if (Array.IndexOf(args, "--import-db2-relational") is int importIndex && importIndex >= 0)
{
    await new OracleWorkspace(builder.Configuration).ImportDb2(args[importIndex + 1]); return;
}
if (args.Contains("--apply-oracle-concurrency") || args.Contains("--apply-relational-concurrency"))
{
    await new OracleWorkspace(builder.Configuration).InstallConcurrency();
    return;
}
if (args.Contains("--migrate-oracle-workspace"))
{
    await new OracleWorkspace(builder.Configuration).MigrateRelational();
    return;
}
if (args.Contains("--import-oracle-workspace"))
{
    await new OracleWorkspace(builder.Configuration).Import();
    Console.WriteLine("Oracle active workspace imported.");
    return;
}
if (args.Contains("--import-oracle-projects"))
{
    var result = await new OracleProjectImport(builder.Configuration).Import();
    Console.WriteLine($"Oracle: imported {result.Projects} projects and {result.Issues} issues.");
    return;
}

if (args.Contains("--apply-oracle-overview") || args.Contains("--seed-oracle-demo"))
{
    var oracle = new OracleOverviewStore(builder.Configuration);
    await oracle.Install(args.Contains("--apply-oracle-overview"), args.Contains("--seed-oracle-demo"));
    Console.WriteLine("Oracle overview installation complete.");
    return;
}

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
if (!string.IsNullOrWhiteSpace(trustedProxyNetwork))
    app.UseForwardedHeaders();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<UserStore>().Bootstrap();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
if (string.IsNullOrWhiteSpace(trustedProxyNetwork))
    app.UseHttpsRedirection();
else
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
app.UseStaticFiles();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var previous = System.Globalization.CultureInfo.CurrentUICulture;
    var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
    var language = context.User.FindFirst(UserStore.LanguageClaim)?.Value;
    if (language is null)
    {
        context.Response.Headers.Append("Vary", "Accept-Language");
        language = context.Request.GetTypedHeaders().AcceptLanguage
            .Where(candidate => candidate.Quality is null or > 0)
            .OrderByDescending(candidate => candidate.Quality ?? 1)
            .Select(candidate => candidate.Value.ToString().Split('-', 2)[0].ToLowerInvariant())
            .FirstOrDefault(candidate => candidate is "en" or "hr") ?? "hr";
    }
    try
    {
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(builder.Configuration["Formatting:Culture"] ?? (language == "en" ? "en-GB" : "hr-HR"));
        await next(context);
    }
    finally { System.Globalization.CultureInfo.CurrentUICulture = previous; System.Globalization.CultureInfo.CurrentCulture=previousCulture; }
});
// Render browser 404s without changing the status or exposing the requested record.
// API clients retain their existing response contract.
app.UseWhen(context => (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
    !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseStatusCodePagesWithReExecute("/NotFound"));
// Keep routing inside the re-executable pipeline so the error page replaces the original endpoint.
app.UseRouting();
if (oracleMode)
{
    app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (OracleWriteBusyException error) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            context.Response.ContentType = "text/plain; charset=utf-8";
            var text = context.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<UiText>>();
            await context.Response.WriteAsync(text[error.Message]);
        }
        catch (UnauthorizedAccessException) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            var text = context.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<UiText>>();
            await context.Response.WriteAsync(text["Nemaš ovlasti za ovu radnju."]);
        }
    });
    app.Use(OracleWorkspaceRoutes.Guard);
}
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages();
app.MapTeamManagerApi();
app.MapIncomingMail();
app.Run();

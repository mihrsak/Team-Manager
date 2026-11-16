using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using TeamManager.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
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
});
builder.Services.AddRazorPages(options => {
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(LocalizedValidationFilter)));
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Register");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddScoped<TeamStore>();
builder.Services.AddScoped<IssueStore>();
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
builder.Services.AddScoped<ApiTokenStore>();
builder.Services.AddScoped<RegistrationStore>();
builder.Services.AddSingleton<ImapImportMonitor>();
builder.Services.AddHostedService<ImapIssueImporter>();
builder.Services.AddScoped<Permissions>();
builder.Services.AddScoped<AdministrationAuditStore>();
builder.Services.AddScoped<GroupStore>();
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
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddSingleton<IHealthCheckPublisher, DatabaseHealthPublisher>();

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
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var previous = System.Globalization.CultureInfo.CurrentUICulture;
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
        await next(context);
    }
    finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
});
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages();
app.MapTeamManagerApi();
app.MapIncomingMail();
app.Run();

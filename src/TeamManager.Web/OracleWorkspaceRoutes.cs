using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

internal static class OracleWorkspaceRoutes
{
    private static readonly HashSet<string> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/Index",
        "/Login",
        "/LdapLogin",
        "/Logout",
        "/InitialPassword",
        "/Denied",
        "/Error",
        "/Profile",
        "/Users",
        "/Roles",
        "/Projects",
        "/Project",
        "/Issues",
        "/Issue",
        "/Dashboard",
        "/Calendar",
        "/Gantt",
        "/Activity",
        "/TimeReport",
        "/Search",
        "/Roadmap",
        "/ProjectNews",
        "/ProjectDocuments",
        "/Wiki",
        "/Forum",
        "/Repositories",
        "/RepositoryHistory",
        "/MailIntake",
        "/ApiTokens",
        "/Register",
        "/RegistrationRequests",
        "/Groups",
        "/IssueFields",
        "/CustomFields",
        "/Notifications",
        "/Workflow",
        "/AdministrationAudit",
        "/health/live",
        "/health/ready",
        "/signin-sso"
    };
    internal static async Task Guard(HttpContext context, RequestDelegate next)
    {
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if ((HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)) || Pages.Contains(context.Request.Path.Value?.TrimEnd('/') ?? "") || context.Request.Path == "/" ||
            (HttpMethods.IsPost(context.Request.Method) && segments is ["mail", "intake", var id] && int.TryParse(id, out var project) && project > 0))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/plain; charset=utf-8";
        var text = context.RequestServices.GetRequiredService<IStringLocalizer<UiText>>();
        await context.Response.WriteAsync(text["Ovaj modul još nije dostupan u Oracle načinu rada."].Value);
    }
}

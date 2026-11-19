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
        if (Pages.Contains(context.Request.Path.Value?.TrimEnd('/') ?? "") || context.Request.Path == "/")
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

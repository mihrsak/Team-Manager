using System.Net.Http.Headers;

namespace TeamManager.Web;

public static class TeamManagerApi
{
    private static readonly object AccountKey = new();

    public static void MapTeamManagerApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter(Authenticate);

        api.MapGet("/projects", async (HttpContext context, ProjectStore store) =>
            (await store.List(UserId(context))).Select(project => new
            {
                project.Id, project.Input.Name, project.Input.Description, project.Input.State
            }));

        api.MapGet("/projects/{projectId:int}/issues", async (
            int projectId, int? beforeId, HttpContext context, IssueStore store) =>
        {
            var user = UserId(context);
            if (!await store.Can(user,projectId,"issues.view")) return Results.NotFound();
            var issues = await store.List(user,projectId,null,null);
            return Results.Ok(issues.Where(issue => beforeId is null || issue.Id < beforeId).Take(100).Select(issue => new
            {
                issue.Id, issue.Title, issue.Status, issue.Priority, issue.DueDate
            }));
        });

        api.MapGet("/issues/{id:int}", async (int id, HttpContext context, IssueStore store) =>
        {
            var issue = await store.Get(UserId(context),id);
            if (issue is null) return Results.NotFound();
            return Results.Ok(new
            {
                issue.Id, issue.ProjectId, issue.Input.Title, issue.Input.Description,
                issue.Input.Type, issue.Input.Status, issue.Input.Priority, issue.Input.AssigneeId,
                issue.Input.StartDate, issue.Input.DueDate, issue.Input.IsPrivate
            });
        });
    }

    private static int UserId(HttpContext context) => (int)context.Items[AccountKey]!;

    private static async ValueTask<object?> Authenticate(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        context.Response.Headers.CacheControl = "private, no-store";
        if (AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) &&
            header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) && header.Parameter is { } token)
        {
            var store = context.RequestServices.GetRequiredService<ApiTokenStore>();
            if (await store.Authenticate(token, context.RequestAborted) is int user)
            {
                context.Items[AccountKey] = user;
                return await next(invocation);
            }
        }
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Unauthorized();
    }
}

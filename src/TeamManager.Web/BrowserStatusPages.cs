using Microsoft.AspNetCore.Diagnostics;

namespace TeamManager.Web;

public static class BrowserStatusPages
{
    public static IApplicationBuilder UseBrowserNotFoundPage(this IApplicationBuilder app)
    {
        app.UseStatusCodePagesWithReExecute("/NotFound");
        return app.Use(async (context, next) =>
        {
            await next(context);
            // StatusCodePages runs after this middleware unwinds. Only a 404 may
            // re-execute the page that deliberately returns 404; preserve every
            // other status, body and headers (including Retry-After).
            if (context.Response.StatusCode != StatusCodes.Status404NotFound &&
                context.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
                statusPages.Enabled = false;
        });
    }
}

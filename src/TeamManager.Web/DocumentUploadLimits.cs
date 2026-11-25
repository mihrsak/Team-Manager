using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

public static class DocumentUploadLimits
{
    public static IApplicationBuilder UseDocumentUploadLimits(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        // Reject before antiforgery reads the multipart body. Do not raise the
        // server limit or read an oversized upload merely to render an error.
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
        if (context.User.Identity?.IsAuthenticated == true && HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals("/ProjectDocuments", StringComparison.OrdinalIgnoreCase) &&
            limit is long maximum && context.Request.ContentLength is long length && length > maximum)
        {
            var text = context.RequestServices.GetRequiredService<IStringLocalizer<UiText>>();
            string Encode(string key) => HtmlEncoder.Default.Encode(text[key].Value);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await context.Response.WriteAsync($"<!doctype html><html lang=\"{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{Encode("Zahtjev je prevelik.")} · Team Manager</title></head><body><main><h1>{Encode("Zahtjev je prevelik.")}</h1><p>{Encode("Vrati se u pregledniku i odaberi datoteku do 10 MiB.")}</p><a href=\"/Projects\">{Encode("Projekti")}</a></main></body></html>");
            return;
        }
        await next(context);
    });
}

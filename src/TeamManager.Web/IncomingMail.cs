using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace TeamManager.Web;

public sealed record InboundIssueMail(string? MessageId, string? Subject, string? Text);

public static class IncomingMail
{
    private const int MaxRequestBytes = 64 * 1024;

    public static string? ConfiguredAccount(IConfiguration configuration, int project)
    {
        var route = configuration.GetSection($"MailIntake:Routes:{project}");
        var employee = route["EmployeeNumber"];
        return !string.IsNullOrWhiteSpace(employee) && IsSecret(route["Secret"]) ? employee : null;
    }

    public static void MapIncomingMail(this WebApplication app)
        => app.MapPost("/mail/intake/{projectId:int}", Receive);

    private static async Task<IResult> Receive(int projectId, HttpContext context, IConfiguration configuration,
        UserStore users, IssueStore issues)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var employee = ConfiguredAccount(configuration,projectId);
        if (employee is null) return Results.NotFound();
        var secret = configuration[$"MailIntake:Routes:{projectId}:Secret"]!;
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization,out var header) ||
            !header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase) ||
            !IsSecret(header.Parameter) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(secret),Convert.FromHexString(header.Parameter!)))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Unauthorized();
        }

        var actor = await users.Find(employee);
        if (actor is not { IsActive: true }) return Results.Forbid();
        if (context.Request.ContentLength > MaxRequestBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
            bodyLimit.MaxRequestBodySize = MaxRequestBytes;
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        InboundIssueMail? message;
        try { message = await context.Request.ReadFromJsonAsync<InboundIssueMail>(context.RequestAborted); }
        catch (JsonException) { return Results.BadRequest(); }
        catch (BadHttpRequestException ex) when (ex.StatusCode==StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(ex.StatusCode);
        }

        var messageId = message?.MessageId?.Trim();
        var subject = message?.Subject?.Trim();
        var body = message?.Text?.Trim() ?? "";
        if (messageId is null or { Length: 0 or > 512 } || messageId.Any(char.IsControl) ||
            subject is null or { Length: 0 or > 200 } || subject.Any(char.IsControl) || body.Length > 4000)
            return Results.BadRequest();

        try
        {
            var issueId = await issues.ImportMail(actor.Id,projectId,SHA256.HashData(Encoding.UTF8.GetBytes(messageId)),
                new IssueInput { Title=subject,Description=body,IsPrivate=true });
            if (issueId is null) return Results.Ok(new { duplicate=true });
            return Results.Created($"/Issue?id={issueId}",new { issueId });
        }
        catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
    }

    private static bool IsSecret(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

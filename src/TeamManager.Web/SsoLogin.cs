using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TeamManager.Web;

internal static class SsoLogin
{
    internal const string Scheme = "TeamManager.Sso";
    internal const string IssuerClaim = "team_manager_sso_issuer";
    internal const string SubjectClaim = "team_manager_sso_subject";

    internal static bool Enabled(IConfiguration configuration) => configuration.GetValue<bool>("Sso:Enabled");

    internal static string? LinkedEmployee(IConfiguration configuration, string? issuer, string? subject)
    {
        if (!Enabled(configuration) || string.IsNullOrWhiteSpace(subject) ||
            !string.Equals(configuration["Sso:Authority"], issuer, StringComparison.Ordinal))
            return null;

        var employees = configuration.GetSection("Sso:Accounts").GetChildren()
            .Where(account => string.Equals(account["Subject"], subject, StringComparison.Ordinal))
            .Select(account => account["EmployeeNumber"])
            .ToArray();
        return employees.Length == 1 && !string.IsNullOrWhiteSpace(employees[0])
            ? employees[0]!.Trim().ToLowerInvariant() : null;
    }

    internal static ClaimsPrincipal LocalPrincipal(AppUser user, string? issuer = null, string? subject = null)
    {
        var identity = new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.EmployeeNumber),
            new("session_version", user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        if (user.IsAdministrator) identity.AddClaim(new(ClaimTypes.Role, "Administrator"));
        if (issuer is not null && subject is not null)
        {
            identity.AddClaim(new(IssuerClaim, issuer));
            identity.AddClaim(new(SubjectClaim, subject));
        }
        return new(identity);
    }

    internal static void AddProvider(AuthenticationBuilder authentication, IConfiguration configuration)
    {
        if (!Enabled(configuration)) return;
        var authority = configuration["Sso:Authority"];
        var clientId = configuration["Sso:ClientId"];
        var clientSecret = configuration["Sso:ClientSecret"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("SSO requires an HTTPS issuer, client ID and client secret.");

        authentication.AddOpenIdConnect(Scheme, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.Authority = authority;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.CallbackPath = "/signin-sso";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Events.OnTokenValidated = async context =>
            {
                var issuer = context.SecurityToken.Issuer;
                var subject = context.Principal?.FindFirstValue("sub");
                var currentConfiguration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var employee = LinkedEmployee(currentConfiguration, issuer, subject);
                var user = employee is null ? null :
                    await context.HttpContext.RequestServices.GetRequiredService<UserStore>().Find(employee);
                if (user is null || !user.IsActive)
                {
                    context.Fail("SSO identity is not linked to an active account.");
                    return;
                }
                context.Principal = LocalPrincipal(user, issuer, subject);
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(context.Request.PathBase + "/Login?ssoError=true");
                return Task.CompletedTask;
            };
        });
    }
}

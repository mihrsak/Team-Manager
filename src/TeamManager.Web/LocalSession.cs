using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Globalization;

namespace TeamManager.Web;

internal static class LocalSession
{
    internal static ClaimsPrincipal Create(AppUser user, params Claim[] sourceClaims)
    {
        var identity = new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.EmployeeNumber),
            new("session_version", user.SessionVersion.ToString(CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        if (user.IsAdministrator) identity.AddClaim(new(ClaimTypes.Role, "Administrator"));
        identity.AddClaims(sourceClaims);
        return new(identity);
    }
}

using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TeamManager.Web;

public sealed class LdapDirectory(IConfiguration configuration, ILogger<LdapDirectory> logger)
{
    private const string EndpointClaim = "team_manager_ldap_endpoint";
    private const string EntryClaim = "team_manager_ldap_entry";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public bool Enabled => IsEnabled(configuration);
    internal static bool IsEnabled(IConfiguration config) => config.GetValue<bool>("Ldap:Enabled");
    private string Host => configuration["Ldap:Host"]?.Trim() ?? "";
    private int Port => configuration.GetValue("Ldap:Port", 636);
    private string Endpoint => $"ldaps://{Host.ToLowerInvariant()}:{Port}";

    private string? LinkedEntry(string? employee)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(employee)) return null;
        var entries = configuration.GetSection("Ldap:Accounts").GetChildren()
            .Where(account => string.Equals(account["EmployeeNumber"]?.Trim(), employee, StringComparison.OrdinalIgnoreCase))
            .Select(account => account["DistinguishedName"])
            .ToArray();
        return entries.Length == 1 && !string.IsNullOrWhiteSpace(entries[0]) ? entries[0]!.Trim() : null;
    }

    internal bool AcceptsSession(ClaimsPrincipal? principal)
    {
        var endpoint = principal?.FindFirstValue(EndpointClaim);
        if (endpoint is null) return true;
        var entry = LinkedEntry(principal?.Identity?.Name);
        return entry is not null && string.Equals(endpoint, Endpoint, StringComparison.Ordinal) &&
            string.Equals(entry, principal?.FindFirstValue(EntryClaim), StringComparison.Ordinal);
    }

    internal ClaimsPrincipal? Authenticate(AppUser? user, string password)
    {
        if (user is null || !user.IsActive || string.IsNullOrEmpty(password)) return null;
        var entry = LinkedEntry(user.EmployeeNumber);
        if (entry is null) return null;

        if (Uri.CheckHostName(Host) == UriHostNameType.Unknown || Port is < 1 or > 65535)
        {
            logger.LogWarning("LDAP sign-in requires a valid LDAPS host and port.");
            return null;
        }

        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(Host, Port),
                new NetworkCredential(entry, password), AuthType.Basic) { Timeout = Timeout };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = true;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            if (OperatingSystem.IsWindows())
                connection.SessionOptions.VerifyServerCertificate = (_, certificate) => VerifyCertificate(certificate, Host);
            connection.Bind();
            return LocalSession.Create(user, new(EndpointClaim, Endpoint), new(EntryClaim, entry));
        }
        catch (LdapException error)
        {
            if (error.ErrorCode != 49)
                logger.LogWarning("LDAP bind failed with directory error code {ErrorCode}.", error.ErrorCode);
            return null;
        }
    }

    private static bool VerifyCertificate(X509Certificate certificate, string host)
    {
        using var presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        chain.ChainPolicy.UrlRetrievalTimeout = Timeout;
        return presented.MatchesHostname(host, allowCommonName: false) && chain.Build(presented);
    }
}

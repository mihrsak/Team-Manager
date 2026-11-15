using System.Globalization;

namespace TeamManager.Web;

public sealed record ProjectExtensionLink(string Label, Uri Url);

public interface IProjectNavigationExtension
{
    IEnumerable<ProjectExtensionLink> Links(int projectId);
}

public sealed class ConfiguredProjectLinks(IConfiguration configuration) : IProjectNavigationExtension
{
    public IEnumerable<ProjectExtensionLink> Links(int projectId)
    {
        foreach (var entry in configuration.GetSection("ProjectExtensions:Links").GetChildren())
        {
            if (entry["ProjectId"] is string scope && (!int.TryParse(scope, out var id) || id != projectId))
                continue;

            var label = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                ? entry["EnglishLabel"] ?? entry["Label"] : entry["Label"];
            if (string.IsNullOrWhiteSpace(label) ||
                !Uri.TryCreate(entry["Url"], UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                continue;

            yield return new(label, uri);
        }
    }
}

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class ActivityModel(IssueStore issues) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    public List<Team> Projects { get; private set; } = [];
    public List<ActivityRow> Entries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await Load() ?? Page();

    public async Task<IActionResult> OnGetAtomAsync()
    {
        var error = await Load();
        if (error is not null) return error;

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var feed = new XElement(atom + "feed",
            new XElement(atom + "id", PageUrl("/Activity", new { ProjectId })),
            new XElement(atom + "title", "Team Manager · Aktivnosti"),
            new XElement(atom + "author", new XElement(atom + "name", "Team Manager")),
            new XElement(atom + "link", new XAttribute("rel", "self"),
                new XAttribute("href", PageUrl("/Activity", new { handler = "Atom", ProjectId }))),
            new XElement(atom + "updated", Entries.Count == 0 ? DateTime.UnixEpoch : Entries[0].At),
            Entries.Select(entry => new XElement(atom + "entry",
                new XElement(atom + "id", EntryUrl(entry)),
                new XElement(atom + "title", XmlText(EntryTitle(entry))),
                new XElement(atom + "updated", entry.At),
                new XElement(atom + "link", new XAttribute("href", PageUrl("/Issue", new { id = entry.IssueId }))))));
        return Content(feed.ToString(), "application/atom+xml", Encoding.UTF8);
    }

    public async Task<IActionResult> OnGetRssAsync()
    {
        var error = await Load();
        if (error is not null) return error;

        var feed = new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel",
                new XElement("title", "Team Manager · Aktivnosti"),
                new XElement("link", PageUrl("/Activity", new { ProjectId })),
                new XElement("description", "Nedavne aktivnosti na dostupnim zadacima"),
                Entries.Select(entry => new XElement("item",
                    new XElement("title", XmlText(EntryTitle(entry))),
                    new XElement("guid", new XAttribute("isPermaLink", "false"), EntryUrl(entry)),
                    new XElement("link", PageUrl("/Issue", new { id = entry.IssueId })),
                    new XElement("pubDate", entry.At.ToString("R", CultureInfo.InvariantCulture))))));
        return Content(feed.ToString(), "application/rss+xml", Encoding.UTF8);
    }

    private async Task<IActionResult?> Load()
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!ModelState.IsValid) return BadRequest();
        var user = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Projects = await issues.Projects(user);
        if (ProjectId is int project && !Projects.Any(item => item.Id == project)) return NotFound();
        var allowed = ProjectId is int selected ? [selected] : Projects.Select(item => item.Id).ToArray();
        if (allowed.Length > 0)
            Entries = await issues.RecentActivity(allowed);
        return null;
    }

    private string PageUrl(string page, object route) => Url.Page(page, pageHandler: null, values: route, protocol: Request.Scheme)!;
    private string EntryUrl(ActivityRow entry) => PageUrl("/Issue", new { id = entry.IssueId }) + $"#activity-{entry.Id}";
    private static string EntryTitle(ActivityRow entry) => $"{KindLabel(entry.Kind)} · {entry.Title}";

    private static string XmlText(string value) => string.Concat(value.EnumerateRunes()
        .Where(rune => rune.Value is 9 or 10 or 13 or (>= 32 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or >= 0x10000));

    public static string KindLabel(string kind) => kind switch
    {
        "created" => "Novi zadatak",
        "updated" => "Izmjena zadatka",
        "moved" => "Premještanje zadatka",
        "comment" => "Komentar",
        "time" => "Unos vremena",
        _ => "Aktivnost zadatka"
    };
}

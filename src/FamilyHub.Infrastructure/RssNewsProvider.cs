using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyHub.Infrastructure;

/// <param name="Lean">How the outlet is commonly characterised. Contested and shown with a caveat —
/// it exists so the page can guarantee a spread of perspectives, not to rate anyone as correct.</param>
public sealed record NewsSource(string Name, string FeedUrl, string Lean, string Color);

/// <param name="Summary">The outlet's own feed summary, trimmed. Never the full article.</param>
public sealed record NewsStory(string Title, string Summary, string Link, string SourceName, string SourceLean,
    string SourceColor, DateTimeOffset? Published)
{
    public string PublishedText => Published is { } when
        ? when.LocalDateTime.ToString("ddd h:mm tt", System.Globalization.CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>Shown under every story, per the requirement that each item carry a disclaimer.</summary>
    public string Disclaimer =>
        $"Source: {SourceName} ({SourceLean}). Headline and summary are {SourceName}'s own words, shown unedited; "
        + "FamilyHub does not verify or endorse them. Open the link to read the full article.";
}

/// <summary>
/// Reads public RSS/Atom feeds. Only headlines, feed summaries, and links are stored — full article
/// text is deliberately never fetched or displayed, so the dashboard stays within fair use and
/// always sends the reader back to the publisher.
/// </summary>
public sealed class RssNewsProvider(HttpClient httpClient)
{
    /// <summary>
    /// A deliberately mixed roster: outlets commonly described as left-leaning and right-leaning in
    /// roughly equal measure, plus several usually rated centre. AP and Reuters are absent because
    /// both retired their public RSS feeds (they now return 401 and 404).
    /// </summary>
    public static IReadOnlyList<NewsSource> DefaultSources { get; } =
    [
        new("BBC News", "https://feeds.bbci.co.uk/news/world/rss.xml", "Centre", "#6B7280"),
        new("Christian Science Monitor", "https://rss.csmonitor.com/feeds/all", "Centre", "#0F766E"),
        new("PBS NewsHour", "https://www.pbs.org/newshour/feeds/rss/headlines", "Centre", "#1D4ED8"),
        new("The Hill", "https://thehill.com/news/feed/", "Centre", "#475569"),
        new("NPR", "https://feeds.npr.org/1001/rss.xml", "Leans left", "#B91C1C"),
        new("CNN", "https://rss.cnn.com/rss/cnn_topstories.rss", "Leans left", "#CC0000"),
        new("Fox News", "https://moxie.foxnews.com/google-publisher/latest.xml", "Leans right", "#003366")
    ];

    public async Task<IReadOnlyList<NewsStory>> GetAsync(IReadOnlyList<NewsSource> sources, int perSource,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(sources.Select(source => ReadAsync(source, perSource, cancellationToken)));
        // Interleave so no single outlet owns the top of the page.
        return results.SelectMany(stories => stories.Select((story, index) => (story, index)))
            .OrderBy(pair => pair.index)
            .ThenByDescending(pair => pair.story.Published ?? DateTimeOffset.MinValue)
            .Select(pair => pair.story)
            .ToArray();
    }

    private async Task<IReadOnlyList<NewsStory>> ReadAsync(NewsSource source, int take, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.FeedUrl);
            // Several publishers reject requests without a browser-like agent.
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (compatible; FamilyHub/1.0; +https://localhost) AppleWebKit/537.36");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(body, source, take);
        }
        // One dead feed must not blank the whole page.
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or System.Xml.XmlException or InvalidOperationException)
        {
            return [];
        }
    }

    private static NewsStory[] Parse(string xml, NewsSource source, int take)
    {
        var document = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var items = document.Descendants("item")
            .Concat(document.Descendants(atom + "entry"))
            .Take(take);

        return items.Select(item => new NewsStory(
                Clean(Value(item, "title", atom)),
                Trim(Clean(Value(item, "description", atom) is { Length: > 0 } d ? d : Value(item, "summary", atom))),
                Link(item, atom),
                source.Name, source.Lean, source.Color,
                ParseDate(Value(item, "pubDate", atom) is { Length: > 0 } p ? p : Value(item, "updated", atom))))
            .Where(story => story.Title.Length > 0)
            .ToArray();
    }

    private static string Value(XElement item, string name, XNamespace atom) =>
        item.Element(name)?.Value ?? item.Element(atom + name)?.Value ?? string.Empty;

    private static string Link(XElement item, XNamespace atom)
    {
        var plain = item.Element("link")?.Value;
        if (!string.IsNullOrWhiteSpace(plain)) return plain.Trim();
        return item.Elements(atom + "link").FirstOrDefault()?.Attribute("href")?.Value ?? string.Empty;
    }

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : null;

    /// <summary>Feed summaries often carry markup and entities; strip both for a plain dashboard.</summary>
    private static string Clean(string value) =>
        WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, "<.*?>", string.Empty,
            RegexOptions.Singleline, TimeSpan.FromSeconds(1))).Replace('\n', ' ').Trim();

    /// <summary>Keeps excerpts short — a pointer to the article, not a substitute for it.</summary>
    private static string Trim(string value) => value.Length <= 220 ? value : value[..220].TrimEnd() + "…";
}

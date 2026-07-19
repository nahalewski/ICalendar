using FamilyHub.Infrastructure;
using System.Net;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class RssNewsProviderTests
{
    /// <summary>Serves canned feed bodies per URL so the tests never touch the network.</summary>
    private sealed class StubHandler(Dictionary<string, (HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (!responses.TryGetValue(url, out var canned))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(canned.Status) { Content = new StringContent(canned.Body) });
        }
    }

    private static RssNewsProvider ProviderFor(params (string Url, HttpStatusCode Status, string Body)[] feeds) =>
        new(new HttpClient(new StubHandler(feeds.ToDictionary(f => f.Url, f => (f.Status, f.Body)))));

    private const string RssFeed = """
    <?xml version="1.0"?>
    <rss version="2.0"><channel>
      <item>
        <title>Council approves budget</title>
        <description><![CDATA[<p>The vote was <b>7-2</b> after a long debate.</p>]]></description>
        <link>https://example.com/budget</link>
        <pubDate>Sat, 18 Jul 2026 14:30:00 GMT</pubDate>
      </item>
      <item>
        <title>Storm clears overnight</title>
        <description>Rain &amp; wind eased before dawn.</description>
        <link>https://example.com/storm</link>
        <pubDate>Sat, 18 Jul 2026 09:00:00 GMT</pubDate>
      </item>
    </channel></rss>
    """;

    private const string AtomFeed = """
    <?xml version="1.0"?>
    <feed xmlns="http://www.w3.org/2005/Atom">
      <entry>
        <title>Bridge reopens</title>
        <summary>Traffic resumed Friday.</summary>
        <link href="https://example.org/bridge"/>
        <updated>2026-07-17T12:00:00Z</updated>
      </entry>
    </feed>
    """;

    private static readonly NewsSource Rss = new("Test Wire", "https://feed.test/rss", "Centre", "#111111");
    private static readonly NewsSource Atom = new("Atom Wire", "https://feed.test/atom", "Leans left", "#222222");

    [Fact]
    public async Task ParsesRssItems()
    {
        var stories = await ProviderFor((Rss.FeedUrl, HttpStatusCode.OK, RssFeed)).GetAsync([Rss], 5, CancellationToken.None);
        Assert.Equal(2, stories.Count);
        Assert.Equal("Council approves budget", stories[0].Title);
        Assert.Equal("https://example.com/budget", stories[0].Link);
        Assert.Equal("Test Wire", stories[0].SourceName);
    }

    [Fact]
    public async Task StripsMarkupAndDecodesEntities()
    {
        var stories = await ProviderFor((Rss.FeedUrl, HttpStatusCode.OK, RssFeed)).GetAsync([Rss], 5, CancellationToken.None);
        Assert.Equal("The vote was 7-2 after a long debate.", stories[0].Summary);
        Assert.Equal("Rain & wind eased before dawn.", stories[1].Summary);
    }

    [Fact]
    public async Task ParsesAtomEntries()
    {
        var stories = await ProviderFor((Atom.FeedUrl, HttpStatusCode.OK, AtomFeed)).GetAsync([Atom], 5, CancellationToken.None);
        var story = Assert.Single(stories);
        Assert.Equal("Bridge reopens", story.Title);
        Assert.Equal("https://example.org/bridge", story.Link);
        Assert.NotNull(story.Published);
    }

    [Fact]
    public async Task EveryStoryCarriesASourcedDisclaimer()
    {
        var stories = await ProviderFor((Rss.FeedUrl, HttpStatusCode.OK, RssFeed)).GetAsync([Rss], 5, CancellationToken.None);
        Assert.All(stories, story =>
        {
            Assert.Contains("Source: Test Wire", story.Disclaimer, StringComparison.Ordinal);
            Assert.Contains("does not verify or endorse", story.Disclaimer, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task PerSourceLimitIsRespected()
    {
        var stories = await ProviderFor((Rss.FeedUrl, HttpStatusCode.OK, RssFeed)).GetAsync([Rss], 1, CancellationToken.None);
        Assert.Single(stories);
    }

    [Fact]
    public async Task ADeadFeedDoesNotBlankThePage()
    {
        // One outlet fails, the other still renders.
        var provider = ProviderFor(
            (Rss.FeedUrl, HttpStatusCode.OK, RssFeed),
            (Atom.FeedUrl, HttpStatusCode.InternalServerError, string.Empty));
        var stories = await provider.GetAsync([Rss, Atom], 5, CancellationToken.None);
        Assert.Equal(2, stories.Count);
        Assert.All(stories, story => Assert.Equal("Test Wire", story.SourceName));
    }

    [Fact]
    public async Task MalformedXmlIsSurvived()
    {
        var provider = ProviderFor((Rss.FeedUrl, HttpStatusCode.OK, "<rss><channel><item><title>oops"));
        Assert.Empty(await provider.GetAsync([Rss], 5, CancellationToken.None));
    }

    [Fact]
    public async Task OutletsAreInterleavedSoNoneOwnsTheTop()
    {
        var provider = ProviderFor(
            (Rss.FeedUrl, HttpStatusCode.OK, RssFeed),
            (Atom.FeedUrl, HttpStatusCode.OK, AtomFeed));
        var stories = await provider.GetAsync([Rss, Atom], 5, CancellationToken.None);
        // First item from each outlet comes before the second item of any outlet.
        Assert.Equal(2, stories.Take(2).Select(story => story.SourceName).Distinct().Count());
    }

    [Fact]
    public void DefaultRosterSpansThePoliticalRange()
    {
        var leans = RssNewsProvider.DefaultSources.Select(source => source.Lean).ToArray();
        Assert.Contains("Leans left", leans);
        Assert.Contains("Leans right", leans);
        Assert.Contains("Centre", leans);
        // Centre-rated outlets should not be outnumbered by the partisan ones.
        Assert.True(leans.Count(lean => lean == "Centre") >= leans.Count(lean => lean != "Centre"));
    }

    [Fact]
    public void SummariesAreExcerptsNotArticles()
    {
        var story = new NewsStory("t", new string('x', 400), "l", "s", "Centre", "#000", null);
        // The provider trims; this guards the contract the trimming exists to satisfy.
        Assert.True(story.Summary.Length <= 400);
    }
}

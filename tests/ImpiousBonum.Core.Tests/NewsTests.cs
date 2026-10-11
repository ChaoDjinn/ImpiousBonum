using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.News;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class NewsTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>Example News</title>
            <item><title>Big &amp; <b>bold</b> story</title><link>https://example.com/1</link><pubDate>Sun, 11 Oct 2026 09:30:00 GMT</pubDate></item>
            <item><title>Second story</title><link>https://example.com/2</link><pubDate>Sun, 11 Oct 2026 08:00:00 BST</pubDate></item>
            <item><title>  </title><link>https://example.com/empty</link></item>
            <item><title>Sneaky</title><link>javascript:alert(1)</link></item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Blog</title>
          <entry><title type="html">Atom &lt;em&gt;post&lt;/em&gt;</title><link rel="edit" href="https://blog/edit"/><link rel="alternate" href="https://blog.example/post"/><updated>2026-10-11T10:00:00Z</updated></entry>
          <entry><title>Older</title><link href="https://blog.example/older"/><published>2026-10-10T10:00:00+02:00</published></entry>
        </feed>
        """;

    private const string Rdf = """
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel><title>Old school</title></channel>
          <item><title>RDF item</title><link>http://old.example/1</link><dc:date>2026-10-09T12:00:00Z</dc:date></item>
        </rdf:RDF>
        """;

    [Fact]
    public void Rss_items_are_read_cleaned_and_dated()
    {
        var items = Feeds.Parse(Rss, "Example");
        Assert.Equal(["Big & bold story", "Second story", "Sneaky"], items.Select(i => i.Title));
        Assert.Equal("https://example.com/1", items[0].Link);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 9, 30, 0, TimeSpan.Zero), items[0].Published);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.FromHours(1)), items[1].Published);
        Assert.Null(items[2].Link);
        Assert.All(items, i => Assert.Equal("Example", i.Source));
    }

    [Fact]
    public void Atom_and_rdf_feeds_are_read_too()
    {
        var atom = Feeds.Parse(Atom, "Blog");
        Assert.Equal(["Atom post", "Older"], atom.Select(i => i.Title));
        Assert.Equal(["https://blog.example/post", "https://blog.example/older"], atom.Select(i => i.Link));
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), atom[1].Published!.Value.ToUniversalTime());

        var rdf = Assert.Single(Feeds.Parse(Rdf, "Old"));
        Assert.Equal("RDF item", rdf.Title);
        Assert.Equal("http://old.example/1", rdf.Link);
    }

    [Theory]
    [InlineData("<html><body>Not a feed</body></html>")]
    [InlineData("just text")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x "boom">]><rss><channel><item><title>&x;</title></item></channel></rss>""")]
    public void Things_that_arent_feeds_are_refused(string xml) => Assert.Throws<FormatException>(() => Feeds.Parse(xml, "x"));

    [Theory]
    [InlineData("Sun, 11 Oct 2026 09:30:00 +0100", 8, 30)]
    [InlineData("Sun, 11 Oct 2026 09:30:00 PDT", 16, 30)]
    [InlineData("2026-10-11T09:30:00Z", 9, 30)]
    public void Dates_in_either_style_are_read(string text, int utcHour, int minute)
    {
        var date = Feeds.ParseDate(text)!.Value.ToUniversalTime();
        Assert.Equal((utcHour, minute), (date.Hour, date.Minute));
    }

    [Fact]
    public void Feeds_merge_newest_first_and_the_same_story_shows_once()
    {
        var merged = NewsProvider.Merge(
        [
            Feeds.Parse(Rss, "Example"),
            Feeds.Parse(Atom, "Blog"),
            [new Headline("Dupe", "https://example.com/1", DateTimeOffset.MaxValue, "Other")],
        ]);
        Assert.Equal("Dupe", merged[0].Title);
        Assert.DoesNotContain(merged, h => h.Title == "Big & bold story");
        Assert.Equal(["Dupe", "Atom post", "Second story", "Sneaky", "Older"], merged.Select(h => h.Title));
    }

    [Fact]
    public async Task The_provider_shows_the_newest_headline_and_says_what_went_wrong()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 10, 12, 0, TimeSpan.Zero));
        using var provider = new NewsProvider(
            () => [new NewsFeed("Blog", "https://blog.example/feed"), new NewsFeed("Broken", "https://broken.example/feed")],
            (url, _) => url.Contains("broken") ? Task.FromResult("<html/>") : Task.FromResult(Atom),
            time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("Atom post", Text(store, NewsProvider.Title));
        Assert.Equal("Blog", Text(store, NewsProvider.Source));
        Assert.Equal("12 min ago", Text(store, NewsProvider.Age));
        Assert.StartsWith("Couldn't read Broken:", Text(store, NewsProvider.Status));
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<Headline>>(store.GetAttachment(NewsProvider.Headlines)).Count);
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(12, "12 min ago")]
    [InlineData(185, "3 h ago")]
    [InlineData(1500, "1 day ago")]
    [InlineData(4000, "2 days ago")]
    public void Ages_read_naturally(double minutes, string expected) => Assert.Equal(expected, NewsProvider.Ago(TimeSpan.FromMinutes(minutes)));

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

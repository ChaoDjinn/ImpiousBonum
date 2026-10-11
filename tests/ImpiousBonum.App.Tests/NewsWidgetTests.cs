using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.News;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class NewsWidgetTests
{
    private static readonly DateTimeOffset Ten = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly Headline[] Headlines =
    [
        new("One", "https://a.example/1", Ten, "A"),
        new("Two", null, Ten.AddMinutes(-30), "B"),
        new("Three", "https://a.example/3", null, "A"),
    ];

    [Fact]
    public void Headlines_are_picked_by_feed_and_count()
    {
        Assert.Equal(["One", "Two"], NewsWidget.Pick(Headlines, null, 2).Select(h => h.Title));
        Assert.Equal(["One", "Three"], NewsWidget.Pick(Headlines, new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase), 5).Select(h => h.Title));
        Assert.Empty(NewsWidget.Pick(null, null, 5));
    }

    [Fact]
    public void The_ticker_moves_on_every_few_seconds_and_wraps_round()
    {
        var start = new DateTime(2026, 10, 11, 10, 0, 0);
        var seen = Enumerable.Range(0, 6).Select(i => NewsWidget.Ticker(Headlines, start.AddSeconds(i * 8), 8).Single().Title).ToList();
        Assert.Equal(3, seen.Distinct().Count());
        Assert.Equal(seen[0], seen[3]);
        Assert.Equal(seen[0], NewsWidget.Ticker(Headlines, start.AddSeconds(7), 8).Single().Title);
        Assert.Empty(NewsWidget.Ticker([], start, 8));
    }

    [Fact]
    public void The_widget_builds_as_a_list_and_a_ticker()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(NewsProvider.Headlines, Headlines);
            foreach (var style in new[] { "list", "ticker" })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "news", ["style"] = style, ["lines"] = 2 }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, Ten.UtcDateTime);
                widget.Refresh(store, Ten.UtcDateTime);
                widget.Refresh(new MetricStore(), Ten.UtcDateTime);
            }
        });
    }

    [Fact]
    public void The_feeds_window_only_takes_web_links()
    {
        Sta.Run(() =>
        {
            var window = new LinkListWindow([], "News feeds", "feed", LinkListWindow.FeedIntro, LinkListWindow.FeedNote, allowWebcal: false);
            window.LinkBox.Text = "webcal://example.com/feed";
            Assert.Contains("https://", window.Problem());
            window.LinkBox.Text = "https://www.theverge.com/rss/index.xml";
            Assert.Null(window.Problem());
            window.Add();
            Assert.Equal([new NamedLink("www.theverge.com", "https://www.theverge.com/rss/index.xml")], window.Links);
            window.Close();
        });
    }
}

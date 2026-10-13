using System.Text.Json.Nodes;
using System.Windows.Controls;
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

    /// <summary>Three feeds as the provider gives them: A's three headlines, B's one (which A has too) and C's none yet.</summary>
    private static readonly FeedHeadlines[] ByFeed =
    [
        new("A", [Headlines[0], Headlines[2], new("Four", null, null, "A")]),
        new("B", [Headlines[0] with { Source = "B" }]),
        new("C", []),
    ];

    [Fact]
    public void Each_feed_gets_a_ticker_in_the_order_chosen()
    {
        // All of them, in the News feeds page's order, each with its newest few; one with nothing yet has no line.
        var all = NewsWidget.PickFeeds(ByFeed, PickList.Everything, 2);
        Assert.Equal([["One", "Three"], ["One"]], all.Select(f => f.Select(h => h.Title).ToArray()));
        // The ones chosen, in the order chosen, whatever their case; one that's gone is left out.
        var chosen = NewsWidget.PickFeeds(ByFeed, PickList.Parse("b, Gone, A", allowAll: true), 5);
        Assert.Equal(["B", "A"], chosen.Select(f => f[0].Source));
        Assert.Equal(3, chosen[1].Count);
        Assert.Empty(NewsWidget.PickFeeds(null, PickList.Everything, 5));
    }

    [Fact]
    public void Each_ticker_moves_on_in_turn_rather_than_all_at_once()
    {
        // Three feeds of three headlines, eight seconds each, watched a second at a time for a minute.
        IReadOnlyList<IReadOnlyList<Headline>> feeds =
            [.. Enumerable.Range(0, 3).Select(IReadOnlyList<Headline> (f) => [.. Enumerable.Range(0, 3).Select(i => new Headline($"{f}.{i}", null, null, $"{f}"))])];
        var start = new DateTime(2026, 10, 11, 10, 0, 0);
        var seen = Enumerable.Range(0, 61).Select(s => NewsWidget.Tickers(feeds, start.AddSeconds(s), 8).Select(h => h.Title).ToList()).ToList();

        Assert.All(seen, shown => Assert.Equal(["0", "1", "2"], shown.Select(t => t[..1])));
        var changes = Enumerable.Range(1, 60).Select(s => Enumerable.Range(0, 3).Where(f => seen[s][f] != seen[s - 1][f]).ToList()).ToList();
        // Never two at the same moment, and each still every eight seconds.
        Assert.All(changes, changed => Assert.True(changed.Count <= 1));
        Assert.All(Enumerable.Range(0, 3), f => Assert.InRange(changes.Count(c => c.Contains(f)), 7, 8));
        // Each goes round its own headlines.
        Assert.All(Enumerable.Range(0, 3), f => Assert.Equal(3, seen.Select(shown => shown[f]).Distinct().Count()));
        Assert.Empty(NewsWidget.Tickers([], start, 8));
    }

    [Fact]
    public void The_widget_builds_as_a_list_a_ticker_and_a_ticker_per_feed()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(NewsProvider.Headlines, Headlines);
            store.SetAttachment(NewsProvider.ByFeed, ByFeed);
            foreach (var (style, rows) in new[] { ("list", 3), ("ticker", 1), ("tickers", 2) })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "news", ["style"] = style, ["lines"] = 2 }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, Ten.UtcDateTime);
                var first = ((StackPanel)widget.Children[0]).Children[0];
                widget.Refresh(store, Ten.UtcDateTime.AddSeconds(1));
                Assert.Equal(rows, ((StackPanel)widget.Children[0]).Children.Count);
                // Nothing on show changed, so nothing was built afresh.
                Assert.Same(first, ((StackPanel)widget.Children[0]).Children[0]);
                widget.Refresh(new MetricStore(), Ten.UtcDateTime);
                Assert.Empty(((StackPanel)widget.Children[0]).Children);
            }

            // Tickers for the feeds chosen, in that order.
            var chosen = WidgetFactory.Create(new JsonObject { ["type"] = "news", ["style"] = "tickers", ["feeds"] = "B, C" }, Theme.From(new ThemeSettings()));
            chosen.Refresh(store, Ten.UtcDateTime);
            Assert.Single(((StackPanel)chosen.Children[0]).Children);
        });
    }

    [Fact]
    public void The_feeds_list_only_takes_web_links()
    {
        Sta.Run(() =>
        {
            var window = LinkListPanel.ForFeeds();
            window.LinkBox.Text = "webcal://example.com/feed";
            Assert.Contains("https://", window.Problem());
            window.LinkBox.Text = "https://www.theverge.com/rss/index.xml";
            Assert.Null(window.Problem());
            // A news widget's Feeds lists them by name, separated by commas.
            window.NameBox.Text = "The Verge, tech";
            Assert.Contains("comma", window.Problem());
            // And "all" there means every feed.
            window.NameBox.Text = " All ";
            Assert.Equal("A feed can't be called all.", window.Problem());
            window.NameBox.Text = string.Empty;
            window.Add();
            Assert.Equal([new NamedLink("www.theverge.com", "https://www.theverge.com/rss/index.xml")], window.Links);
        });
    }
}

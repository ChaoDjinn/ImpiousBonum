using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.News;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Headlines from your news feeds: a list of the newest, a ticker showing one at a time, or a ticker for each feed.
/// Tap one to read it.
/// </summary>
public sealed class NewsWidget : Widget
{
    /// <summary>The names the editor offers for <c>feeds</c>: the News feeds page's, in its order.</summary>
    public const string FeedNames = "newsFeeds";

    public static WidgetDescriptor Descriptor { get; } = new(
        "news", "News",
        "Headlines from your news feeds (set them in the editor, or on the News feeds page), newest first, as a list, one at a time, or one at a time from each feed. Tap a headline to open it in your browser.",
        600, 220,
        [
            Setting.Choice("style", "Style", "list", ["list", "ticker", "tickers"], group: Setting.Content,
                help: "list shows the newest few; ticker shows one at a time, moving on every few seconds; tickers gives each feed a ticker of its own, a line each."),
            Setting.Number("count", "Headlines", 5, 1, 30, group: Setting.Content, help: "How many the list shows, or each ticker goes through."),
            Setting.Number("seconds", "Ticker seconds", 8, 2, 120, group: Setting.Content, help: "How long a ticker shows each headline."),
            Setting.Picks("feeds", "Feeds", PickList.AllValue, source: FeedNames, all: "All feeds", none: "No news feeds yet: add them on the News feeds page.",
                help: "All feeds, or just the ones chosen. With tickers, each gets a line of its own, in the order they're listed."),
            Setting.Toggle("source", "Show where from", true, group: Setting.Content),
            Setting.Toggle("age", "Show how old", true, group: Setting.Content),
            Setting.Toggle("tapToOpen", "Tap to open", true, group: Setting.Content, help: "Opens the article in your browser. Not in the tablet view."),
            Setting.Number("fontSize", "Font size", 22, 6, 200),
            Setting.Number("spacing", "Spacing", 8, 0, 100, group: Setting.Appearance),
            Setting.Number("lines", "Lines per headline", 1, 1, 4, group: Setting.Appearance, help: "Long headlines wrap onto this many lines, then end with …"),
        ],
        (settings, theme) => new NewsWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly bool _ticker;
    private readonly bool _tickers;
    private readonly int _count;
    private readonly int _seconds;
    private readonly PickList _feeds;
    private readonly HashSet<string>? _only;
    private readonly bool _source;
    private readonly bool _age;
    private readonly bool _tapToOpen;
    private readonly double _fontSize;
    private readonly double _spacing;
    private readonly int _lines;
    private string _shown = "\0";

    public NewsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _ticker = settings.String("style") == "ticker";
        _tickers = settings.String("style") == "tickers";
        _count = (int)settings.Number("count");
        _seconds = Math.Max(1, (int)settings.Number("seconds"));
        _feeds = settings.Picks("feeds");
        _only = _feeds.All ? null : _feeds.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _source = settings.Bool("source");
        _age = settings.Bool("age");
        _tapToOpen = settings.Bool("tapToOpen");
        _fontSize = settings.Number("fontSize");
        _spacing = settings.Number("spacing");
        _lines = (int)settings.Number("lines");
        Children.Add(_rows);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        IReadOnlyList<Headline> shown;
        if (_tickers)
        {
            shown = Tickers(PickFeeds(store.GetAttachment(NewsProvider.ByFeed) as IReadOnlyList<FeedHeadlines>, _feeds, _count), now, _seconds);
        }
        else
        {
            var headlines = Pick(store.GetAttachment(NewsProvider.Headlines) as IReadOnlyList<Headline>, _only, _count);
            shown = _ticker ? Ticker(headlines, now, _seconds) : headlines;
        }
        // Ages change by the minute, and tickers every few seconds; rebuild only when something visible does.
        var key = string.Join("\n", shown.Select(h => $"{h.Source}|{h.Title}|{h.Link}|{Age(h, now)}"));
        if (key == _shown)
            return;
        _shown = key;
        Build(shown, now);
    }

    /// <summary>The newest <paramref name="count"/> headlines from the chosen feeds.</summary>
    internal static IReadOnlyList<Headline> Pick(IReadOnlyList<Headline>? headlines, IReadOnlySet<string>? only, int count) =>
        headlines is null ? [] : headlines.Where(h => only is null || only.Contains(h.Source)).Take(count).ToList();

    /// <summary>The one headline the ticker shows now: each for <paramref name="seconds"/>, round and round.</summary>
    internal static IReadOnlyList<Headline> Ticker(IReadOnlyList<Headline> headlines, DateTime now, int seconds)
    {
        if (headlines.Count == 0)
            return [];
        var slot = (long)(now.TimeOfDay.TotalSeconds / seconds) + now.DayOfYear * 100_000L;
        return [headlines[(int)(slot % headlines.Count)]];
    }

    /// <summary>
    /// The feeds that get a ticker each, with their newest <paramref name="count"/> headlines: the chosen ones in the order
    /// chosen, or all of them in the News feeds page's order. Feeds with no headlines yet, or not found, are left out.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<Headline>> PickFeeds(IReadOnlyList<FeedHeadlines>? feeds, PickList chosen, int count)
    {
        if (feeds is null)
            return [];
        var picked = chosen.All
            ? feeds
            : chosen.Names.Select(name => feeds.FirstOrDefault(f => f.Feed.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<FeedHeadlines>();
        return picked.Select(f => (IReadOnlyList<Headline>)f.Headlines.Take(count).ToList()).Where(h => h.Count > 0).ToList();
    }

    /// <summary>
    /// The headline each feed's ticker shows now, top to bottom. Each moves on every <paramref name="seconds"/>, a little
    /// after the one above it, so they don't all change at the same moment.
    /// </summary>
    internal static IReadOnlyList<Headline> Tickers(IReadOnlyList<IReadOnlyList<Headline>> feeds, DateTime now, int seconds) =>
        feeds.SelectMany((headlines, i) => Ticker(headlines, now.AddSeconds(-(double)seconds * i / feeds.Count), seconds)).ToList();

    private string? Age(Headline headline, DateTime now) =>
        _age && headline.Published is { } published ? NewsProvider.Ago(now.ToUniversalTime() - published.UtcDateTime) : null;

    private void Build(IReadOnlyList<Headline> headlines, DateTime now)
    {
        _rows.Children.Clear();
        foreach (var headline in headlines)
        {
            var title = CreateText(_fontSize);
            title.Text = headline.Title;
            title.TextWrapping = _lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.MaxHeight = title.FontSize * title.FontFamily.LineSpacing * _lines + 1;

            var details = string.Join(" · ", new[] { _source ? headline.Source : null, Age(headline, now) }.OfType<string>().Where(t => t.Length > 0));
            var row = new StackPanel();
            row.Children.Add(title);
            if (details.Length > 0)
            {
                var detail = CreateText(_fontSize * 0.7, Theme.Secondary);
                detail.Text = details;
                row.Children.Add(detail);
            }

            FrameworkElement item = row;
            if (_tapToOpen && headline.Link is { } link)
            {
                var tap = new TapTarget { Child = row, Background = Brushes.Transparent };
                tap.Tapped += (_, _) => RequestAction(new WidgetAction(WidgetAction.Open, link));
                item = tap;
            }
            item.Margin = new Thickness(0, 0, 0, _spacing);
            _rows.Children.Add(item);
        }
    }
}

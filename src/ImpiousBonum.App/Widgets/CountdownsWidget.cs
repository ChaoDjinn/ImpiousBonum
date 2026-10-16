using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// How long until the days you're looking forward to (birthdays, holidays, a release, a trip), soonest first, with the
/// ones that are today picked out.
/// </summary>
public sealed class CountdownsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "countdowns", "Countdowns",
        "How long until the days you add, soonest first: birthdays, holidays, a release, a trip. On the day, it says Today in the accent colour.",
        420, 220,
        [
            Setting.Items("events", "Events",
            [
                Setting.PlainText("label", "Label", "Christmas"),
                Setting.PlainText("date", "Date", "12-25",
                    help: "A month and day that comes round every year, e.g. 12-25 or 03-14 09:00, or a date that happens once, e.g. 2026-12-25 or 2026-12-25 18:00."),
            ],
            help: "Each with a label and a date. One that comes round every year starts again the day after; one that happens once leaves the list.",
            defaults: [Event("Halloween", "10-31"), Event("Christmas", "12-25"), Event("New Year", "01-01"), Event("Valentine's Day", "02-14")]),
            Setting.Number("count", "Rows", 5, 1, 50, group: Setting.Content, help: "The most it lists, soonest first."),
            Setting.Choice("style", "How long", "days", ["days", "time"], group: Setting.Content,
                help: "days says 69 days, Tomorrow or Today; time gives the days, hours and minutes, e.g. 69d 02:13."),
            Setting.PlainText("heading", "Heading", null, help: "Shown above the list, e.g. Coming up. Empty shows none."),
            Setting.PlainText("emptyText", "When there's nothing", "Nothing coming up", help: "Empty shows nothing."),
            Setting.Number("fontSize", "Font size", 22, 6, 200),
            Setting.Number("headingSize", "Heading size", null, 6, 200, help: "Empty uses 75% of the font size."),
            Setting.ShrinkListToFit(),
            Setting.Number("spacing", "Row spacing", 6, 0, 100, group: Setting.Appearance),
            Setting.Color("todayColor", "Today's colour", "accent", help: "The colour of the events that are today."),
        ],
        (settings, theme) => new CountdownsWidget(settings, theme));

    private static JsonObject Event(string label, string date) => new() { ["label"] = label, ["date"] = date };

    private readonly Panel _rows;
    private readonly IReadOnlyList<(string Label, string Date)> _events;
    private readonly int _count;
    private readonly bool _time;
    private readonly string _heading;
    private readonly string _emptyText;
    private readonly double _fontSize;
    private readonly double _headingSize;
    private readonly double _spacing;
    private readonly Brush _todayColor;
    private string _shown = "\0";

    public CountdownsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _events = settings.Items("events").Select(e => (e.String("label"), e.String("date"))).ToList();
        _count = (int)settings.Number("count");
        _time = settings.String("style") == "time";
        _heading = settings.String("heading");
        _emptyText = settings.String("emptyText");
        _fontSize = settings.Number("fontSize");
        _headingSize = settings.OptionalNumber("headingSize") ?? _fontSize * 0.75;
        _spacing = settings.Number("spacing");
        _todayColor = theme.Resolve(settings.String("todayColor"), theme.Accent);
        _rows = CreateList();
        Children.Add(_rows);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var rows = Rows(_events, now, _count, _time);
        // Rebuild only when what's shown changes: once a day, or once a minute with the time.
        var key = string.Join("\n", rows.Select(r => $"{r.Label}|{r.HowLong}|{r.Today}"));
        if (key == _shown)
            return;
        _shown = key;
        Build(rows);
    }

    /// <summary>One line of the list: the event, how long to go (null when its date can't be read), and whether it's today.</summary>
    internal sealed record Row(string Label, string? HowLong, bool Today);

    /// <summary>
    /// The events to list, soonest first, as many as <paramref name="count"/>. One that happens once is listed until its
    /// day is over; yearly ones have moved on to next year's by then. Dates that can't be read go last.
    /// </summary>
    internal static IReadOnlyList<Row> Rows(IReadOnlyList<(string Label, string Date)> events, DateTime now, int count, bool time) =>
        events
            .Select(e => (Label: string.IsNullOrWhiteSpace(e.Label) ? e.Date : e.Label, When: TargetDate.Parse(e.Date, now)))
            .Where(e => e.When is not { } when || now < when.Date.AddDays(1))
            .OrderBy(e => e.When ?? DateTime.MaxValue)
            .Take(count)
            .Select(e => new Row(e.Label, e.When is { } when ? HowLong(when, now, time) : null, e.When?.Date == now.Date))
            .ToList();

    /// <summary>
    /// "Today", "Tomorrow" or "69 days"; with <paramref name="time"/>, the days, hours and minutes to go, e.g.
    /// "69d 02:13", then "Today" once it's here.
    /// </summary>
    internal static string HowLong(DateTime when, DateTime now, bool time)
    {
        if (!time)
        {
            return (when.Date - now.Date).Days switch
            {
                <= 0 => "Today",
                1 => "Tomorrow",
                var days => string.Create(CultureInfo.CurrentCulture, $"{days} days"),
            };
        }
        var left = when - now;
        if (left <= TimeSpan.Zero)
            return "Today";
        // Rounded up, so it never says 00:00 before it's here.
        var minutes = (left.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / (24 * 60)}d {minutes / 60 % 24:00}:{minutes % 60:00}");
    }

    private void Build(IReadOnlyList<Row> rows)
    {
        _rows.Children.Clear();
        if (rows.Count == 0)
        {
            if (_emptyText.Length > 0)
            {
                var empty = CreateText(_fontSize, Theme.Secondary);
                empty.Text = _emptyText;
                empty.TextTrimming = TextTrimming.CharacterEllipsis;
                AddRow(_rows, UnderHeading(empty));
            }
            return;
        }

        foreach (var row in rows)
        {
            var color = row.Today ? _todayColor : null;
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = CreateText(_fontSize, color);
            label.Text = row.Label;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var howLong = CreateText(_fontSize, color ?? (row.HowLong is null ? Theme.Critical : Theme.Secondary));
            howLong.Text = row.HowLong ?? "Unknown date";
            howLong.Margin = new Thickness(_fontSize * 0.5, 0, 0, 0);
            Grid.SetColumn(howLong, 1);
            // A label makes do with what's left, down to a few letters; a line that still doesn't fit gets smaller.
            line.Children.Add(Squeezed(label, _fontSize * 3));
            line.Children.Add(howLong);
            var fitted = Fit(line, FitDirection.Width);

            var item = _rows.Children.Count == 0 ? UnderHeading(fitted) : fitted;
            item.Margin = new Thickness(0, 0, 0, _spacing);
            AddRow(_rows, item);
        }
    }

    /// <summary>
    /// The heading, if there is one, with the first row under it: they go together, so a list too short for both
    /// shrinks them together rather than show a heading over nothing.
    /// </summary>
    private FrameworkElement UnderHeading(FrameworkElement first)
    {
        if (_heading.Length == 0)
            return first;
        var heading = CreateText(_headingSize, Theme.Secondary);
        heading.Text = _heading;
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.Margin = new Thickness(0, 0, 0, _spacing);
        var group = new StackPanel();
        group.Children.Add(heading);
        group.Children.Add(first);
        return group;
    }
}

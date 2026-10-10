using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Labelled bars, one per metric, each filled to the metric's share of its maximum. A bar whose metric has no reading
/// can hide itself, and the whole widget hides when none has one. "Claude usage" is the same widget, set up for
/// Claude's session and weekly limits, whose metrics only have readings while Claude Code is open.
/// </summary>
public sealed class BarsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = Describe(
        "bars", "Bars", "Labelled bars, one per metric, each filled to the metric's share of its maximum, e.g. CPU and RAM load.",
        [Bar("CPU", CpuProvider.Load), Bar("RAM", MemoryProvider.Load)]);

    public static WidgetDescriptor ClaudeDescriptor { get; } = Describe(
        "claude", "Claude usage",
        "Your Claude plan's session (5-hour) and weekly usage, as Claude Code reports them. Shown only while Claude Code is open "
        + "and reporting. Set it up from the tray: Claude usage → Connect Claude Code.",
        [
            Bar("Session", ClaudeUsageProvider.Session, $"{{{ClaudeUsageProvider.Session}:0}} · {{{ClaudeUsageProvider.SessionResets}}}"),
            Bar("Week", ClaudeUsageProvider.Week, $"{{{ClaudeUsageProvider.Week}:0}} · {{{ClaudeUsageProvider.WeekResets}}}"),
        ]);

    private static WidgetDescriptor Describe(string type, string name, string description, IReadOnlyList<JsonObject> bars) => new(
        type, name, description,
        425, 130,
        [
            Setting.Items("bars", "Bars",
            [
                Setting.PlainText("label", "Label", "CPU"),
                Setting.Metric("metric", "Metric", CpuProvider.Load, help: "Fills the bar."),
                Setting.Template("text", "Value", null, help: "Shown on the right. Empty shows the metric's value."),
                Setting.Number("max", "Maximum", null, double.MinValue, double.MaxValue,
                    help: "A full bar. Empty uses the metric's natural maximum: 100 for percentages, the total for memory."),
            ],
            defaults: bars),
            Setting.Number("fontSize", "Label size", 25, 6, 200),
            Setting.Number("valueFontSize", "Value size", null, 6, 200, help: "Empty uses 85% of the label size."),
            Setting.Number("barHeight", "Bar height", 8, 1, 100, group: Setting.Appearance),
            Setting.Number("spacing", "Bar spacing", 12, 0, 200, group: Setting.Appearance),
            Setting.Toggle("hideUnavailable", "Hide without a reading", true, group: Setting.Appearance,
                help: "Hides a bar whose metric has no reading, and the whole widget when none has one. They show faded while you edit the layout."),
            Setting.Thresholds("Changes each bar. An empty metric uses that bar's metric."),
        ],
        (settings, theme) => new BarsWidget(settings, theme));

    private static JsonObject Bar(string label, string metric, string? text = null)
    {
        var bar = new JsonObject { ["label"] = label, ["metric"] = metric };
        if (text is not null)
            bar["text"] = text;
        return bar;
    }

    private readonly List<Row> _rows = [];
    private readonly bool _hideUnavailable;
    private readonly ThresholdColors _thresholds;

    public BarsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var fontSize = settings.Number("fontSize");
        var valueFontSize = settings.OptionalNumber("valueFontSize") ?? fontSize * 0.85;
        var barHeight = settings.Number("barHeight");
        var spacing = settings.Number("spacing");
        _hideUnavailable = settings.Bool("hideUnavailable");
        _thresholds = new ThresholdColors(settings, theme);

        var stack = new StackPanel();
        foreach (var bar in settings.Items("bars"))
        {
            var metric = bar.String("metric");
            var header = new Grid();
            var label = CreateText(fontSize);
            label.Text = bar.String("label");
            var value = CreateText(valueFontSize, theme.Secondary, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(label);
            header.Children.Add(value);

            var track = new Grid { Height = barHeight, Background = theme.Track, Margin = new Thickness(0, 2, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var fill = new Border { Background = theme.Accent };
            track.Children.Add(fill);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, spacing) };
            row.Children.Add(header);
            row.Children.Add(track);
            stack.Children.Add(row);

            var text = bar.OptionalString("text") is { Length: > 0 } t ? t : $"{{{metric}}}";
            _rows.Add(new Row(metric, bar.OptionalNumber("max"), ValueTemplate.Parse(text), row, value, track, fill));
        }

        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var available = _rows.Select(row => store.TryGet(row.Metric, out var sample) && sample.Value is double v && !double.IsNaN(v)).ToList();
        var anyAvailable = available.Contains(true);

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Value.Text = row.Template.Render(store);
            var fraction = available[i] && store.TryGet(row.Metric, out var sample) ? Fraction(sample, row.Max) : 0;
            row.Track.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
            row.Track.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
            row.Fill.Background = _thresholds.Pick(store, row.Metric, Theme.Accent);

            // While editing, an unreadable bar fades rather than disappears (unless the whole widget is already faded).
            var hidden = _hideUnavailable && !available[i];
            row.Panel.Visibility = hidden && !Editing ? Visibility.Collapsed : Visibility.Visible;
            row.Panel.Opacity = hidden && Editing && anyAvailable ? FadedOpacity : 1;
        }

        SetUnavailable(_hideUnavailable && !anyAvailable);
    }

    /// <summary>How full the bar is, 0–1: the value against the bar's maximum, the metric's, or 100.</summary>
    internal static double Fraction(MetricSample sample, double? max)
    {
        var full = max ?? sample.Definition.Max ?? 100;
        return sample.Value is double value && full > 0 ? Math.Clamp(value / full, 0, 1) : 0;
    }

    private sealed record Row(string Metric, double? Max, ValueTemplate Template, StackPanel Panel, TextBlock Value, Grid Track, Border Fill);
}

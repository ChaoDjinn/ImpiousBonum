using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A big value with a small trend line under it and the lowest and highest readings over the same stretch: a graph's
/// story in the space of a text widget.
/// </summary>
public sealed class StatWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "stat", "Stat tile",
        "A big value with a small trend line and its lowest and highest readings over the last minute or so.",
        300, 160,
        [
            Setting.PlainText("label", "Label", "CPU"),
            Setting.Metric("metric", "Metric", "cpu.load"),
            Setting.Template("text", "Value text", null, help: "Empty shows the metric's value."),
            Setting.Number("points", "History (seconds)", 60, 2, MetricStore.DefaultHistoryLength, group: Setting.Appearance),
            Setting.Choice("style", "Trend line", "area", ["area", "line", "none"], group: Setting.Appearance),
            Setting.Toggle("showRange", "Lowest and highest", true, group: Setting.Appearance),
            Setting.Number("fontSize", "Value size", 56, 6, 400),
            Setting.Number("labelSize", "Label size", 20, 6, 200),
            Setting.Thresholds("Changes the value and the trend line. An empty metric uses the tile's metric."),
        ],
        (settings, theme) => new StatWidget(settings, theme));

    private readonly string _metric;
    private readonly ValueTemplate _template;
    private readonly int _points;
    private readonly TextBlock _value;
    private readonly TextBlock? _range;
    private readonly GraphElement? _trend;
    private readonly ThresholdColors _thresholds;
    private readonly Dictionary<Brush, Brush> _fills = [];

    public StatWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _metric = settings.String("metric");
        _template = ValueTemplate.Parse(settings.OptionalString("text") is { Length: > 0 } text ? text : $"{{{_metric}}}");
        _points = (int)settings.Number("points");
        _thresholds = new ThresholdColors(settings, theme);
        var labelSize = settings.Number("labelSize");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var label = CreateText(labelSize, theme.Secondary);
        label.Text = settings.String("label");
        Children.Add(label);
        if (settings.Bool("showRange"))
        {
            _range = CreateText(labelSize * 0.8, theme.Secondary, HorizontalAlignment.Right);
            _range.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(_range);
        }

        _value = CreateText(settings.Number("fontSize"));
        _value.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(_value, 1);
        Children.Add(_value);

        var style = settings.String("style");
        if (style != "none")
        {
            _trend = new GraphElement
            {
                Kind = style == "line" ? GraphStyle.Line : GraphStyle.Area,
                Points = _points,
                Stroke = theme.Accent,
                Fill = Fill(theme.Accent),
                Thickness = 2,
                Margin = new Thickness(0, 4, 0, 0),
            };
            SetRow(_trend, 2);
            Children.Add(_trend);
        }
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _value.Text = _template.Render(store);
        var color = _thresholds.Pick(store, _metric, Theme.Accent);
        _value.Foreground = ReferenceEquals(color, Theme.Accent) ? Theme.Foreground : color;

        var history = store.GetHistory(_metric, _points);
        var known = store.TryGet(_metric, out var sample);
        var range = Range(history);
        if (_trend is not null)
        {
            _trend.Stroke = color;
            _trend.Fill = Fill(color);
            // Scaled to its own lowest and highest, so small changes still show as a shape.
            if (range is { } r)
                (_trend.Minimum, _trend.Maximum) = Scale(r.Min, r.Max);
            _trend.SetValues(history);
        }
        if (_range is not null)
            _range.Text = known && range is { } shown ? $"↓ {Format(sample, shown.Min)}   ↑ {Format(sample, shown.Max)}" : string.Empty;
    }

    /// <summary>The lowest and highest readings, ignoring gaps; null when there are none.</summary>
    internal static (double Min, double Max)? Range(IEnumerable<double> history)
    {
        var values = history.Where(v => !double.IsNaN(v)).ToList();
        return values.Count == 0 ? null : (values.Min(), values.Max());
    }

    /// <summary>The trend line's scale: the range with a little room above and below, or around a flat value.</summary>
    internal static (double Min, double Max) Scale(double min, double max)
    {
        var room = max > min ? (max - min) * 0.1 : Math.Max(Math.Abs(max) * 0.05, 1);
        return (min - room, max + room);
    }

    private static string Format(MetricSample sample, double value) =>
        MetricFormatter.Format(sample with { Value = value, Text = null });

    private Brush Fill(Brush stroke)
    {
        if (!_fills.TryGetValue(stroke, out var fill))
        {
            var color = stroke is SolidColorBrush solid ? solid.Color : Theme.AccentColor;
            var brush = new LinearGradientBrush(Color.FromArgb(0x90, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90);
            brush.Freeze();
            _fills[stroke] = fill = brush;
        }
        return fill;
    }
}

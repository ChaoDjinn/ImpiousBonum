using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A header (label left, value right) above a history graph.</summary>
public sealed class GraphWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "graph", "Graph", "A label and live value above a scrolling history of one metric, drawn as a line or a filled area, from the last few minutes up to the last 24 hours.",
        427, 100,
        [
            Setting.PlainText("label", "Label", "CPU"),
            Setting.Metric("metric", "Metric", "cpu.load"),
            Setting.Template("text", "Value text", null, help: "Shown on the right of the header. Empty shows the metric's value."),
            Setting.Choice("style", "Style", "line", ["line", "area"], group: Setting.Appearance),
            GraphSpan.Setting(),
            GraphSpan.Points(120),
            Setting.Toggle("band", "Lowest and highest", true, group: Setting.Appearance,
                help: "With a longer time span, a pale band behind the line from the lowest reading to the highest in each point's time, so a short spike still shows."),
            Setting.Number("min", "Minimum", 0, double.MinValue, double.MaxValue, group: Setting.Appearance),
            Setting.Number("max", "Maximum", null, double.MinValue, double.MaxValue, group: Setting.Appearance,
                help: "Empty uses the metric's natural maximum (100 for percentages, the total for memory), else scales to fit."),
            Setting.Number("lineThickness", "Line thickness", 2.5, 0.5, 20, group: Setting.Appearance),
            Setting.Number("fontSize", "Font size", 26, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Thresholds("Changes the line or area and the value. An empty metric uses the graph's metric."),
        ],
        (settings, theme) => new GraphWidget(settings, theme));

    private readonly string _metric;
    private readonly ValueTemplate _template;
    private readonly TextBlock _value;
    private readonly WarningSymbol? _symbol;
    private readonly GraphElement _graph;
    private readonly double? _configuredMax;
    private readonly ThresholdColors _thresholds;
    private readonly LongSpan? _span;
    private readonly bool _band;
    private readonly Dictionary<Brush, Brush> _areaFills = new();
    private readonly Dictionary<Brush, Brush> _bandFills = new();

    public GraphWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _metric = settings.String("metric");
        _span = GraphSpan.Of(settings);
        _band = settings.Bool("band");
        _template = ValueTemplate.Parse(settings.OptionalString("text") is { Length: > 0 } text ? text : $"{{{_metric}}}");
        _configuredMax = settings.OptionalNumber("max");
        _thresholds = new ThresholdColors(settings, theme);

        var fontSize = settings.Number("fontSize");
        var style = settings.String("style") == "area" ? GraphStyle.Area : GraphStyle.Line;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var label = CreateText(fontSize);
        label.Text = settings.String("label");
        _value = CreateText(fontSize * 0.9, theme.Secondary, HorizontalAlignment.Right);
        _value.VerticalAlignment = VerticalAlignment.Center;
        _symbol = CreateSymbol(_value);
        Children.Add(LabelAndValue(label, _value, _symbol));

        _graph = new GraphElement
        {
            Kind = style,
            Points = _span?.Points() ?? (int)settings.Number("points"),
            Minimum = settings.Number("min"),
            Maximum = _configuredMax,
            Stroke = theme.Accent,
            Fill = AreaFill(theme.Accent),
            Thickness = settings.Number("lineThickness"),
            Margin = new Thickness(0, fontSize * 0.35, 0, 0),
        };
        SetRow(_graph, 1);
        Children.Add(_graph);
        // The graph takes what's left, and everything shrinks when the header alone is too tall.
        FitContent(FitDirection.Height);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _value.Text = _template.Render(store);
        var color = _thresholds.Pick(store, _metric, Theme.Accent, out var level);
        _value.Foreground = ReferenceEquals(color, Theme.Accent) ? Theme.Secondary : color;
        _symbol?.Show(level, color);
        _graph.Stroke = color;
        _graph.Fill = AreaFill(color);
        if (_configuredMax is null && store.TryGet(_metric, out var sample))
            _graph.Maximum = sample.Definition.Max;
        if (_span is { } span)
        {
            var history = store.GetHistory(_metric, span);
            _graph.BandFill = _band ? BandFill(color) : null;
            _graph.SetValues(history.Average, history.Low, history.High);
        }
        else
            _graph.SetValues(store.GetHistory(_metric, _graph.Points));
    }

    /// <summary>The line's colour, faint: the band of lowest and highest readings behind it.</summary>
    private Brush BandFill(Brush stroke)
    {
        if (!_bandFills.TryGetValue(stroke, out var fill))
        {
            var color = stroke is SolidColorBrush solid ? solid.Color : Theme.AccentColor;
            var brush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
            brush.Freeze();
            _bandFills[stroke] = fill = brush;
        }
        return fill;
    }

    private Brush AreaFill(Brush stroke)
    {
        if (!_areaFills.TryGetValue(stroke, out var fill))
            _areaFills[stroke] = fill = CreateAreaFill(stroke is SolidColorBrush solid ? solid.Color : Theme.AccentColor);
        return fill;
    }

    private static Brush CreateAreaFill(Color accent)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(0xF0, accent.R, accent.G, accent.B),
            Color.FromArgb(0x10, accent.R, accent.G, accent.B),
            90);
        brush.Freeze();
        return brush;
    }
}

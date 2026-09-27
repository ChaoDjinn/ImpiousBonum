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
        "graph", "Graph", "A label and live value above a scrolling history of one metric, drawn as a line or a filled area.",
        427, 100,
        [
            Setting.PlainText("label", "Label", "CPU"),
            Setting.Metric("metric", "Metric", "cpu.load"),
            Setting.Template("text", "Value text", null, help: "Shown on the right of the header. Empty shows the metric's value."),
            Setting.Choice("style", "Style", "line", ["line", "area"], group: Setting.Appearance),
            Setting.Number("points", "History (seconds)", 120, 2, MetricStore.DefaultHistoryLength, group: Setting.Appearance),
            Setting.Number("min", "Minimum", 0, double.MinValue, double.MaxValue, group: Setting.Appearance),
            Setting.Number("max", "Maximum", null, double.MinValue, double.MaxValue, group: Setting.Appearance,
                help: "Empty uses the metric's natural maximum (100 for percentages, the total for memory), else scales to fit."),
            Setting.Number("lineThickness", "Line thickness", 2.5, 0.5, 20, group: Setting.Appearance),
            Setting.Number("fontSize", "Font size", 26, 6, 200),
        ],
        (settings, theme) => new GraphWidget(settings, theme));

    private readonly string _metric;
    private readonly ValueTemplate _template;
    private readonly TextBlock _value;
    private readonly GraphElement _graph;
    private readonly double? _configuredMax;

    public GraphWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _metric = settings.String("metric");
        _template = ValueTemplate.Parse(settings.OptionalString("text") is { Length: > 0 } text ? text : $"{{{_metric}}}");
        _configuredMax = settings.OptionalNumber("max");

        var fontSize = settings.Number("fontSize");
        var style = settings.String("style") == "area" ? GraphStyle.Area : GraphStyle.Line;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var label = CreateText(fontSize);
        label.Text = settings.String("label");
        _value = CreateText(fontSize * 0.9, theme.Secondary, HorizontalAlignment.Right);
        _value.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(label);
        Children.Add(_value);

        _graph = new GraphElement
        {
            Kind = style,
            Points = (int)settings.Number("points"),
            Minimum = settings.Number("min"),
            Maximum = _configuredMax,
            Stroke = theme.Accent,
            Fill = CreateAreaFill(theme.AccentColor),
            Thickness = settings.Number("lineThickness"),
            Margin = new Thickness(0, fontSize * 0.35, 0, 0),
        };
        SetRow(_graph, 1);
        Children.Add(_graph);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _value.Text = _template.Render(store);
        if (_configuredMax is null && store.TryGet(_metric, out var sample))
            _graph.Maximum = sample.Definition.Max;
        _graph.SetValues(store.GetHistory(_metric, _graph.Points));
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

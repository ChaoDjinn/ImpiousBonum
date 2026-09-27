using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A header (label left, value right) above a history graph.
/// Settings: <c>label</c>, <c>text</c> (template for the right side), <c>metric</c>, <c>style</c> (line/area),
/// <c>fontSize</c>, <c>points</c>, <c>min</c>, <c>max</c> (defaults to the metric's natural maximum, else auto).
/// </summary>
public sealed class GraphWidget : Widget
{
    private readonly string _metric;
    private readonly ValueTemplate _template;
    private readonly TextBlock _value;
    private readonly GraphElement _graph;
    private readonly double? _configuredMax;

    public GraphWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        _metric = definition.GetString("metric") ?? string.Empty;
        _template = ValueTemplate.Parse(definition.GetString("text") ?? $"{{{_metric}}}");
        _configuredMax = definition.GetDouble("max");

        var fontSize = definition.GetDouble("fontSize", 26);
        var style = definition.GetString("style")?.Equals("area", StringComparison.OrdinalIgnoreCase) == true ? GraphStyle.Area : GraphStyle.Line;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var label = CreateText(fontSize);
        label.Text = definition.GetString("label") ?? string.Empty;
        _value = CreateText(fontSize * 0.9, theme.Secondary, HorizontalAlignment.Right);
        _value.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(label);
        Children.Add(_value);

        _graph = new GraphElement
        {
            Kind = style,
            Points = (int)definition.GetDouble("points", 120),
            Minimum = definition.GetDouble("min", 0),
            Maximum = _configuredMax,
            Stroke = theme.Accent,
            Fill = CreateAreaFill(theme.AccentColor),
            Thickness = definition.GetDouble("lineThickness", 2.5),
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

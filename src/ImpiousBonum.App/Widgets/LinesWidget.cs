using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>Several metrics' histories on one shared scale, each in its own colour, with a legend of live values.</summary>
public sealed class LinesWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "lines", "Multi-line graph",
        "Several metrics' histories on one scale, each in its own colour, e.g. CPU and GPU load, or download and upload. A legend above shows each line's value.",
        427, 160,
        [
            Setting.Items("lines", "Lines",
            [
                Setting.PlainText("label", "Label", "CPU"),
                Setting.Metric("metric", "Metric", CpuProvider.Load),
                Setting.Color("color", "Colour", "accent", group: Setting.Content),
                Setting.Template("text", "Value", null, help: "Shown in the legend. Empty shows the metric's value."),
            ],
            defaults: [Line("CPU", CpuProvider.Load, "accent"), Line("GPU", GpuProvider.Load, "warning")]),
            Setting.Toggle("legend", "Legend", true, group: Setting.Content, help: "Each line's colour, label and value above the graph."),
            Setting.Number("points", "History (seconds)", 120, 2, MetricStore.DefaultHistoryLength, group: Setting.Appearance),
            Setting.Number("min", "Minimum", 0, double.MinValue, double.MaxValue, group: Setting.Appearance),
            Setting.Number("max", "Maximum", null, double.MinValue, double.MaxValue, group: Setting.Appearance,
                help: "Empty uses the metrics' natural maximum when they share one (100 for percentages), else scales to fit the highest line."),
            Setting.Number("lineThickness", "Line thickness", 2, 0.5, 20, group: Setting.Appearance),
            Setting.Number("fontSize", "Font size", 18, 6, 200),
            Setting.ShrinkToFit(),
        ],
        (settings, theme) => new LinesWidget(settings, theme));

    private static JsonObject Line(string label, string metric, string color) =>
        new() { ["label"] = label, ["metric"] = metric, ["color"] = color };

    private readonly List<Series> _series = [];
    private readonly double? _configuredMax;
    private readonly int _points;

    private sealed record Series(string Metric, ValueTemplate Template, TextBlock? Value, GraphElement Graph);

    public LinesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _configuredMax = settings.OptionalNumber("max");
        _points = (int)settings.Number("points");
        var fontSize = settings.Number("fontSize");
        var legend = settings.Bool("legend");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new WrapPanel();
        var plot = new Grid { Margin = new Thickness(0, legend ? fontSize * 0.35 : 0, 0, 0) };
        SetRow(plot, 1);

        foreach (var line in settings.Items("lines"))
        {
            var metric = line.String("metric");
            var color = theme.Resolve(line.String("color"), theme.Accent);
            var graph = new GraphElement
            {
                Kind = GraphStyle.Line,
                Points = _points,
                Minimum = settings.Number("min"),
                Maximum = _configuredMax,
                Stroke = color,
                Thickness = settings.Number("lineThickness"),
            };
            plot.Children.Add(graph);

            TextBlock? value = null;
            if (legend)
            {
                var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, fontSize, 0) };
                entry.Children.Add(new Rectangle
                {
                    Width = fontSize * 0.6,
                    Height = fontSize * 0.6,
                    RadiusX = fontSize * 0.12,
                    RadiusY = fontSize * 0.12,
                    Fill = color,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, fontSize * 0.3, 0),
                });
                var label = CreateText(fontSize);
                label.Text = line.String("label");
                entry.Children.Add(label);
                value = CreateText(fontSize * 0.9, theme.Secondary);
                value.Margin = new Thickness(fontSize * 0.3, 0, 0, 0);
                value.VerticalAlignment = VerticalAlignment.Center;
                entry.Children.Add(value);
                // Entries wrap onto more lines as they need; one too wide for the widget on its own shrinks.
                header.Children.Add(Fit(entry, FitDirection.Width));
            }

            var template = ValueTemplate.Parse(line.OptionalString("text") is { Length: > 0 } text ? text : $"{{{metric}}}");
            _series.Add(new Series(metric, template, value, graph));
        }

        if (legend)
            Children.Add(header);
        Children.Add(plot);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var histories = _series.Select(s => store.GetHistory(s.Metric, _points)).ToList();
        var naturals = _series.Select(s => store.TryGet(s.Metric, out var sample) ? sample.Definition.Max : null).ToList();
        var max = _configuredMax ?? SharedMaximum(naturals, histories);
        for (var i = 0; i < _series.Count; i++)
        {
            var series = _series[i];
            if (series.Value is not null)
                series.Value.Text = series.Template.Render(store);
            series.Graph.Maximum = max;
            series.Graph.SetValues(histories[i]);
        }
    }

    /// <summary>
    /// The top of the shared scale: the metrics' natural maximum when they all have the same one (100 for percentages),
    /// otherwise the highest value any line shows, so every line fits.
    /// </summary>
    internal static double? SharedMaximum(IReadOnlyList<double?> naturals, IReadOnlyList<double[]> histories)
    {
        if (naturals.Count > 0 && naturals.All(n => n is not null) && naturals.Distinct().Count() == 1)
            return naturals[0];
        var highest = histories.SelectMany(h => h).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
        return double.IsNaN(highest) ? null : highest;
    }
}

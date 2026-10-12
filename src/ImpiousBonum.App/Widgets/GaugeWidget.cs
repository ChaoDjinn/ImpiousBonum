using System.Windows;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A dial filled to a metric's share of its range, with the value in the middle and a label.</summary>
public sealed class GaugeWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "gauge", "Gauge", "A dial filled to a metric's share of its range, with the value in the middle, e.g. GPU load or CPU temperature.",
        260, 240,
        [
            Setting.PlainText("label", "Label", "CPU"),
            Setting.Metric("metric", "Metric", "cpu.load", help: "Fills the dial."),
            Setting.Template("text", "Value text", null, help: "Shown in the middle. Empty shows the metric's value."),
            Setting.Choice("style", "Shape", "arc", ["arc", "half", "ring"], group: Setting.Appearance,
                help: "arc is a 270° dial with the label in the gap, half a semicircle, ring a full circle."),
            Setting.Number("min", "Minimum", 0, double.MinValue, double.MaxValue, group: Setting.Appearance, help: "An empty dial."),
            Setting.Number("max", "Maximum", null, double.MinValue, double.MaxValue, group: Setting.Appearance,
                help: "A full dial. Empty uses the metric's natural maximum: 100 for percentages, the total for memory."),
            Setting.Number("thickness", "Thickness", 16, 1, 200, group: Setting.Appearance),
            Setting.Color("color", "Colour", "accent", help: "The filled part of the dial."),
            Setting.Color("trackColor", "Track colour", "track", help: "The empty part of the dial. track is the theme's bar track."),
            Setting.Number("fontSize", "Value size", 56, 6, 400),
            Setting.Number("labelSize", "Label size", 24, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Thresholds("Changes the dial and the value. An empty metric uses the gauge's metric."),
        ],
        (settings, theme) => new GaugeWidget(settings, theme));

    private readonly string _metric;
    private readonly ValueTemplate _template;
    private readonly double _min;
    private readonly double? _max;
    private readonly Brush _color;
    private readonly ThresholdColors _thresholds;
    private readonly GaugeElement _gauge;

    public GaugeWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _metric = settings.String("metric");
        _template = ValueTemplate.Parse(settings.OptionalString("text") is { Length: > 0 } text ? text : $"{{{_metric}}}");
        _min = settings.Number("min");
        _max = settings.OptionalNumber("max");
        _color = theme.Resolve(settings.String("color"), theme.Accent);
        _thresholds = new ThresholdColors(settings, theme);

        _gauge = new GaugeElement
        {
            Shape = settings.String("style") switch
            {
                "half" => GaugeShape.Half,
                "ring" => GaugeShape.Ring,
                _ => GaugeShape.Arc,
            },
            Thickness = settings.Number("thickness"),
            Track = theme.Resolve(settings.String("trackColor"), theme.Track),
            Typeface = new Typeface(theme.FontFamily, FontStyles.Normal, theme.FontWeight, FontStretches.Normal),
            Label = settings.String("label"),
            LabelSize = settings.Number("labelSize"),
            LabelBrush = theme.Secondary,
            ValueSize = settings.Number("fontSize"),
            ShrinkValue = settings.Bool(Setting.ShrinkToFitKey),
        };
        Children.Add(_gauge);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var fraction = store.TryGet(_metric, out var sample) ? Fraction(sample, _min, _max) : 0;
        var color = _thresholds.Pick(store, _metric, _color);
        _gauge.Update(fraction, _template.Render(store), color, ReferenceEquals(color, _color) ? Theme.Foreground : color);
    }

    /// <summary>How full the dial is, 0–1: the value between the minimum and the gauge's maximum, the metric's, or 100.</summary>
    internal static double Fraction(MetricSample sample, double min, double? max)
    {
        var full = max ?? sample.Definition.Max ?? 100;
        return sample.Value is double value && full > min ? Math.Clamp((value - min) / (full - min), 0, 1) : 0;
    }
}

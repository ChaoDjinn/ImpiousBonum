using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A widget's <c>thresholds</c> list, with each rule's colour resolved against the theme once up front.</summary>
public sealed class ThresholdColors
{
    private readonly IReadOnlyList<ThresholdRule> _rules;
    private readonly Dictionary<ThresholdRule, Brush> _brushes = new();

    public ThresholdColors(WidgetSettings settings, Theme theme)
    {
        _rules = settings.Items(Setting.ThresholdsKey)
            .Select(r => new ThresholdRule(r.OptionalString("metric"), r.OptionalNumber("above"), r.OptionalNumber("below"), r.String("color")))
            .ToList();
        foreach (var rule in _rules)
            _brushes.TryAdd(rule, theme.Resolve(rule.Color, theme.Critical));
    }

    /// <summary>The brush of the winning rule, or <paramref name="normal"/> when none matches.</summary>
    public Brush Pick(MetricStore store, string? ownMetric, Brush normal) =>
        _rules.Count > 0 && ThresholdRule.Select(_rules, ownMetric, store) is { } rule ? _brushes[rule] : normal;
}

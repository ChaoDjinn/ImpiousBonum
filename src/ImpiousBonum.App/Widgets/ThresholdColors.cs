using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A widget's <c>thresholds</c> list, with each rule's colour resolved against the theme once up front.</summary>
public sealed class ThresholdColors
{
    private readonly IReadOnlyList<ThresholdRule> _rules;
    private readonly Dictionary<ThresholdRule, Brush> _brushes = new();
    private readonly Theme _theme;

    public ThresholdColors(WidgetSettings settings, Theme theme)
    {
        _theme = theme;
        _rules = settings.Items(Setting.ThresholdsKey)
            .Select(r => new ThresholdRule(r.OptionalString("metric"), r.OptionalNumber("above"), r.OptionalNumber("below"), r.String("color")))
            .ToList();
        foreach (var rule in _rules)
            _brushes.TryAdd(rule, theme.Resolve(rule.Color, theme.Critical));
    }

    /// <summary>The brush of the winning rule, or <paramref name="normal"/> when none matches.</summary>
    public Brush Pick(MetricStore store, string? ownMetric, Brush normal) => Pick(store, ownMetric, normal, out _);

    /// <summary>As above, with which <see cref="WarningSymbol"/> goes beside a value in that colour.</summary>
    public Brush Pick(MetricStore store, string? ownMetric, Brush normal, out ThresholdLevel level) =>
        Brush(_rules.Count > 0 ? ThresholdRule.Select(_rules, ownMetric, store) : null, normal, out level);

    /// <summary>As above, for a widget whose own value isn't a metric (a rule with an empty metric reads <paramref name="own"/>).</summary>
    public Brush Pick(MetricStore store, double own, Brush normal, out ThresholdLevel level) =>
        Brush(
            _rules.Count > 0 ? ThresholdRule.Select(_rules, OwnValue, id => id == OwnValue ? own : store.TryGet(id, out var sample) ? sample.Value : null) : null,
            normal,
            out level);

    /// <summary>
    /// The rule's brush, or <paramref name="normal"/> for none. Only the theme's own warning and critical colours have a
    /// symbol: what a colour of the layout's own means, only its author knows.
    /// </summary>
    private Brush Brush(ThresholdRule? rule, Brush normal, out ThresholdLevel level)
    {
        var brush = rule is null ? normal : _brushes[rule];
        level = rule is null ? ThresholdLevel.None
            : ReferenceEquals(brush, _theme.Warning) ? ThresholdLevel.Warning
            : ReferenceEquals(brush, _theme.Critical) ? ThresholdLevel.Critical
            : ThresholdLevel.None;
        return brush;
    }

    // Not a metric id anyone can type.
    private const string OwnValue = "\0own";
}

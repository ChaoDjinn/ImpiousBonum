namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// A warning colour: when <see cref="Metric"/> is above <see cref="Above"/> and/or below <see cref="Below"/>, draw in <see cref="Color"/>.
/// A rule with neither bound never matches. A null <see cref="Metric"/> means the widget's own metric.
/// </summary>
public sealed record ThresholdRule(string? Metric, double? Above, double? Below, string Color)
{
    public bool Matches(double value) =>
        (Above is not null || Below is not null)
        && (Above is not { } above || value > above)
        && (Below is not { } below || value < below);

    /// <summary>
    /// The last rule that matches, so rules are listed mildest first (amber above 80, then red above 90).
    /// A rule whose metric has no reading never matches, so missing values keep the normal colour.
    /// </summary>
    public static ThresholdRule? Select(IReadOnlyList<ThresholdRule> rules, string? ownMetric, Func<string, double?> read)
    {
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var rule = rules[i];
            var metric = string.IsNullOrWhiteSpace(rule.Metric) ? ownMetric : rule.Metric.Trim();
            if (metric is not null && read(metric) is { } value && double.IsFinite(value) && rule.Matches(value))
                return rule;
        }
        return null;
    }

    public static ThresholdRule? Select(IReadOnlyList<ThresholdRule> rules, string? ownMetric, MetricStore store) =>
        Select(rules, ownMetric, id => store.TryGet(id, out var sample) ? sample.Value : null);
}

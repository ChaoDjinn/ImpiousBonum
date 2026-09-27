namespace ImpiousBonum.Core.Metrics;

/// <summary>The latest reading for a metric. A metric with no reading yet (or a sensor that is unavailable) has neither value set.</summary>
public readonly record struct MetricSample(MetricDefinition Definition, double? Value, string? Text)
{
    public bool HasValue => Value is not null || Text is not null;
}

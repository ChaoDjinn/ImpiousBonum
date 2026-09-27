using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>A source of metrics. Each provider is sampled on its own loop, so a slow one (ping, a sleeping disk) never holds up the rest.</summary>
public interface IMetricProvider : IDisposable
{
    /// <summary>How often <see cref="SampleAsync"/> is called.</summary>
    TimeSpan Interval { get; }

    /// <summary>Registers the metrics this provider supplies. Throwing here disables the provider.</summary>
    void Initialize(MetricStore store);

    ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken);
}

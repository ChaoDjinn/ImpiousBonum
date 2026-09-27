using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Registers the metrics that need the elevated sensor host (temperatures, FPS) so layouts can reference them today.
/// They show as "—" until the sensor host exists and fills them in.
/// </summary>
public sealed class SensorPlaceholderProvider : IMetricProvider
{
    public const string CpuTemperature = "cpu.temp";
    public const string GpuTemperature = "gpu.temp";
    public const string FramesPerSecond = "fps";

    public TimeSpan Interval => Timeout.InfiniteTimeSpan;

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(CpuTemperature, "CPU temperature", "Sensors", MetricUnit.Celsius));
        store.Register(new MetricDefinition(GpuTemperature, "GPU temperature", "Sensors", MetricUnit.Celsius));
        store.Register(new MetricDefinition(FramesPerSecond, "Frames per second (foreground app)", "Sensors", MetricUnit.FramesPerSecond));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}

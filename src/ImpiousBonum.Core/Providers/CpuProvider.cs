using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>Total CPU load from <c>GetSystemTimes</c>. Cheap: one syscall per sample, no counters.</summary>
public sealed class CpuProvider : IMetricProvider
{
    public const string Load = "cpu.load";
    public const string Threads = "cpu.threads";

    private long _lastIdle;
    private long _lastTotal;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Load, "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(Threads, "CPU logical processors", "CPU", MetricUnit.None));
        store.Set(Threads, Environment.ProcessorCount);
        Kernel32.GetSystemTimes(out _lastIdle, out var kernel, out var user);
        _lastTotal = kernel + user;
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        if (Kernel32.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            // Kernel time includes idle time.
            var total = kernel + user;
            var totalDelta = total - _lastTotal;
            var idleDelta = idle - _lastIdle;
            if (totalDelta > 0)
                store.Set(Load, Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100));

            _lastIdle = idle;
            _lastTotal = total;
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }
}

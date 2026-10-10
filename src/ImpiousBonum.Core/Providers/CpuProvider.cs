using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Total CPU load from <c>GetSystemTimes</c> (one syscall per sample), plus the effective clock speed and each logical
/// processor's load from performance counters. If the counters aren't available, total load still works.
/// </summary>
public sealed class CpuProvider : IMetricProvider
{
    public const string Load = "cpu.load";
    public const string Threads = "cpu.threads";
    public const string Clock = "cpu.clock";

    /// <summary>One logical processor's load, numbered from 0 as in Task Manager.</summary>
    public static string CoreLoad(int index) => $"cpu.core.{index}.load";

    private long _lastIdle;
    private long _lastTotal;
    private PdhQuery? _query;
    private int _frequencyCounter;
    private int _performanceCounter;
    private int _coreCounter;
    private readonly HashSet<int> _cores = [];

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Load, "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(Threads, "CPU logical processors", "CPU", MetricUnit.None));
        store.Set(Threads, Environment.ProcessorCount);
        Kernel32.GetSystemTimes(out _lastIdle, out var kernel, out var user);
        _lastTotal = kernel + user;

        try
        {
            _query = new PdhQuery();
            // Effective clock = base frequency × "% Processor Performance" (over 100 while boosting), as Task Manager shows it.
            _frequencyCounter = _query.Add(@"\Processor Information(*)\Processor Frequency");
            _performanceCounter = _query.Add(@"\Processor Information(*)\% Processor Performance");
            _coreCounter = _query.Add(@"\Processor(*)\% Processor Time");
            store.Register(new MetricDefinition(Clock, "CPU clock (effective)", "CPU", MetricUnit.Megahertz));
            // These are rates, so they need a baseline collection before the first real read.
            _query.Collect();
        }
        catch (InvalidOperationException)
        {
            _query?.Dispose();
            _query = null;
        }
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

        if (_query is not null)
            SampleCounters(store, _query);

        return ValueTask.CompletedTask;
    }

    private void SampleCounters(MetricStore store, PdhQuery query)
    {
        query.Collect();

        var frequency = Total(query.Read(_frequencyCounter));
        var performance = Total(query.Read(_performanceCounter));
        store.Set(Clock, frequency is > 0 && performance is { } p ? frequency * p / 100 : null);

        foreach (var (instance, value) in query.Read(_coreCounter))
        {
            if (ParseCore(instance) is not { } index)
                continue;
            if (_cores.Add(index))
                store.Register(new MetricDefinition(CoreLoad(index), $"CPU {index} load", "CPU cores", MetricUnit.Percent, 100));
            store.Set(CoreLoad(index), Math.Clamp(value, 0, 100));
        }
    }

    /// <summary>The "_Total" instance's value.</summary>
    private static double? Total(List<(string Instance, double Value)> values) =>
        values.Where(v => v.Instance == "_Total").Select(v => (double?)v.Value).FirstOrDefault();

    /// <summary>"\Processor(*)" instances are logical processor numbers ("0", "1", …) plus "_Total".</summary>
    internal static int? ParseCore(string instance) => int.TryParse(instance, out var index) && index >= 0 ? index : null;

    public void Dispose() => _query?.Dispose();
}

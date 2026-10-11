using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>One app's share: all its processes together (Chrome's thirty count as one).</summary>
/// <param name="Cpu">Percent of the whole CPU, like Task Manager's CPU column.</param>
/// <param name="Memory">Private working set in bytes, like Task Manager's Memory column.</param>
public sealed record AppUsage(string Name, int Processes, double Cpu, long Memory);

/// <summary>
/// What's using the CPU and memory, by app, every two seconds: the five busiest by each as metrics
/// (<c>top.cpu.1</c> with <c>top.cpu.1.name</c>, … <c>top.memory.5</c>), and the whole list as an attachment for the
/// processes widget.
/// </summary>
/// <param name="read">Reads every process; <see cref="ProcessList"/> unless a test swaps it.</param>
public sealed class ProcessProvider(Func<IReadOnlyList<ProcessReading>>? read = null, TimeProvider? time = null) : IMetricProvider
{
    /// <summary>The apps (an <c>IReadOnlyList&lt;AppUsage&gt;</c>, busiest CPU first), as a <see cref="MetricStore"/> attachment.</summary>
    public const string Apps = "top.apps";

    public const int Ranked = 5;

    public static string Cpu(int rank) => $"top.cpu.{rank}";

    public static string CpuName(int rank) => $"top.cpu.{rank}.name";

    public static string Memory(int rank) => $"top.memory.{rank}";

    public static string MemoryName(int rank) => $"top.memory.{rank}.name";

    private readonly Func<IReadOnlyList<ProcessReading>> _read = read ?? ProcessList.Read;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private IReadOnlyList<ProcessReading>? _before;
    private DateTimeOffset _beforeAt;

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Initialize(MetricStore store)
    {
        for (var rank = 1; rank <= Ranked; rank++)
        {
            store.Register(new MetricDefinition(Cpu(rank), $"Busiest app #{rank}: CPU", "Processes", MetricUnit.Percent, 100));
            store.Register(new MetricDefinition(CpuName(rank), $"Busiest app #{rank}: name", "Processes", MetricUnit.Text));
            store.Register(new MetricDefinition(Memory(rank), $"Biggest app #{rank}: memory", "Processes", MetricUnit.Bytes));
            store.Register(new MetricDefinition(MemoryName(rank), $"Biggest app #{rank}: name", "Processes", MetricUnit.Text));
        }
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var readings = _read();
        if (_before is not null)
        {
            var apps = Usage(_before, readings, now - _beforeAt, Environment.ProcessorCount);
            store.SetAttachment(Apps, apps);
            var byCpu = apps.OrderByDescending(a => a.Cpu).ToList();
            var byMemory = apps.OrderByDescending(a => a.Memory).ToList();
            for (var rank = 1; rank <= Ranked; rank++)
            {
                var busy = rank <= byCpu.Count ? byCpu[rank - 1] : null;
                var big = rank <= byMemory.Count ? byMemory[rank - 1] : null;
                store.Set(Cpu(rank), busy?.Cpu);
                store.SetText(CpuName(rank), busy?.Name);
                store.Set(Memory(rank), big?.Memory);
                store.SetText(MemoryName(rank), big?.Name);
            }
        }
        _before = readings;
        _beforeAt = now;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Each app's CPU over the time between two readings, and its memory now, grouped by program name. A process that
    /// started in between counts from the next reading.
    /// </summary>
    internal static IReadOnlyList<AppUsage> Usage(IReadOnlyList<ProcessReading> before, IReadOnlyList<ProcessReading> now, TimeSpan elapsed, int cores)
    {
        var earlier = new Dictionary<int, ProcessReading>();
        foreach (var reading in before)
            earlier[reading.ProcessId] = reading;
        var available = elapsed.Ticks * (double)Math.Max(1, cores);

        return now
            .Select(p =>
            {
                // The same id with a different name is a new process that reused it.
                var used = earlier.TryGetValue(p.ProcessId, out var was) && was.Name == p.Name ? Math.Max(0, p.CpuTime - was.CpuTime) : 0;
                return (Name: DisplayName(p.Name), Cpu: available > 0 ? used / available * 100 : 0, p.PrivateWorkingSet);
            })
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppUsage(g.First().Name, g.Count(), Math.Min(100, g.Sum(p => p.Cpu)), g.Sum(p => p.PrivateWorkingSet)))
            .OrderByDescending(a => a.Cpu)
            .ThenByDescending(a => a.Memory)
            .ToList();
    }

    /// <summary>"chrome.exe" → "chrome".</summary>
    internal static string DisplayName(string imageName) =>
        imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;

    public void Dispose()
    {
    }
}

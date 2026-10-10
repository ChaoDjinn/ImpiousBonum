using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

public sealed class MemoryProvider : IMetricProvider
{
    public const string Used = "mem.used";
    public const string Total = "mem.total";
    public const string Available = "mem.available";
    public const string Load = "mem.load";

    // Committed memory: what programs have been promised, backed by RAM plus the page file. Windows' limit is the total.
    public const string CommitUsed = "mem.commit.used";
    public const string CommitTotal = "mem.commit.total";
    public const string CommitLoad = "mem.commit.load";

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        var status = Kernel32.MemoryStatusEx.Create();
        Kernel32.GlobalMemoryStatusEx(ref status);

        store.Register(new MetricDefinition(Used, "RAM used", "Memory", MetricUnit.Bytes, status.TotalPhys));
        store.Register(new MetricDefinition(Total, "RAM total", "Memory", MetricUnit.Bytes));
        store.Register(new MetricDefinition(Available, "RAM available", "Memory", MetricUnit.Bytes, status.TotalPhys));
        store.Register(new MetricDefinition(Load, "RAM load", "Memory", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(CommitUsed, "Committed memory", "Memory", MetricUnit.Bytes, status.TotalPageFile));
        store.Register(new MetricDefinition(CommitTotal, "Commit limit (RAM + page file)", "Memory", MetricUnit.Bytes));
        store.Register(new MetricDefinition(CommitLoad, "Committed memory %", "Memory", MetricUnit.Percent, 100));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var status = Kernel32.MemoryStatusEx.Create();
        if (Kernel32.GlobalMemoryStatusEx(ref status))
        {
            store.Set(Total, status.TotalPhys);
            store.Set(Available, status.AvailPhys);
            store.Set(Used, status.TotalPhys - status.AvailPhys);
            store.Set(Load, status.TotalPhys == 0 ? null : 100.0 * (status.TotalPhys - status.AvailPhys) / status.TotalPhys);
            // In MEMORYSTATUSEX the "page file" fields are the commit limit and what's left of it.
            store.Set(CommitTotal, status.TotalPageFile);
            store.Set(CommitUsed, status.TotalPageFile - status.AvailPageFile);
            store.Set(CommitLoad, status.TotalPageFile == 0 ? null : 100.0 * (status.TotalPageFile - status.AvailPageFile) / status.TotalPageFile);
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }
}

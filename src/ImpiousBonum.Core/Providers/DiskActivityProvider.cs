using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// How busy each drive is: read and write speed and the share of time it was active, per drive letter
/// (<c>disk.C.read</c>) and across all drives (<c>disk.read</c>). From the same performance counters as Task Manager.
/// </summary>
public sealed class DiskActivityProvider : IMetricProvider
{
    public const string ReadTotal = "disk.read";
    public const string WriteTotal = "disk.write";

    public static string Read(string letter) => $"disk.{letter}.read";
    public static string Write(string letter) => $"disk.{letter}.write";
    public static string Active(string letter) => $"disk.{letter}.active";

    private PdhQuery? _query;
    private int _readCounter;
    private int _writeCounter;
    private int _idleCounter;
    private HashSet<string> _known = [];

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        _query = new PdhQuery();
        _readCounter = _query.Add(@"\LogicalDisk(*)\Disk Read Bytes/sec");
        _writeCounter = _query.Add(@"\LogicalDisk(*)\Disk Write Bytes/sec");
        _idleCounter = _query.Add(@"\LogicalDisk(*)\% Idle Time");
        store.Register(new MetricDefinition(ReadTotal, "Disk read (all drives)", "Disk", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(WriteTotal, "Disk write (all drives)", "Disk", MetricUnit.BytesPerSecond));
        // These are rates, so they need a baseline collection before the first real read.
        _query.Collect();
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        if (_query is null)
            return ValueTask.CompletedTask;
        _query.Collect();

        var reads = _query.Read(_readCounter);
        var writes = _query.Read(_writeCounter);
        var idle = _query.Read(_idleCounter).ToDictionary(v => v.Instance, v => v.Value, StringComparer.OrdinalIgnoreCase);

        store.Set(ReadTotal, reads.Where(r => r.Instance == "_Total").Select(r => (double?)r.Value).FirstOrDefault());
        store.Set(WriteTotal, writes.Where(w => w.Instance == "_Total").Select(w => (double?)w.Value).FirstOrDefault());

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writesByLetter = writes.Where(w => Letter(w.Instance) is not null).ToDictionary(w => Letter(w.Instance)!, w => w.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in reads)
        {
            if (Letter(instance) is not { } letter)
                continue;
            seen.Add(letter);
            if (!_known.Contains(letter))
            {
                var category = $"Disk {letter}:";
                store.Register(new MetricDefinition(Read(letter), $"{letter}: read", category, MetricUnit.BytesPerSecond));
                store.Register(new MetricDefinition(Write(letter), $"{letter}: write", category, MetricUnit.BytesPerSecond));
                store.Register(new MetricDefinition(Active(letter), $"{letter}: active time", category, MetricUnit.Percent, 100));
            }
            store.Set(Read(letter), value);
            store.Set(Write(letter), writesByLetter.GetValueOrDefault(letter));
            store.Set(Active(letter), idle.TryGetValue(instance, out var idlePercent) ? Math.Clamp(100 - idlePercent, 0, 100) : null);
        }

        foreach (var gone in _known.Except(seen))
        {
            store.Unregister(Read(gone));
            store.Unregister(Write(gone));
            store.Unregister(Active(gone));
        }
        _known = seen;
        return ValueTask.CompletedTask;
    }

    /// <summary>"C:" → "C". Other instances ("_Total", "HarddiskVolume5" for volumes without a letter) → null.</summary>
    internal static string? Letter(string instance) =>
        instance.Length == 2 && instance[1] == ':' && char.IsAsciiLetter(instance[0]) ? instance[..1].ToUpperInvariant() : null;

    public void Dispose() => _query?.Dispose();
}

using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// GPU load (overall and per engine) and memory use from the same performance counters Task Manager uses. No admin rights
/// or vendor SDKs needed. Reports the adapter with the most dedicated memory (the discrete card on systems that also have an iGPU).
/// </summary>
public sealed class GpuProvider : IMetricProvider
{
    public const string Name = "gpu.name";
    public const string Load = "gpu.load";
    public const string VramUsed = "gpu.vram.used";
    public const string VramTotal = "gpu.vram.total";
    public const string VramFree = "gpu.vram.free";
    public const string VramUsedPercent = "gpu.vram.usedPct";
    public const string SharedUsed = "gpu.shared.used";
    public const string Load3D = "gpu.load.3d";
    public const string LoadDecode = "gpu.load.decode";
    public const string LoadEncode = "gpu.load.encode";
    public const string LoadCompute = "gpu.load.compute";

    /// <summary>
    /// Each process's share of the GPU, by process id, as Task Manager's GPU column has it, as an
    /// <c>IReadOnlyDictionary&lt;int, double&gt;</c> attachment: for <see cref="ProcessProvider"/>'s apps.
    /// </summary>
    public const string Processes = "gpu.processes";

    private PdhQuery? _query;
    private int _engineCounter;
    private int _memoryCounter;
    private int _sharedCounter;
    private string _luid = string.Empty;
    private double _total;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        var adapter = Dxgi.EnumerateAdapters().MaxBy(a => a.DedicatedVideoMemory)
            ?? throw new InvalidOperationException("No hardware graphics adapter found.");
        _luid = adapter.Luid;

        store.Register(new MetricDefinition(Name, "GPU name", "GPU", MetricUnit.Text));
        store.Register(new MetricDefinition(Load, "GPU load", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(VramUsed, "GPU memory used", "GPU", MetricUnit.Bytes, adapter.DedicatedVideoMemory));
        store.Register(new MetricDefinition(VramTotal, "GPU memory total", "GPU", MetricUnit.Bytes));
        store.Register(new MetricDefinition(VramFree, "GPU memory free", "GPU", MetricUnit.Bytes, adapter.DedicatedVideoMemory));
        store.Register(new MetricDefinition(VramUsedPercent, "GPU memory used %", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(SharedUsed, "GPU shared memory used (from RAM)", "GPU", MetricUnit.Bytes));
        store.Register(new MetricDefinition(Load3D, "GPU 3D load", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(LoadDecode, "GPU video decode load", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(LoadEncode, "GPU video encode load", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(LoadCompute, "GPU compute load", "GPU", MetricUnit.Percent, 100));
        store.SetText(Name, adapter.Name);
        store.Set(VramTotal, adapter.DedicatedVideoMemory);
        _total = adapter.DedicatedVideoMemory;

        _query = new PdhQuery();
        _engineCounter = _query.Add(@"\GPU Engine(*)\Utilization Percentage");
        _memoryCounter = _query.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _sharedCounter = _query.Add(@"\GPU Adapter Memory(*)\Shared Usage");
        // Utilisation is a rate, so it needs a baseline collection before the first real read.
        _query.Collect();
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        if (_query is null)
            return ValueTask.CompletedTask;

        _query.Collect();

        // Instances look like "pid_1234_luid_0x00000000_0x0000D1B5_phys_0_eng_3_engtype_3D".
        // Task Manager sums per engine type across processes and reports the busiest engine type.
        var engines = _query.Read(_engineCounter).Where(e => e.Instance.Contains(_luid, StringComparison.OrdinalIgnoreCase)).ToList();
        var perEngineType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in engines)
        {
            var engineType = EngineType(instance);
            perEngineType[engineType] = perEngineType.GetValueOrDefault(engineType) + value;
        }
        store.SetAttachment(Processes, PerProcess(engines));
        store.Set(Load, perEngineType.Count == 0 ? 0 : Math.Clamp(perEngineType.Values.Max(), 0, 100));
        foreach (var (id, group) in new[] { (Load3D, EngineGroup.ThreeD), (LoadDecode, EngineGroup.Decode), (LoadEncode, EngineGroup.Encode), (LoadCompute, EngineGroup.Compute) })
        {
            var loads = perEngineType.Where(e => Classify(e.Key) == group).Select(e => e.Value).ToList();
            store.Set(id, loads.Count == 0 ? 0 : Math.Clamp(loads.Max(), 0, 100));
        }

        // Instances look like "luid_0x00000000_0x0000D1B5_phys_0".
        var used = SumForAdapter(_memoryCounter);
        store.Set(VramUsed, used);
        store.Set(VramFree, used is { } u && _total > 0 ? Math.Max(0, _total - u) : null);
        store.Set(VramUsedPercent, used is { } v && _total > 0 ? Math.Clamp(100 * v / _total, 0, 100) : null);
        store.Set(SharedUsed, SumForAdapter(_sharedCounter));

        return ValueTask.CompletedTask;
    }

    private double? SumForAdapter(int counter)
    {
        double? sum = null;
        foreach (var (instance, value) in _query!.Read(counter))
        {
            if (instance.Contains(_luid, StringComparison.OrdinalIgnoreCase))
                sum = (sum ?? 0) + value;
        }
        return sum;
    }

    /// <summary>"…_eng_3_engtype_3D" → "3D"; empty when the instance doesn't say.</summary>
    private static string EngineType(string instance)
    {
        var typeIndex = instance.LastIndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        return typeIndex < 0 ? string.Empty : instance[(typeIndex + "engtype_".Length)..];
    }

    /// <summary>
    /// Each process's GPU share from its engines' utilisation, as Task Manager works out its GPU column: the process's
    /// busiest engine type, adding up its engines of that type. Processes using none are left out.
    /// </summary>
    internal static IReadOnlyDictionary<int, double> PerProcess(IEnumerable<(string Instance, double Value)> engines)
    {
        var loads = new Dictionary<(int Process, string Type), double>();
        foreach (var (instance, value) in engines)
        {
            if (ProcessOf(instance) is not { } process || !(value > 0))
                continue;
            var key = (process, EngineType(instance).ToUpperInvariant());
            loads[key] = loads.GetValueOrDefault(key) + value;
        }
        return loads
            .GroupBy(l => l.Key.Process)
            .ToDictionary(g => g.Key, g => Math.Clamp(g.Max(l => l.Value), 0, 100));
    }

    /// <summary>"pid_1234_luid_…" → 1234; null for an instance without one.</summary>
    internal static int? ProcessOf(string instance)
    {
        if (!instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
            return null;
        var end = instance.IndexOf('_', 4);
        return end > 4 && int.TryParse(instance.AsSpan(4, end - 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    public enum EngineGroup
    {
        Other,
        ThreeD,
        Decode,
        Encode,
        Compute,
    }

    /// <summary>
    /// Which group an engine type belongs to. Names vary by vendor and driver: "3D", "VideoDecode", "VideoEncode",
    /// "Compute_0", "Cuda", "Graphics_1" and so on.
    /// </summary>
    internal static EngineGroup Classify(string engineType) =>
        engineType.Equals("3D", StringComparison.OrdinalIgnoreCase) ? EngineGroup.ThreeD
        : engineType.Contains("Decode", StringComparison.OrdinalIgnoreCase) ? EngineGroup.Decode
        : engineType.Contains("Encode", StringComparison.OrdinalIgnoreCase) ? EngineGroup.Encode
        : engineType.StartsWith("Compute", StringComparison.OrdinalIgnoreCase) || engineType.StartsWith("Cuda", StringComparison.OrdinalIgnoreCase) ? EngineGroup.Compute
        : EngineGroup.Other;

    public void Dispose() => _query?.Dispose();
}

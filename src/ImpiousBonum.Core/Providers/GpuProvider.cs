using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// GPU load and VRAM use from the same performance counters Task Manager uses. No admin rights or vendor SDKs needed.
/// Reports the adapter with the most dedicated memory (the discrete card on systems that also have an iGPU).
/// </summary>
public sealed class GpuProvider : IMetricProvider
{
    public const string Name = "gpu.name";
    public const string Load = "gpu.load";
    public const string VramUsed = "gpu.vram.used";
    public const string VramTotal = "gpu.vram.total";

    private PdhQuery? _query;
    private int _engineCounter;
    private int _memoryCounter;
    private string _luid = string.Empty;

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
        store.SetText(Name, adapter.Name);
        store.Set(VramTotal, adapter.DedicatedVideoMemory);

        _query = new PdhQuery();
        _engineCounter = _query.Add(@"\GPU Engine(*)\Utilization Percentage");
        _memoryCounter = _query.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
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
        var perEngineType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in _query.Read(_engineCounter))
        {
            if (!instance.Contains(_luid, StringComparison.OrdinalIgnoreCase))
                continue;
            var typeIndex = instance.LastIndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            var engineType = typeIndex < 0 ? string.Empty : instance[(typeIndex + "engtype_".Length)..];
            perEngineType[engineType] = perEngineType.GetValueOrDefault(engineType) + value;
        }
        store.Set(Load, perEngineType.Count == 0 ? 0 : Math.Clamp(perEngineType.Values.Max(), 0, 100));

        // Instances look like "luid_0x00000000_0x0000D1B5_phys_0".
        double? used = null;
        foreach (var (instance, value) in _query.Read(_memoryCounter))
        {
            if (instance.Contains(_luid, StringComparison.OrdinalIgnoreCase))
                used = (used ?? 0) + value;
        }
        store.Set(VramUsed, used);

        return ValueTask.CompletedTask;
    }

    public void Dispose() => _query?.Dispose();
}

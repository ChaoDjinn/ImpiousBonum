using System.Security.Principal;
using System.ServiceProcess;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace ImpiousBonum.Sensors;

/// <summary>
/// Wraps LibreHardwareMonitor: updates the hardware, describes every sensor as a metric (<c>hw/amdcpu/0/temperature/2</c>)
/// and resolves the friendly aliases (<c>cpu.temp</c>, <c>gpu.temp</c>, …) to the best matching sensor on this machine.
/// </summary>
internal sealed class SensorCollector : IDisposable
{
    // Preferred sensor names, best first. Ryzen reports Tctl/Tdie; Intel reports CPU Package.
    private static readonly string[] CpuTemperatureNames = ["Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tctl)", "Tctl", "Tdie", "Core Max", "Core Average"];
    private static readonly string[] CpuPowerNames = ["Package", "CPU Package"];
    private static readonly string[] GpuPowerNames = ["GPU Package", "GPU Power", "GPU Core"];

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
        IsPsuEnabled = true,
    };

    private List<Tracked> _tracked = [];
    private Dictionary<string, ISensor> _aliases = [];
    private string _signature = string.Empty;

    public IReadOnlyList<SensorInfo> Catalog { get; private set; } = [];

    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool IsPawnIoRunning { get; } = CheckPawnIo();

    /// <summary>Low-level CPU and motherboard sensors need both admin rights and the PawnIO driver.</summary>
    public bool HasLowLevelAccess => IsElevated && IsPawnIoRunning;

    public string Status =>
        !IsElevated ? "Not running as admin: CPU temperature unavailable"
        : !IsPawnIoRunning ? "PawnIO driver not installed: CPU temperature unavailable"
        : !_aliases.ContainsKey(SensorAliases.CpuTemperature) ? "Sensors running (no CPU temperature sensor found)"
        : "Sensors running";

    public void Open() => _computer.Open();

    /// <summary>Refreshes every sensor. Returns true when the set of sensors changed and <see cref="Catalog"/> was rebuilt.</summary>
    public bool Update()
    {
        foreach (var hardware in _computer.Hardware)
            UpdateRecursive(hardware);

        var sensors = AllSensors().Where(s => !s.IsDefaultHidden).ToList();
        var signature = string.Join('|', sensors.Select(s => s.Identifier.ToString()));
        if (signature == _signature)
            return false;

        _signature = signature;
        _tracked = sensors.Select(Track).ToList();
        _aliases = ResolveAliases();
        Catalog = SensorAliases.All.Concat(_tracked.Select(t => t.Info)).ToList();
        return true;
    }

    public Dictionary<string, double?> ReadValues()
    {
        var values = new Dictionary<string, double?>(_tracked.Count + _aliases.Count);
        foreach (var tracked in _tracked)
            values[tracked.Info.Id] = tracked.Sensor.Value is float v ? v * tracked.Scale : null;

        // Aliases are always sent (null when unresolved) so the dashboard shows "—" rather than a stale reading.
        foreach (var alias in SensorAliases.All)
            values[alias.Id] = _aliases.TryGetValue(alias.Id, out var sensor) && sensor.Value is float v ? v : null;

        // The driver reports 0 FPS when nothing is running fullscreen; show that as no reading.
        if (values[SensorAliases.FramesPerSecond] is <= 0)
            values[SensorAliases.FramesPerSecond] = null;

        return values;
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private IEnumerable<ISensor> AllSensors()
    {
        static IEnumerable<ISensor> Walk(IHardware hardware) =>
            hardware.Sensors.Concat(hardware.SubHardware.SelectMany(Walk));
        return _computer.Hardware.SelectMany(Walk);
    }

    private static Tracked Track(ISensor sensor)
    {
        var (unit, scale, max) = sensor.SensorType switch
        {
            SensorType.Temperature => (MetricUnit.Celsius, 1.0, (double?)null),
            SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => (MetricUnit.Percent, 1.0, 100.0),
            SensorType.Clock => (MetricUnit.Megahertz, 1.0, null),
            SensorType.Fan => (MetricUnit.Rpm, 1.0, null),
            SensorType.Voltage => (MetricUnit.Volts, 1.0, null),
            SensorType.Power => (MetricUnit.Watts, 1.0, null),
            // LHM reports Data in GB and SmallData in MB; normalise to bytes so formatting picks the unit.
            SensorType.Data => (MetricUnit.Bytes, 1024.0 * 1024 * 1024, null),
            SensorType.SmallData => (MetricUnit.Bytes, 1024.0 * 1024, null),
            SensorType.Throughput => (MetricUnit.BytesPerSecond, 1.0, null),
            _ => (MetricUnit.None, 1.0, null),
        };

        var hardware = sensor.Hardware;
        var category = hardware.Parent is { } parent ? $"{parent.Name} / {hardware.Name}" : hardware.Name;
        var info = new SensorInfo("hw" + sensor.Identifier, $"{sensor.Name} ({sensor.SensorType})", category, unit, max);
        return new Tracked(sensor, info, scale);
    }

    private Dictionary<string, ISensor> ResolveAliases()
    {
        var aliases = new Dictionary<string, ISensor>(StringComparer.OrdinalIgnoreCase);

        void Add(string alias, ISensor? sensor)
        {
            if (sensor is not null)
                aliases[alias] = sensor;
        }

        // Without low-level access the CPU sensors exist but read 0, which would look like a real (and alarming) value.
        var cpu = HasLowLevelAccess ? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu) : null;
        if (cpu is not null)
        {
            var temps = Sensors(cpu, SensorType.Temperature);
            Add(SensorAliases.CpuTemperature, ByName(temps, CpuTemperatureNames) ?? temps.MaxBy(s => s.Value ?? float.MinValue));
            Add(SensorAliases.CpuPower, ByName(Sensors(cpu, SensorType.Power), CpuPowerNames));
        }

        var gpu = PickGpu();
        if (gpu is not null)
        {
            Add(SensorAliases.GpuTemperature, ByName(Sensors(gpu, SensorType.Temperature), ["GPU Core"]) ?? Sensors(gpu, SensorType.Temperature).FirstOrDefault());
            Add(SensorAliases.GpuHotSpot, ByName(Sensors(gpu, SensorType.Temperature), ["GPU Hot Spot"]));
            Add(SensorAliases.GpuPower, ByName(Sensors(gpu, SensorType.Power), GpuPowerNames));
            Add(SensorAliases.GpuFan, Sensors(gpu, SensorType.Fan).FirstOrDefault());
            // AMD's driver measures the frame rate of fullscreen apps itself. Other vendors will need ETW (later).
            Add(SensorAliases.FramesPerSecond, ByName(Sensors(gpu, SensorType.Factor), ["Fullscreen FPS"]));
        }

        return aliases;
    }

    /// <summary>The discrete card: the GPU with the most memory, so an integrated GPU alongside it is ignored.</summary>
    private IHardware? PickGpu() =>
        _computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
            .OrderByDescending(h => ByName(Sensors(h, SensorType.SmallData), ["GPU Memory Total"])?.Value ?? 0)
            .ThenBy(h => h.HardwareType == HardwareType.GpuIntel)
            .FirstOrDefault();

    private static List<ISensor> Sensors(IHardware hardware, SensorType type) =>
        hardware.Sensors.Where(s => s.SensorType == type).ToList();

    private static ISensor? ByName(IEnumerable<ISensor> sensors, IEnumerable<string> names)
    {
        var list = sensors.ToList();
        return names.Select(n => list.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(s => s is not null);
    }

    private static bool CheckPawnIo()
    {
        try
        {
            using var service = new ServiceController("PawnIO");
            return service.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose() => _computer.Close();

    private sealed record Tracked(ISensor Sensor, SensorInfo Info, double Scale);
}

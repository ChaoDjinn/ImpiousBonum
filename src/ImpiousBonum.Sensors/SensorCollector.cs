using System.Security.Principal;
using System.ServiceProcess;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sensors;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;

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
    private List<(string Letter, StorageDevice Device)> _drives = [];
    private string _signature = string.Empty;

    public IReadOnlyList<SensorInfo> Catalog { get; private set; } = [];

    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>The PawnIO driver's service state, or null when it isn't installed.</summary>
    public ServiceControllerStatus? PawnIo { get; private set; } = CheckPawnIo();

    public bool IsPawnIoRunning => PawnIo == ServiceControllerStatus.Running;

    /// <summary>Low-level CPU and motherboard sensors need both admin rights and the PawnIO driver.</summary>
    public bool HasLowLevelAccess => IsElevated && IsPawnIoRunning;

    public string Status =>
        !IsElevated ? "Not running as admin: CPU temperature unavailable"
        : PawnIo is null ? "PawnIO driver not installed: CPU temperature unavailable"
        : !IsPawnIoRunning ? $"PawnIO driver installed but not running ({PawnIo}): CPU temperature unavailable"
        : !_aliases.ContainsKey(SensorAliases.CpuTemperature) ? "Sensors running (no CPU temperature sensor found)"
        : "Sensors running";

    public void Open() => _computer.Open();

    /// <summary>
    /// The service starts at boot and can get there before the PawnIO driver has loaded (or the driver can be
    /// reinstalled while it runs). Without this the CPU sensors would stay unavailable until the service restarted.
    /// When PawnIO has started since the last check, rebuilds the hardware so the CPU sensors are created with it, and
    /// returns true; the next <see cref="Update"/> then rebuilds the catalog.
    /// </summary>
    public bool RecheckPawnIo()
    {
        if (!IsElevated || IsPawnIoRunning)
            return false;

        PawnIo = CheckPawnIo();
        if (!IsPawnIoRunning)
            return false;

        _computer.Reset();
        _signature = string.Empty;
        return true;
    }

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
        _drives = FindDrives();
        Catalog = SensorAliases.All.Concat(_tracked.Select(t => t.Info)).Concat(_drives.SelectMany(d => DriveInfos(d.Letter))).ToList();
        return true;
    }

    public Dictionary<string, double?> ReadValues()
    {
        var values = new Dictionary<string, double?>(_tracked.Count + _aliases.Count);
        foreach (var tracked in _tracked)
            values[tracked.Info.Id] = tracked.Sensor.Value is float v ? v * tracked.Scale : null;

        // Aliases are always sent (null when unresolved) so the dashboard shows "—" rather than a stale reading.
        foreach (var alias in SensorAliases.All.Where(a => !SensorAliases.IsComputedByDashboard(a.Id)))
            values[alias.Id] = _aliases.TryGetValue(alias.Id, out var sensor) && sensor.Value is float v ? v : null;

        foreach (var (letter, device) in _drives)
        {
            var smart = Smart(device);
            values[DriveMetric(letter, "life")] = smart?.Life is >= 0 and <= 100 and var life ? life : null;
            values[DriveMetric(letter, "temp")] = smart?.Temperature is > 0 and var temp ? temp : null;
            values[DriveMetric(letter, "hours")] = smart is null ? null : smart.DetectedPowerOnHours > 0 ? smart.DetectedPowerOnHours : smart.MeasuredPowerOnHours > 0 ? smart.MeasuredPowerOnHours : null;
        }

        return values;
    }

    /// <summary>Per-drive readings that are text: the drive's health status (Good, Caution, Bad) and model.</summary>
    public Dictionary<string, string?> ReadTexts()
    {
        var texts = new Dictionary<string, string?>(_drives.Count * 2);
        foreach (var (letter, device) in _drives)
        {
            texts[DriveMetric(letter, "status")] = Smart(device)?.DiskStatus.ToString();
            texts[DriveMetric(letter, "model")] = device.Storage?.Model?.Trim();
        }
        return texts;
    }

    /// <summary>
    /// Each drive letter and the physical disk it's on, so a disk's health can be shown per drive (<c>disk.C.life</c>).
    /// A disk with several partitions appears once per letter.
    /// </summary>
    private List<(string Letter, StorageDevice Device)> FindDrives()
    {
        var drives = new List<(string, StorageDevice)>();
        foreach (var device in _computer.Hardware.OfType<StorageDevice>())
        {
            try
            {
                foreach (var partition in device.Storage?.Partitions ?? [])
                {
                    if (partition.DriveLetter is char letter && char.IsAsciiLetter(letter))
                        drives.Add((char.ToUpperInvariant(letter).ToString(), device));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                // A disk that can't describe its partitions just doesn't get per-drive health.
            }
        }
        return drives;
    }

    private static IEnumerable<SensorInfo> DriveInfos(string letter)
    {
        var category = $"Disk {letter}:";
        yield return new SensorInfo(DriveMetric(letter, "life"), $"{letter}: life remaining (health)", category, MetricUnit.Percent, 100);
        yield return new SensorInfo(DriveMetric(letter, "temp"), $"{letter}: temperature", category, MetricUnit.Celsius);
        yield return new SensorInfo(DriveMetric(letter, "hours"), $"{letter}: power-on hours", category, MetricUnit.None);
        yield return new SensorInfo(DriveMetric(letter, "status"), $"{letter}: health status", category, MetricUnit.Text);
        yield return new SensorInfo(DriveMetric(letter, "model"), $"{letter}: model", category, MetricUnit.Text);
    }

    private static string DriveMetric(string letter, string name) => $"disk.{letter}.{name}";

    private static DiskInfoToolkit.SmartInfo? Smart(StorageDevice device)
    {
        try
        {
            return device.Storage?.Smart;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            return null;
        }
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

    private static ServiceControllerStatus? CheckPawnIo()
    {
        try
        {
            using var service = new ServiceController("PawnIO");
            return service.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose() => _computer.Close();

    private sealed record Tracked(ISensor Sensor, SensorInfo Info, double Scale);
}

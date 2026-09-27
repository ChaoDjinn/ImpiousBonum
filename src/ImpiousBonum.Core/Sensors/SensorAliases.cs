using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Sensors;

/// <summary>
/// Friendly ids for the sensors people most often want, so layouts don't need hardware-specific ids.
/// The host fills them from whichever sensor best matches on this machine; every raw sensor is also available as <c>hw/...</c>.
/// </summary>
public static class SensorAliases
{
    public const string CpuTemperature = "cpu.temp";
    public const string CpuPower = "cpu.power";
    public const string GpuTemperature = "gpu.temp";
    public const string GpuHotSpot = "gpu.hotspot";
    public const string GpuPower = "gpu.power";
    public const string GpuFan = "gpu.fan";
    public const string FramesPerSecond = "fps";

    public static readonly IReadOnlyList<SensorInfo> All =
    [
        new(CpuTemperature, "CPU temperature", "Sensors", MetricUnit.Celsius),
        new(CpuPower, "CPU package power", "Sensors", MetricUnit.Watts),
        new(GpuTemperature, "GPU temperature", "Sensors", MetricUnit.Celsius),
        new(GpuHotSpot, "GPU hot spot temperature", "Sensors", MetricUnit.Celsius),
        new(GpuPower, "GPU power", "Sensors", MetricUnit.Watts),
        new(GpuFan, "GPU fan speed", "Sensors", MetricUnit.Rpm),
        new(FramesPerSecond, "Frames per second (foreground app)", "Sensors", MetricUnit.FramesPerSecond),
    ];
}

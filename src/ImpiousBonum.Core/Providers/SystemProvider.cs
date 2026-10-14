using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Uptime and the computer's name, and where there's a battery (a laptop's, or a UPS that Windows sees as one) its
/// level, whether it's charging, and the time left.
/// </summary>
public sealed class SystemProvider : IMetricProvider
{
    public const string Uptime = "sys.uptime";
    public const string ComputerName = "sys.name";
    public const string Battery = "sys.battery";
    public const string BatteryStatus = "sys.battery.status";
    public const string BatteryTime = "sys.battery.time";

    private bool _hasBattery;

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Uptime, "Time since Windows started", "System", MetricUnit.Text));
        store.Register(new MetricDefinition(ComputerName, "Computer name", "System", MetricUnit.Text));
        store.SetText(ComputerName, Environment.MachineName);

        // Desktops have no battery; only offer the battery metrics where there is one.
        _hasBattery = Kernel32.GetSystemPowerStatus(out var power) && HasBattery(power.BatteryFlag);
        if (_hasBattery)
        {
            store.Register(new MetricDefinition(Battery, "Battery", "System", MetricUnit.Percent, 100));
            store.Register(new MetricDefinition(BatteryStatus, "Battery status", "System", MetricUnit.Text));
            store.Register(new MetricDefinition(BatteryTime, "Battery time left, e.g. 2h 14m (on battery only)", "System", MetricUnit.Text));
        }
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        store.SetText(Uptime, DurationText.Format(TimeSpan.FromMilliseconds(Environment.TickCount64)));

        if (_hasBattery && Kernel32.GetSystemPowerStatus(out var power))
        {
            store.Set(Battery, power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null);
            store.SetText(BatteryStatus, Describe(power.AcLineStatus, power.BatteryFlag));
            store.SetText(BatteryTime, TimeLeft(power.BatteryLifeTime));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Windows' estimate of the time left on battery; null while plugged in or not worked out yet (0xFFFFFFFF).</summary>
    internal static string? TimeLeft(uint seconds) => seconds == uint.MaxValue ? null : DurationText.Format(TimeSpan.FromSeconds(seconds));

    internal static bool HasBattery(byte batteryFlag) => batteryFlag is not (128 or 255);

    internal static string Describe(byte acLineStatus, byte batteryFlag) =>
        (batteryFlag & 8) != 0 ? "Charging"
        : acLineStatus == 1 ? "Plugged in"
        : acLineStatus == 0 ? "On battery"
        : "Unknown";

    public void Dispose()
    {
    }
}

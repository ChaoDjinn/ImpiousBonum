namespace ImpiousBonum.App.Shell;

/// <summary>
/// Another dashboard besides the main one (tray → Display → More dashboards): a saved layout filling a monitor of its
/// own. Pages, game layouts and the schedule's layouts are the main dashboard's; these always show their layout.
/// </summary>
/// <param name="MonitorId">The monitor's stable id (<see cref="DisplayMonitor.Id"/>).</param>
/// <param name="MonitorDevice">Its device name, e.g. <c>\\.\DISPLAY2</c>, to name it while it isn't connected.</param>
/// <param name="Layout">The saved layout it shows.</param>
public sealed record ExtraDashboard(string MonitorId, string MonitorDevice, string Layout)
{
    /// <summary>
    /// Its monitor, or null while that's unplugged or off: then it hides rather than landing on another screen. Only the
    /// id counts, since device names move between monitors when they're plugged in a different order.
    /// </summary>
    public DisplayMonitor? Resolve(IReadOnlyList<DisplayMonitor> monitors) => monitors.FirstOrDefault(m => m.Id == MonitorId);

    /// <summary>What the tray calls its monitor.</summary>
    public string MonitorName(IReadOnlyList<DisplayMonitor> monitors) =>
        Resolve(monitors)?.DisplayName ?? $"{DisplayMonitor.ShortName(MonitorDevice)} (not connected)";

    public static ExtraDashboard On(DisplayMonitor monitor, string layout) => new(monitor.Id, monitor.Device, layout);

    /// <summary>The monitors with no dashboard on them yet: not the main one's, and not another extra's.</summary>
    public static IReadOnlyList<DisplayMonitor> FreeMonitors(IReadOnlyList<DisplayMonitor> monitors, DisplayMonitor? main, IEnumerable<ExtraDashboard> extras)
    {
        var taken = extras.Select(e => e.Resolve(monitors)?.Id).Append(main?.Id).ToHashSet();
        return monitors.Where(m => !taken.Contains(m.Id)).ToList();
    }

    /// <summary>The layout a new dashboard starts with: one the main dashboard isn't showing, if there is one.</summary>
    public static string FirstLayout(IReadOnlyList<string> layouts, string active) =>
        layouts.FirstOrDefault(l => !string.Equals(l, active, StringComparison.OrdinalIgnoreCase)) ?? active;
}

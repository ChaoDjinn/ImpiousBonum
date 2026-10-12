using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Another dashboard besides the main one (the Screens page): a saved layout filling a monitor of its
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

    /// <summary>
    /// The name of a new dashboard's layout: its monitor's own name, such as "DELL U2720Q", or "Screen" when Windows
    /// doesn't know it or it won't do as a file name, numbered if a layout already has it.
    /// </summary>
    public static string NewLayoutName(DisplayMonitor monitor, IReadOnlyList<string> layouts)
    {
        // Short enough to leave room for a number within the longest name a layout can have.
        var name = monitor.Name is { Length: <= 40 } own && LayoutLibrary.ValidateName(own) is null ? own : "Screen";
        var candidate = name;
        for (var i = 2; layouts.Contains(candidate, StringComparer.OrdinalIgnoreCase); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    /// <summary>
    /// A new dashboard's layout: empty, for widgets of its own rather than a copy of another screen's, the size of its
    /// monitor so nothing is scaled, and in <paramref name="theme"/> (the main dashboard's) so the two look alike.
    /// </summary>
    public static LayoutDocument NewLayout(DisplayMonitor monitor, JsonObject theme) => new()
    {
        Width = monitor.Bounds.Width,
        Height = monitor.Bounds.Height,
        Theme = (JsonObject)theme.DeepClone(),
    };
}

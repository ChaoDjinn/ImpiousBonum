using System.Text.Json;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

public sealed class ExtraDashboardTests
{
    private static readonly DisplayMonitor Main = new(@"\\?\DISPLAY#AAA", @"\\.\DISPLAY1", "Main", new PixelRect(0, 0, 2560, 1440), true);
    private static readonly DisplayMonitor Strip = new(@"\\?\DISPLAY#BBB", @"\\.\DISPLAY2", "Strip", new PixelRect(2560, 0, 1920, 480), false);
    private static readonly DisplayMonitor Square = new(@"\\?\DISPLAY#CCC", @"\\.\DISPLAY3", "Square", new PixelRect(-800, 0, 800, 800), false);
    private static readonly IReadOnlyList<DisplayMonitor> Monitors = [Main, Strip, Square];

    [Fact]
    public void A_dashboard_finds_its_monitor_by_id_only()
    {
        var extra = ExtraDashboard.On(Square, "Clock");
        Assert.Equal(Square, extra.Resolve(Monitors));

        // Plugged in a different order, DISPLAY3 is now another monitor: it waits for its own rather than taking that one.
        var renumbered = new DisplayMonitor(@"\\?\DISPLAY#DDD", @"\\.\DISPLAY3", "Other", new PixelRect(0, 0, 1024, 600), false);
        Assert.Null(extra.Resolve([Main, renumbered]));
        Assert.Equal("DISPLAY3 (not connected)", extra.MonitorName([Main, renumbered]));
        Assert.Equal(Square.DisplayName, extra.MonitorName(Monitors));
    }

    [Fact]
    public void Free_monitors_have_no_dashboard_yet()
    {
        Assert.Equal([Strip, Square], ExtraDashboard.FreeMonitors(Monitors, Main, []));
        Assert.Equal([Square], ExtraDashboard.FreeMonitors(Monitors, Main, [ExtraDashboard.On(Strip, "Gaming")]));
        Assert.Empty(ExtraDashboard.FreeMonitors(Monitors, Main, [ExtraDashboard.On(Strip, "Gaming"), ExtraDashboard.On(Square, "Clock")]));
        // A dashboard whose monitor is unplugged takes up none.
        Assert.Equal([Main, Strip, Square], ExtraDashboard.FreeMonitors(Monitors, null, [new ExtraDashboard("gone", @"\\.\DISPLAY9", "Clock")]));
    }

    [Fact]
    public void A_new_dashboard_starts_with_a_layout_the_main_one_isnt_showing()
    {
        Assert.Equal("Gaming", ExtraDashboard.FirstLayout(["Default", "Gaming"], "default"));
        Assert.Equal("Default", ExtraDashboard.FirstLayout(["Default"], "Default"));
    }

    [Fact]
    public void Extra_dashboards_are_kept_in_settings()
    {
        var settings = new AppSettings { ExtraDashboards = [ExtraDashboard.On(Strip, "Gaming")] };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonDefaults.Options), JsonDefaults.Options)!;
        Assert.Equal(settings.ExtraDashboards, restored.ExtraDashboards);

        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("""{ "extraDashboards": null }""", JsonDefaults.Options)!.ExtraDashboards);
    }
}

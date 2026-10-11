using System.Globalization;
using System.IO;
using System.Text.Json;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Remote;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Shell;

/// <summary>Per-machine settings: which monitor the dashboard lives on and where on it, how its window behaves, which layout it shows, and game layouts.</summary>
public sealed class AppSettings
{
    private List<GameLayoutRule> _gameLayouts = [];
    private List<ScheduleRule> _schedule = [];
    private List<CustomSource> _customData = [];
    private List<AlertRule> _alerts = [];
    private List<string> _pages = [];
    private List<Hotkey> _hotkeys = [];
    private List<CalendarLink> _calendars = [];
    private List<NewsFeed> _newsFeeds = [];
    private LogSettings _logging = LogSettings.Default;

    /// <summary>Stable monitor interface path (survives reboots and display renumbering).</summary>
    public string? MonitorId { get; set; }

    /// <summary>GDI device name such as \\.\DISPLAY7; a fallback when the id can't be matched.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Fill the whole monitor. When false, <see cref="Area"/> is used.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Area relative to the monitor's top-left, in physical pixels.</summary>
    public PixelRect? Area { get; set; }

    /// <summary>When windowed, stops the window being dragged or resized.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>When windowed and locked, clicks pass through to whatever is underneath.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Keep the dashboard above other windows.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>With <see cref="AlwaysOnTop"/>, hide while a fullscreen app is in front on the dashboard's monitor.</summary>
    public bool HideOverFullscreen { get; set; } = true;

    /// <summary>Name of the saved layout to show (a file in the layouts folder), or null for "Default".</summary>
    public string? ActiveLayout { get; set; }

    public string PingHost { get; set; } = "1.1.1.1";

    /// <summary>Games linked to layouts or themes, shown while the game is in front (see <see cref="GameLayoutSwitcher"/>).</summary>
    public List<GameLayoutRule> GameLayouts
    {
        get => _gameLayouts;
        set => _gameLayouts = value ?? [];
    }

    /// <summary>Switch to a game's linked layout automatically. Off leaves the links in place but unused.</summary>
    public bool GameLayoutsEnabled { get; set; } = true;

    /// <summary>Recap each session of a linked game: a notification, a line in sessions.csv, and the session.* metrics.</summary>
    public bool SessionRecaps { get; set; } = true;

    /// <summary>Layouts and themes shown at times of day or with Windows in dark or light mode (see <see cref="ScheduleRule"/>).</summary>
    public List<ScheduleRule> Schedule
    {
        get => _schedule;
        set => _schedule = value ?? [];
    }

    /// <summary>Follow <see cref="Schedule"/>. Off leaves the rules in place but unused.</summary>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>
    /// Your own metrics from web addresses and commands (tray → Custom data…). Replaced as a whole when edited, never
    /// changed in place, since the sampler reads it from another thread.
    /// </summary>
    public List<CustomSource> CustomData
    {
        get => _customData;
        set => _customData = value ?? [];
    }

    /// <summary>Swipe across the dashboard to move between <see cref="Pages"/>. Off by default.</summary>
    public bool SwipePages { get; set; }

    /// <summary>Move to the next page every this many seconds; 0 doesn't rotate.</summary>
    public int RotatePagesSeconds { get; set; }

    /// <summary>The layouts that are pages, or empty for all of them.</summary>
    public List<string> Pages
    {
        get => _pages;
        set => _pages = value ?? [];
    }

    /// <summary>Notifications and flashing banners when a reading is out of bounds for a while (tray → Alerts…).</summary>
    public List<AlertRule> Alerts
    {
        get => _alerts;
        set => _alerts = value ?? [];
    }

    /// <summary>
    /// Calendars to show events from (tray → Calendar…). Replaced as a whole when edited, never changed in place, since
    /// the sampler reads it from another thread.
    /// </summary>
    public List<CalendarLink> Calendars
    {
        get => _calendars;
        set => _calendars = value ?? [];
    }

    /// <summary>Where the layout gallery is (a web folder with an index.json); null for the Impious Bonum repository's.</summary>
    public string? GalleryAddress { get; set; }

    /// <summary>
    /// News feeds whose headlines the news widget shows (tray → News feeds…). Replaced as a whole when edited, never
    /// changed in place, since the sampler reads it from another thread.
    /// </summary>
    public List<NewsFeed> NewsFeeds
    {
        get => _newsFeeds;
        set => _newsFeeds = value ?? [];
    }

    /// <summary>Logging readings to CSV files (tray → Logging…). Off by default.</summary>
    public LogSettings Logging
    {
        get => _logging;
        set => _logging = value ?? LogSettings.Default;
    }

    /// <summary>Key combinations that work from anywhere (tray → Hotkeys…). None by default.</summary>
    public List<Hotkey> Hotkeys
    {
        get => _hotkeys;
        set => _hotkeys = value ?? [];
    }

    /// <summary>Dim the dashboard after this many minutes without keyboard, mouse or touch input; 0 never does.</summary>
    public int AwayDimMinutes { get; set; }

    /// <summary>How far to dim when away, in percent; 0 turns it black.</summary>
    public int AwayBrightness { get; set; } = 20;

    /// <summary>Monitor whose top app the FPS reading follows (a <see cref="DisplayMonitor.Id"/>), or null for the foreground app.</summary>
    public string? FpsMonitorId { get; set; }

    /// <summary>
    /// GPU-accelerated drawing. Off by default: the dashboard redraws once a second, and software rendering
    /// avoids loading the graphics driver into the process, which saves a lot of memory.
    /// </summary>
    public bool HardwareRendering { get; set; }

    /// <summary>Serve the dashboard to browsers on the local network (tray → Display → Tablet view). Off by default.</summary>
    public bool TabletView { get; set; }

    public int TabletPort { get; set; } = TabletServer.DefaultPort;

    /// <summary>The secret in the tablet link. Created when the tablet view is first turned on; replaced by "New link".</summary>
    public string? TabletToken { get; set; }

    /// <summary>Where the weather is for (tray → Weather → Set location…), or null: then no forecast is fetched at all.</summary>
    public WeatherLocation? WeatherLocation { get; set; }

    /// <summary><c>auto</c> follows Windows' region; <c>metric</c> is °C and km/h, <c>imperial</c> °F and mph.</summary>
    public string WeatherUnits { get; set; } = WeatherUnitsAuto;

    public const string WeatherUnitsAuto = "auto";
    public const string WeatherUnitsMetric = "metric";
    public const string WeatherUnitsImperial = "imperial";

    /// <summary>What the weather provider should fetch, or null when no location is set.</summary>
    public WeatherRequest? WeatherRequest() =>
        WeatherLocation is { } place ? new WeatherRequest(place.Name, place.Latitude, place.Longitude, ResolveWeatherUnits(WeatherUnits)) : null;

    public static WeatherUnits ResolveWeatherUnits(string? setting) => setting?.ToLowerInvariant() switch
    {
        WeatherUnitsMetric => Core.Weather.WeatherUnits.Metric,
        WeatherUnitsImperial => Core.Weather.WeatherUnits.Imperial,
        _ => RegionInfo.CurrentRegion.IsMetric ? Core.Weather.WeatherUnits.Metric : Core.Weather.WeatherUnits.Imperial,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonDefaults.Options) is { } settings)
            {
                // Hand edits: drop links missing a process, or both a layout and a theme, rather than tripping over them later.
                settings.GameLayouts.RemoveAll(rule => rule is null || !rule.IsUsable);
                settings.Schedule.RemoveAll(rule => rule is null || !rule.IsUsable);
                settings.CustomData.RemoveAll(source => source is null);
                settings.Alerts.RemoveAll(rule => rule is null || !rule.IsUsable);
                return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt settings file shouldn't stop the dashboard starting; it will be rewritten on next save.
        }

        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonDefaults.Options));
    }
}

public sealed record PixelRect(int X, int Y, int Width, int Height);

/// <param name="Name">The place as you picked it, e.g. "Leeds, England, United Kingdom".</param>
public sealed record WeatherLocation(string Name, double Latitude, double Longitude);

/// <summary>What tray → Logging… logs: these metrics, a row every so many seconds, files kept for so many days (0 = always).</summary>
public sealed record LogSettings(bool Enabled, IReadOnlyList<string> Metrics, int EverySeconds, int KeepDays)
{
    public static readonly LogSettings Default = new(false, ["cpu.load", "mem.load", "gpu.load", "cpu.temp", "gpu.temp", "fps"], 10, 30);
}

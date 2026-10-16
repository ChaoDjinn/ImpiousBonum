using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Remote;
using ImpiousBonum.Core.StatusChecks;
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
    private List<ExtraDashboard> _extraDashboards = [];
    private List<WeatherLocation> _weatherPlaces = [];
    private UsbScreenSettings _usbScreen = UsbScreenSettings.Default;
    private LogSettings _logging = LogSettings.Default;
    private List<StatusCheck> _statusChecks = [];

    /// <summary>Stable monitor interface path (survives reboots and display renumbering).</summary>
    public string? MonitorId { get; set; }

    /// <summary>GDI device name such as \\.\DISPLAY7; a fallback when the id can't be matched.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Fill the whole monitor. When false, <see cref="Area"/> is used.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>
    /// Remembers the monitor the dashboard first lands on (see <see cref="DisplayMonitor.Resolve"/>). Without a spare
    /// screen that's the main one, and filling it would cover everything else, so the dashboard starts as an unlocked
    /// window to put where you like. Returns true if it did. Does nothing once a monitor has been chosen.
    /// </summary>
    public bool PlaceFirstTime(DisplayMonitor monitor)
    {
        if (MonitorId is not null || MonitorDevice is not null)
            return false;
        MonitorId = monitor.Id;
        MonitorDevice = monitor.Device;
        if (!monitor.IsPrimary || !Fill)
            return false;
        Fill = false;
        Locked = false;
        return true;
    }

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
    /// Show the built-in holiday theme on and around each holiday (see <see cref="Holidays"/>), unless a rule in the
    /// schedule or a game's link sets the theme. Off by default.
    /// </summary>
    public bool HolidayThemes { get; set; }

    /// <summary>
    /// Your own metrics from web addresses and commands (the Custom data page). Replaced as a whole when edited, never
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

    /// <summary>Notifications and flashing banners when a reading is out of bounds for a while (the Alerts page).</summary>
    public List<AlertRule> Alerts
    {
        get => _alerts;
        set => _alerts = value ?? [];
    }

    /// <summary>
    /// Calendars to show events from (the Calendars page). Replaced as a whole when edited, never changed in place, since
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
    /// News feeds whose headlines the news widget shows (the News feeds page). Replaced as a whole when edited, never
    /// changed in place, since the sampler reads it from another thread.
    /// </summary>
    public List<NewsFeed> NewsFeeds
    {
        get => _newsFeeds;
        set => _newsFeeds = value ?? [];
    }

    /// <summary>More dashboards, each a layout filling a monitor of its own (the Screens page). None by default.</summary>
    public List<ExtraDashboard> ExtraDashboards
    {
        get => _extraDashboards;
        set => _extraDashboards = value ?? [];
    }

    /// <summary>A small USB screen to show a layout on too (the Tablet and USB screen page). Off by default.</summary>
    public UsbScreenSettings UsbScreen
    {
        get => _usbScreen;
        set => _usbScreen = value ?? UsbScreenSettings.Default;
    }

    /// <summary>Logging readings to CSV files (the Logging page). Off by default.</summary>
    public LogSettings Logging
    {
        get => _logging;
        set => _logging = value ?? LogSettings.Default;
    }

    /// <summary>Key combinations that work from anywhere (the Hotkeys page). None by default.</summary>
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

    /// <summary>Serve the dashboard to browsers on the local network (the Tablet and USB screen page). Off by default.</summary>
    public bool TabletView { get; set; }

    public int TabletPort { get; set; } = TabletServer.DefaultPort;

    /// <summary>Let tablets press the layout's buttons and media controls (the Tablet and USB screen page). Off by default.</summary>
    public bool TabletButtons { get; set; }

    /// <summary>The secret in the tablet link. Created when the tablet view is first turned on; replaced by "New link".</summary>
    public string? TabletToken { get; set; }

    /// <summary>Where the weather is for (the Weather page), or null: then no forecast is fetched at all.</summary>
    public WeatherLocation? WeatherLocation { get; set; }

    /// <summary>
    /// <c>auto</c> follows Windows' region; <c>metric</c> is °C, km/h, mm, km and hPa, <c>imperial</c> °F, mph, inches,
    /// miles and inches of mercury.
    /// </summary>
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

    /// <summary>
    /// More places for the weather widget to move between (the Weather page's More places), besides
    /// <see cref="WeatherLocation"/>, which stays the main one: only it has the weather readings, air quality and
    /// warnings. None by default. Replaced as a whole when edited, never changed in place, since the sampler reads it
    /// from another thread.
    /// </summary>
    public List<WeatherLocation> WeatherPlaces
    {
        get => _weatherPlaces;
        set => _weatherPlaces = value ?? [];
    }

    /// <summary>What the weather provider should fetch for <see cref="WeatherPlaces"/>, in their order: none without a location, as the weather is off.</summary>
    public IReadOnlyList<WeatherRequest> WeatherPlaceRequests()
    {
        var places = WeatherPlaces;
        if (WeatherLocation is null || places.Count == 0)
            return [];
        var units = ResolveWeatherUnits(WeatherUnits);
        return [.. places.Select(p => new WeatherRequest(p.Name, p.Latitude, p.Longitude, units))];
    }

    /// <summary>
    /// Makes <paramref name="place"/>, one of <see cref="WeatherPlaces"/>, the location, and puts the location where it
    /// was among them. As with any new location, the Met Office area goes back to automatic.
    /// </summary>
    public void MakeMainWeatherPlace(WeatherLocation place)
    {
        var index = WeatherPlaces.IndexOf(place);
        if (index < 0 || WeatherLocation is not { } main)
            return;
        var places = WeatherPlaces.ToList();
        places[index] = main;
        // The location first: a sample in between then fetches only the new location, as it would anyway, rather than the old one as a new place too.
        WeatherLocation = place;
        WeatherPlaces = places;
        MetOfficeArea = MetOfficeAreaAuto;
    }

    /// <summary>
    /// A new location, from Set location… or Change location…. The Met Office area goes back to automatic, as one chosen
    /// for the last place is unlikely to suit this one, and if it's one of <see cref="WeatherPlaces"/> (arriving on
    /// holiday, say), it comes out of them, so it isn't listed or fetched twice.
    /// </summary>
    public void SetWeatherLocation(WeatherLocation place)
    {
        WeatherLocation = place;
        MetOfficeArea = MetOfficeAreaAuto;
        if (WeatherPlaces.Any(p => SamePlace(p, place)))
            WeatherPlaces = [.. WeatherPlaces.Where(p => !SamePlace(p, place))];
    }

    /// <summary>Whether <paramref name="place"/> is the location or one of <see cref="WeatherPlaces"/> already, by where it is rather than its name.</summary>
    public bool HasWeatherPlace(WeatherLocation place) =>
        WeatherPlaces.Prepend(WeatherLocation).Any(p => p is not null && SamePlace(p, place));

    /// <summary>Close enough to be the same forecast: the coordinates sent are rounded to four places.</summary>
    private static bool SamePlace(WeatherLocation a, WeatherLocation b) =>
        Math.Round(a.Latitude, 4) == Math.Round(b.Latitude, 4) && Math.Round(a.Longitude, 4) == Math.Round(b.Longitude, 4);

    /// <summary>Air quality and pollen for the weather's location (the Weather page). Off by default: nothing is fetched until it's on.</summary>
    public bool AirQuality { get; set; }

    /// <summary>
    /// Which air quality index: <c>auto</c> is the European one in Europe and the US one elsewhere (see
    /// <see cref="AirQualityReadings.ScaleFor"/>); <c>european</c> and <c>us</c> are always that one.
    /// </summary>
    public string AirQualityScale { get; set; } = AirQualityScaleAuto;

    public const string AirQualityScaleAuto = "auto";
    public const string AirQualityScaleEuropean = "european";
    public const string AirQualityScaleUs = "us";

    /// <summary>What the air quality provider should fetch, or null when it's off or no location is set.</summary>
    public AirQualityRequest? AirQualityRequest() =>
        AirQuality && WeatherLocation is { } place ? new AirQualityRequest(place.Latitude, place.Longitude, ResolveAirQualityScale(AirQualityScale)) : null;

    public static AqiScale ResolveAirQualityScale(string? setting) => setting?.ToLowerInvariant() switch
    {
        AirQualityScaleEuropean => AqiScale.European,
        AirQualityScaleUs => AqiScale.Us,
        _ => AqiScale.Automatic,
    };

    /// <summary>
    /// Severe weather warnings for the weather's location (the Weather page), from the Met Office or the National Weather
    /// Service. Off by default: nothing is fetched until it's on.
    /// </summary>
    public bool WeatherWarnings { get; set; }

    /// <summary>A Windows notification for each new warning, once warnings are on.</summary>
    public bool WeatherWarningsNotify { get; set; } = true;

    /// <summary>Whose warnings to show for a UK place: <c>auto</c> for the area it's in, or an area's code (see <see cref="MetOfficeWarnings.Areas"/>).</summary>
    public string MetOfficeArea { get; set; } = MetOfficeAreaAuto;

    public const string MetOfficeAreaAuto = "auto";

    /// <summary>
    /// Where warnings for the location would come from, whether or not they're on: null with no location, and
    /// <see cref="WarningSource.None"/> for a place outside the UK and the US. Only a US place goes to the NWS.
    /// </summary>
    public WarningsRequest? WeatherWarningsPlace()
    {
        if (WeatherLocation is not { } place)
            return null;
        var source = Core.Weather.WeatherWarnings.SourceFor(place.CountryCode, place.Name, place.Latitude, place.Longitude);
        var area = source == WarningSource.MetOffice
            ? (MetOfficeWarnings.Find(MetOfficeArea) ?? MetOfficeWarnings.AreaFor(place.Latitude, place.Longitude)).Code
            : null;
        return new WarningsRequest(source, place.Latitude, place.Longitude, area);
    }

    /// <summary>What the warnings provider should fetch, or null when they're off, there's no location or nobody warns the place.</summary>
    public WarningsRequest? WeatherWarningsRequest() =>
        WeatherWarnings && WeatherWarningsPlace() is { Source: not WarningSource.None } place ? place : null;

    /// <summary>What a kWh costs (the Energy page), or null: then there are no cost readings.</summary>
    public double? EnergyPrice { get; set; }

    /// <summary>The currency symbol the costs show, or null for the one of the region Windows is set to.</summary>
    public string? EnergyCurrency { get; set; }

    /// <summary>Watts added to the CPU's and graphics card's for the parts nothing measures: the motherboard, memory, drives and fans.</summary>
    public int EnergyOtherWatts { get; set; } = (int)Core.Energy.EnergySettings.DefaultOtherWatts;

    /// <summary>What the energy readings need, with hand edits that make no sense (a negative price, say) left out.</summary>
    public EnergySettings EnergySettings() => new(
        Math.Clamp(EnergyOtherWatts, 0, 2000),
        EnergyPrice is > 0 and < 1000 ? EnergyPrice : null,
        string.IsNullOrWhiteSpace(EnergyCurrency) ? EnergyCost.DefaultSymbol : EnergyCurrency.Trim());

    /// <summary>Great Britain's grid carbon intensity (the Energy page). Off by default: nothing is fetched until it's on.</summary>
    public bool GridCarbon { get; set; }

    /// <summary>The first half of a postcode for a region's carbon intensity, e.g. LS1, or null for Great Britain as a whole.</summary>
    public string? GridCarbonArea { get; set; }

    /// <summary>What the grid carbon provider should fetch, or null when it's off.</summary>
    public GridCarbonRequest? GridCarbonRequest() => GridCarbon ? new GridCarbonRequest(CarbonIntensity.Area(GridCarbonArea)) : null;

    /// <summary>
    /// Your own servers and services to check (the Status checks page). None by default: nothing is checked until one is
    /// added. Replaced as a whole when edited, never changed in place, since the sampler reads it from another thread.
    /// </summary>
    public List<StatusCheck> StatusChecks
    {
        get => _statusChecks;
        set => _statusChecks = value ?? [];
    }

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
                settings.WeatherPlaces.RemoveAll(place => place?.Name is null);
                settings.StatusChecks.RemoveAll(check => check?.Name is null || check.Address is null);
                return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A broken settings file (a hand edit gone wrong, say) shouldn't stop the dashboard starting. The defaults
            // replace it at the next save, so keep a copy first: it has calendars, alerts and hotkeys worth getting back.
            return new AppSettings { LoadProblem = $"{Path.GetFileName(AppPaths.SettingsFile)} couldn't be read ({ex.Message}). {KeepUnreadable()}" };
        }

        return new AppSettings();
    }

    /// <summary>Why the settings file couldn't be read, when <see cref="Load"/> fell back to the defaults; null when it could.</summary>
    [JsonIgnore]
    public string? LoadProblem { get; private init; }

    /// <summary>The name of the copy kept of a settings file that couldn't be read.</summary>
    public const string UnreadableCopyName = "settings.unreadable.json";

    private static string KeepUnreadable()
    {
        try
        {
            File.Copy(AppPaths.SettingsFile, Path.Combine(AppPaths.DataDirectory, UnreadableCopyName), overwrite: true);
            return $"It's been kept as {UnreadableCopyName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"A copy couldn't be kept: {ex.Message}";
        }
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonDefaults.Options));
    }
}

public sealed record PixelRect(int X, int Y, int Width, int Height);

/// <param name="Name">The place as you picked it, e.g. "Leeds, England, United Kingdom".</param>
public sealed record WeatherLocation(string Name, double Latitude, double Longitude)
{
    /// <summary>
    /// The country's two-letter code, e.g. GB, which decides whose warnings it gets. Null for coordinates typed in, and
    /// for places saved before it was kept: then the name and coordinates decide (see <see cref="Core.Weather.WeatherWarnings.SourceFor"/>).
    /// </summary>
    public string? CountryCode { get; init; }
}

/// <summary>What the Logging page logs: these metrics, a row every so many seconds, files kept for so many days (0 = always).</summary>
public sealed record LogSettings(bool Enabled, IReadOnlyList<string> Metrics, int EverySeconds, int KeepDays)
{
    public static readonly LogSettings Default = new(false, ["cpu.load", "mem.load", "gpu.load", "cpu.temp", "gpu.temp", "fps"], 10, 30);
}

using System.Windows;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// What the main window's pages read and change: the app's settings and what acts on them. The app implements it,
/// and every change takes effect (and is saved) at once, as in Windows' own Settings.
/// </summary>
internal interface ISettingsHost
{
    AppSettings Settings { get; }

    LayoutStore Layouts { get; }

    /// <summary>The latest readings, for live previews and metric pickers; null before sampling starts.</summary>
    MetricStore? Store { get; }

    // ---- Layouts ----------------------------------------------------------------------------------

    /// <summary>
    /// Starts editing the layout of one dashboard: the main one (null) or another, by its monitor's id. Its window shows
    /// the editor's handles until <see cref="EndEditing"/>.
    /// </summary>
    LayoutEditor BeginEditing(string? screen);

    /// <summary>The editor has gone (unsaved changes dealt with): the dashboards show their saved layouts again.</summary>
    void EndEditing();

    /// <summary>The monitor id of the dashboard being edited, the main one's included; null if its screen isn't connected.</summary>
    string? EditedMonitorId { get; }

    // ---- Screens ----------------------------------------------------------------------------------

    /// <summary>Shows a saved layout on the main dashboard.</summary>
    void ShowOnMain(string layout);

    /// <summary>Puts the main dashboard on another monitor, replacing any other dashboard there.</summary>
    void MoveMainTo(DisplayMonitor monitor);

    void SetWindowOptions(WindowOptions options);

    WindowOptions WindowOptions { get; }

    /// <summary>Back to the default windowed size, centred on its monitor.</summary>
    void ResetWindow();

    void BringDashboardsForward();

    /// <summary>The dashboards besides the main one, replaced as a whole.</summary>
    void SetExtraDashboards(List<ExtraDashboard> extras);

    /// <summary>
    /// A new dashboard on <paramref name="monitor"/> showing <paramref name="layout"/>, or with a new, empty layout of its
    /// own (made to the screen's size), which opens in the editor to fill.
    /// </summary>
    void AddDashboard(DisplayMonitor monitor, string? layout = null);

    /// <summary>Dim after <paramref name="minutes"/> without input (0 never) to <paramref name="brightness"/> percent.</summary>
    void SetAway(int minutes, int brightness);

    // ---- Tablet and USB screen --------------------------------------------------------------------

    bool TabletRunning { get; }

    void SetTabletView(bool on);

    void SetTabletButtons(bool on);

    void ShowTabletLink();

    void CopyTabletLink();

    void NewTabletLink();

    string UsbStatus { get; }

    void SetUsbScreen(UsbScreenSettings usb);

    // ---- Pages, schedule and game layouts ---------------------------------------------------------

    void SetPages(bool swipe, int rotateSeconds, IReadOnlyList<string> pages);

    void SetScheduleEnabled(bool on);

    void AddScheduleRule(Window owner);

    void RemoveScheduleRule(ScheduleRule rule);

    /// <summary>Apps seen in front lately, most recent first, to offer for linking to a layout.</summary>
    IReadOnlyList<string> RecentApps { get; }

    void SetGameLayoutsEnabled(bool on);

    void SetSessionRecaps(bool on);

    void LinkGame(GameLayoutRule rule);

    void UnlinkGame(GameLayoutRule rule);

    // ---- Readings ---------------------------------------------------------------------------------

    string SensorStatus { get; }

    /// <summary>The installed sensor service is from an older version of the app.</summary>
    bool SensorServiceOutdated { get; }

    /// <summary>True to install (or update) the sensor service, false to remove it. Asks Windows for permission.</summary>
    Task ChangeSensorServiceAsync(bool install);

    /// <summary>A monitor id, or null for whichever app is in the foreground.</summary>
    void SetFpsSource(string? monitorId);

    /// <summary>"Leeds: 12° Light rain", or what's missing.</summary>
    string WeatherStatus { get; }

    void SetWeatherLocation(Window owner);

    void TurnOffWeather();

    void SetWeatherUnits(string units);

    /// <summary>Air quality and pollen on or off; on, they're fetched for the weather's location once it has one.</summary>
    void SetAirQuality(bool on);

    void SetAirQualityScale(string scale);

    /// <summary>Severe weather warnings on or off; on, they're fetched for the weather's location if it's in the UK or the US.</summary>
    void SetWeatherWarnings(bool on);

    void SetWeatherWarningsNotify(bool on);

    /// <summary><see cref="AppSettings.MetOfficeAreaAuto"/>, or a Met Office area's code.</summary>
    void SetMetOfficeArea(string area);

    /// <summary>While warnings are on: whose the location gets and what they are now, or why there are none, e.g. "From the Met Office for Wales: none now".</summary>
    string WeatherWarningsStatus { get; }

    void SetCalendars(IReadOnlyList<CalendarLink> calendars);

    void SetNewsFeeds(IReadOnlyList<NewsFeed> feeds);

    string ClaudeStatus { get; }

    bool ClaudeCodeConnected { get; }

    void ConnectClaudeCode(Window owner);

    /// <summary>A custom source's last status (its <c>custom.&lt;name&gt;.status</c> metric).</summary>
    string? CustomSourceStatus(string statusId);

    // ---- Alerts, hotkeys and logging --------------------------------------------------------------

    /// <summary>The alert rules changed (and have been saved); <paramref name="log"/> says how.</summary>
    void AlertsChanged(string log);

    /// <summary>Lets go of the hotkeys while one is being set, so pressing them records them rather than running them.</summary>
    void SuspendHotkeys();

    /// <summary>Takes the hotkeys up again after <see cref="SuspendHotkeys"/>, as saved.</summary>
    void ResumeHotkeys();

    /// <summary>Saves the hotkeys; they work once they're no longer suspended.</summary>
    void SetHotkeys(IReadOnlyList<Hotkey> hotkeys);

    void SetLogging(LogSettings logging);

    // ---- The app ------------------------------------------------------------------------------------

    /// <summary>The installed version, or null for a development build.</summary>
    string? Version { get; }

    UpdateStatus Updates { get; }

    Task CheckForUpdatesAsync();

    Task RestartToUpdateAsync();

    void SetHardwareRendering(bool on);
}

/// <summary>Where updating has got to, for the About page.</summary>
/// <param name="Installed">False for a development build, which never updates.</param>
/// <param name="Checking">A check (and maybe a download) is under way.</param>
/// <param name="Ready">A downloaded version waiting for a restart.</param>
/// <param name="LastChecked">When the last check finished, if one has.</param>
/// <param name="Failed">The last check couldn't reach GitHub or failed some other way.</param>
/// <param name="Progress">How much of a download is done, 0–100, while one runs.</param>
public sealed record UpdateStatus(bool Installed, bool Checking, string? Ready, DateTime? LastChecked, bool Failed, int? Progress);

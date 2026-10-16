using System.Globalization;
using System.Windows;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App;

// The app's window: opening it, and what its pages read and change.
public partial class App : ISettingsHost
{
    /// <summary>How many apps seen in front are offered for linking to a layout.</summary>
    private const int RecentAppsKept = 8;

    private SettingsWindow? _settingsWindow;
    private readonly List<string> _recentApps = [];
    private bool _hotkeysSuspended;

    /// <summary>Opens the window, or brings it forward if it's open; when it opens, on the layout editor unless that's not wanted.</summary>
    private SettingsWindow ShowSettings(bool openEditor = true)
    {
        if (_settingsWindow is { } open)
        {
            BringToFront(open);
            return open;
        }

        var window = new SettingsWindow(this);
        _settingsWindow = window;
        window.Closed += (_, _) => _settingsWindow = null;
        window.Show();
        BringToFront(window);
        if (openEditor)
            window.ShowPage(SettingsPage.Layouts);
        return window;
    }

    /// <summary>An app was seen in front: it goes to the top of those offered for linking to a layout.</summary>
    private void NoteRecentApp(string app)
    {
        if (_recentApps.Count > 0 && string.Equals(_recentApps[0], app, StringComparison.OrdinalIgnoreCase))
            return;
        _recentApps.RemoveAll(a => string.Equals(a, app, StringComparison.OrdinalIgnoreCase));
        _recentApps.Insert(0, app);
        if (_recentApps.Count > RecentAppsKept)
            _recentApps.RemoveAt(_recentApps.Count - 1);
    }

    AppSettings ISettingsHost.Settings => _settings;

    LayoutStore ISettingsHost.Layouts => _layouts!;

    MetricStore? ISettingsHost.Store => _sampler?.Store;

    // ---- Layouts ----------------------------------------------------------------------------------

    LayoutEditor ISettingsHost.BeginEditing(string? screen)
    {
        _editingScreen = ScreenIndex(screen) == 0 ? null : screen;
        var session = new LayoutSession(_layouts!.LoadOrDefault(EditedLayout()), _layouts.Themes);
        var editor = new LayoutEditor(session, _layouts, EditedLayout, EditedScreenSize);
        _editor = editor;
        editor.SetScreens(EditorScreens(), ScreenIndex(_editingScreen));

        session.Changed += (_, change) => MirrorToDashboard(session, change);
        editor.LayoutChosen += (_, name) => ShowOnEditedDashboard(name);
        editor.ScreenChosen += (_, index) =>
        {
            EditedWindow()?.EndEdit();
            _editingScreen = index > 0 && index <= _settings.ExtraDashboards.Count ? _settings.ExtraDashboards[index - 1].MonitorId : null;
            // Any changes left unsaved on the last dashboard are gone from the screen too.
            ShowSavedLayouts();
            EditedWindow()?.BeginEdit(session);
        };
        editor.Setups = WidgetSetups;
        editor.Options = PickOptions;
        editor.PickedOption = PickedOption;
        editor.WidgetAdded += (_, descriptor) =>
        {
            if (_settingsWindow is not { } owner)
                return;
            if (descriptor.Type == BarsWidget.ClaudeDescriptor.Type)
                OfferClaudeCodeConnection(owner);
            else if ((descriptor.Type == WeatherWidget.Descriptor.Type || descriptor.Type == RainWidget.Descriptor.Type) && _settings.WeatherLocation is null)
                SetWeatherLocation(owner);
        };
        EditedWindow()?.BeginEdit(session);
        return editor;
    }

    void ISettingsHost.EndEditing()
    {
        _editor = null;
        EditedWindow()?.EndEdit();
        _editingScreen = null;
        // Catch up with any game that came or went while the editor had the layout.
        ApplyGameLayout();
        // Back to what's on disk: the saved layout, or the old one if changes were discarded.
        ApplyLayout();
    }

    string? ISettingsHost.EditedMonitorId => EditedExtra()?.MonitorId ?? DisplayMonitor.Resolve(_settings, DisplayMonitor.GetAll())?.Id;

    // ---- Screens ----------------------------------------------------------------------------------

    void ISettingsHost.ShowOnMain(string layout) => SelectLayout(layout);

    void ISettingsHost.MoveMainTo(DisplayMonitor monitor) => MoveTo(monitor);

    void ISettingsHost.SetWindowOptions(WindowOptions options) => SetWindowOptions(options);

    WindowOptions ISettingsHost.WindowOptions => CurrentWindowOptions();

    void ISettingsHost.ResetWindow() => ResetWindow();

    void ISettingsHost.BringDashboardsForward()
    {
        _window?.BringForward();
        foreach (var extra in _extras)
            extra.BringForward();
    }

    void ISettingsHost.SetExtraDashboards(List<ExtraDashboard> extras)
    {
        if (RefuseWhileEditing())
            return;
        _settings.ExtraDashboards = extras;
        _settings.Save();
        AppLog.Info(extras.Count == 0 ? "More dashboards: none" : $"More dashboards: {string.Join(", ", extras.Select(e => $"\"{e.Layout}\" on {e.MonitorDevice}"))}");
        ApplyExtraDashboards();
        Place();
    }

    void ISettingsHost.AddDashboard(DisplayMonitor monitor, string? layout) => AddDashboard(monitor, layout);

    void ISettingsHost.SetAway(int minutes, int brightness)
    {
        (_settings.AwayDimMinutes, _settings.AwayBrightness) = (minutes, brightness);
        _settings.Save();
        AppLog.Info(minutes == 0 ? "Dimming when away: off" : $"Dimming when away: after {minutes} minutes, to {brightness}%");
    }

    // ---- Tablet and USB screen --------------------------------------------------------------------

    bool ISettingsHost.TabletRunning => _tablet?.IsRunning == true;

    void ISettingsHost.SetTabletView(bool on) => SetTabletView(on);

    void ISettingsHost.SetTabletButtons(bool on)
    {
        _settings.TabletButtons = on;
        _settings.Save();
        if (_tablet is not null)
            _tablet.AllowTaps = on;
        AppLog.Info($"Tablets {(on ? "can" : "can't")} press buttons");
    }

    void ISettingsHost.SetTabletReadings(bool on)
    {
        _settings.TabletReadings = on;
        _settings.Save();
        if (_tablet is not null)
            _tablet.Readings = on ? _sampler?.Store : null;
        AppLog.Info($"Readings {(on ? "shared" : "not shared")} with other apps");
    }

    void ISettingsHost.ShowTabletLink(string? layout) => ShowTabletLink(layout);

    void ISettingsHost.CopyTabletLink(string? layout) => CopyTabletLink(layout);

    void ISettingsHost.NewTabletLink() => NewTabletLink();

    string ISettingsHost.UsbStatus => _usb?.Status ?? "Off";

    void ISettingsHost.SetUsbScreen(UsbScreenSettings usb)
    {
        _settings.UsbScreen = usb;
        _settings.Save();
        AppLog.Info(usb.Enabled ? $"USB screen: on, port {usb.Port}, layout {usb.Layout ?? "same as the dashboard"}, {usb.Brightness}%, {usb.Orientation}" : "USB screen: off");
        ApplyUsbScreen();
    }

    // ---- Pages, schedule and game layouts ---------------------------------------------------------

    void ISettingsHost.SetPages(bool swipe, int rotateSeconds, IReadOnlyList<string> pages)
    {
        (_settings.SwipePages, _settings.RotatePagesSeconds) = (swipe, rotateSeconds);
        _settings.Pages = [.. pages];
        _settings.Save();
        _window!.SwipesEnabled = _settings.SwipePages;
        _lastPageChange = DateTime.UtcNow;
    }

    void ISettingsHost.SetScheduleEnabled(bool on)
    {
        _settings.ScheduleEnabled = on;
        _settings.Save();
        AppLog.Info($"Schedule {(on ? "on" : "off")}");
        UpdateSchedule();
    }

    void ISettingsHost.SetHolidayThemes(bool on)
    {
        _settings.HolidayThemes = on;
        _settings.Save();
        AppLog.Info($"Holiday themes {(on ? "on" : "off")}");
        UpdateSchedule();
    }

    void ISettingsHost.AddScheduleRule(Window owner) => AddScheduleRule(owner);

    void ISettingsHost.RemoveScheduleRule(ScheduleRule rule)
    {
        _settings.Schedule.Remove(rule);
        _settings.Save();
        AppLog.Info($"Removed from the schedule: {rule.Describe()}");
        UpdateSchedule();
    }

    IReadOnlyList<string> ISettingsHost.RecentApps => _recentApps;

    void ISettingsHost.SetGameLayoutsEnabled(bool on) => SetGameLayoutsEnabled(on);

    void ISettingsHost.SetSessionRecaps(bool on)
    {
        _settings.SessionRecaps = on;
        _settings.Save();
        AppLog.Info($"Session recaps {(on ? "on" : "off")}");
    }

    void ISettingsHost.LinkGame(GameLayoutRule rule) => LinkGame(rule);

    void ISettingsHost.UnlinkGame(GameLayoutRule rule) => UnlinkGame(rule);

    // ---- Readings ---------------------------------------------------------------------------------

    string ISettingsHost.SensorStatus => SensorStatus();

    bool ISettingsHost.SensorServiceOutdated => SensorServiceControl.IsOutdated(SensorVersion());

    Task ISettingsHost.ChangeSensorServiceAsync(bool install) => ChangeSensorServiceAsync(install);

    void ISettingsHost.SetFpsSource(string? monitorId)
    {
        _settings.FpsMonitorId = monitorId;
        _settings.Save();
    }

    string ISettingsHost.WeatherStatus => WeatherStatus();

    void ISettingsHost.SetWeatherLocation(Window owner) => SetWeatherLocation(owner);

    void ISettingsHost.TurnOffWeather()
    {
        _settings.WeatherLocation = null;
        _settings.WeatherPlaces = [];
        _settings.Save();
        AppLog.Info("Weather off");
    }

    void ISettingsHost.SetWeatherPlaces(IReadOnlyList<WeatherLocation> places, string log)
    {
        // A new list, not a change to the old one: the sampler reads it from another thread.
        _settings.WeatherPlaces = [.. places];
        _settings.Save();
        AppLog.Info(log);
    }

    void ISettingsHost.MakeMainWeatherPlace(WeatherLocation place)
    {
        _settings.MakeMainWeatherPlace(place);
        _settings.Save();
        // Not which place: it says where you are, and the log goes with bug reports.
        AppLog.Info("Weather location swapped with one of the more places");
    }

    string ISettingsHost.WeatherPlaceStatus(WeatherLocation place) => WeatherPlaceStatus(place);

    void ISettingsHost.SetWeatherUnits(string units)
    {
        _settings.WeatherUnits = units;
        _settings.Save();
    }

    void ISettingsHost.SetAirQuality(bool on)
    {
        _settings.AirQuality = on;
        _settings.Save();
        AppLog.Info($"Air quality and pollen {(on ? "on" : "off")}");
    }

    void ISettingsHost.SetAirQualityScale(string scale)
    {
        _settings.AirQualityScale = scale;
        _settings.Save();
    }

    void ISettingsHost.SetWeatherWarnings(bool on)
    {
        _settings.WeatherWarnings = on;
        _settings.Save();
        AppLog.Info($"Severe weather warnings {(on ? "on" : "off")}");
    }

    void ISettingsHost.SetWeatherWarningsNotify(bool on)
    {
        _settings.WeatherWarningsNotify = on;
        _settings.Save();
        AppLog.Info($"Notifications of weather warnings {(on ? "on" : "off")}");
    }

    void ISettingsHost.SetMetOfficeArea(string area)
    {
        _settings.MetOfficeArea = area;
        _settings.Save();
        // Not which area: it says where you are, and the log goes with bug reports.
        AppLog.Info(area == AppSettings.MetOfficeAreaAuto ? "Met Office area set to automatic" : "Met Office area chosen");
    }

    string ISettingsHost.WeatherWarningsStatus => WeatherWarningsStatus();

    void ISettingsHost.SetEnergyPrice(double? price)
    {
        _settings.EnergyPrice = price;
        _settings.Save();
        AppLog.Info(price is null ? "Energy price: none" : string.Create(CultureInfo.InvariantCulture, $"Energy price: {price} a kWh"));
    }

    void ISettingsHost.SetEnergyCurrency(string? symbol)
    {
        _settings.EnergyCurrency = symbol;
        _settings.Save();
    }

    void ISettingsHost.SetEnergyOtherWatts(int watts)
    {
        _settings.EnergyOtherWatts = watts;
        _settings.Save();
        AppLog.Info($"Energy: {watts} W for everything else");
    }

    void ISettingsHost.SetGridCarbon(bool on)
    {
        _settings.GridCarbon = on;
        _settings.Save();
        AppLog.Info($"Grid carbon intensity {(on ? "on" : "off")}");
    }

    void ISettingsHost.SetGridCarbonArea(string? area)
    {
        _settings.GridCarbonArea = area;
        _settings.Save();
        // Not which area: it says roughly where you live, and the log goes with bug reports.
        AppLog.Info(area is null ? "Grid carbon for all of Great Britain" : "Grid carbon area chosen");
    }

    void ISettingsHost.SetCalendars(IReadOnlyList<CalendarLink> calendars) => SetCalendars(calendars);

    void ISettingsHost.SetNewsFeeds(IReadOnlyList<NewsFeed> feeds) => SetNewsFeeds(feeds);

    string ISettingsHost.ClaudeStatus => ClaudeStatus();

    bool ISettingsHost.ClaudeCodeConnected => IsClaudeCodeConnected();

    void ISettingsHost.ConnectClaudeCode(Window owner) => ConnectClaudeCode(owner);

    string? ISettingsHost.CustomSourceStatus(string statusId) =>
        _sampler?.Store.TryGet(statusId, out var status) == true ? status.Text : null;

    // ---- Alerts, hotkeys and logging --------------------------------------------------------------

    void ISettingsHost.AlertsChanged(string log) => AppLog.Info(log);

    void ISettingsHost.SuspendHotkeys()
    {
        _hotkeysSuspended = true;
        _hotkeys?.Clear();
    }

    void ISettingsHost.ResumeHotkeys()
    {
        if (!_hotkeysSuspended)
            return;
        _hotkeysSuspended = false;
        ApplyHotkeys();
    }

    void ISettingsHost.SetHotkeys(IReadOnlyList<Hotkey> hotkeys)
    {
        _settings.Hotkeys = [.. hotkeys];
        _settings.Save();
        AppLog.Info(hotkeys.Count == 0 ? "Hotkeys: none" : $"Hotkeys: {string.Join(", ", hotkeys.Select(h => $"{h.Keys} = {h.Describe()}"))}");
        if (!_hotkeysSuspended)
            ApplyHotkeys();
    }

    void ISettingsHost.SetLogging(LogSettings logging)
    {
        _settings.Logging = logging;
        _settings.Save();
        // A fresh logger, and a new file if the columns changed.
        _logger = null;
        _prunedOn = default;
        AppLog.Info(logging.Enabled
            ? $"Logging {string.Join(", ", logging.Metrics)} every {logging.EverySeconds} s to {AppPaths.LogsFolder}"
            : "Logging off");
    }

    // ---- The app ------------------------------------------------------------------------------------

    string? ISettingsHost.Version => _updater.CurrentVersion;

    UpdateStatus ISettingsHost.Updates => UpdateStatus();

    Task ISettingsHost.CheckForUpdatesAsync() => CheckForUpdatesAsync(notify: false);

    Task ISettingsHost.RestartToUpdateAsync() => RestartToUpdateAsync();

    void ISettingsHost.SetHardwareRendering(bool on)
    {
        _settings.HardwareRendering = on;
        _settings.Save();
        AppLog.Info($"Hardware rendering {(on ? "on" : "off")} from the next start");
    }
}

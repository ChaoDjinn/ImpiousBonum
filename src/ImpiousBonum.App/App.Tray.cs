using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App;

// The tray: what its menu items do, and the actions buttons on the dashboard ask for.
public partial class App
{
    /// <summary>What each tray menu item does, and filling in the menus each time they open.</summary>
    private void ConnectTray(TrayIcon tray)
    {
        tray.MenuOpening += (_, _) =>
        {
            var monitors = DisplayMonitor.GetAll();
            tray.SetMonitors(monitors, DisplayMonitor.Resolve(_settings, monitors), CurrentWindowOptions());
            tray.SetSensorState(SensorStatus(), SensorServiceControl.IsInstalled, SensorServiceControl.IsOutdated(SensorVersion()));
            tray.SetClaudeState(ClaudeStatus(), IsClaudeCodeConnected());
            tray.SetWeatherState(WeatherStatus(), _settings.WeatherLocation is not null, _settings.WeatherUnits);
            tray.SetFpsSources(monitors, _settings.FpsMonitorId);
            tray.SetTabletView(_tablet?.IsRunning == true, _settings.TabletButtons);
            tray.SetUsbScreen(_settings.UsbScreen, System.IO.Ports.SerialPort.GetPortNames().Order().ToList(), _layouts?.List() ?? [], _usb?.Status ?? "Off");
            if (_layouts is not null)
            {
                var names = _layouts.List();
                tray.SetLayouts(names, _layouts.Active, _layouts.ActivePath);
                tray.SetExtraDashboards(_settings.ExtraDashboards, monitors, DisplayMonitor.Resolve(_settings, monitors), names);
                tray.SetGameLayouts(_settings.GameLayoutsEnabled, _settings.SessionRecaps, _settings.GameLayouts, _recentApp, names, _layouts.Themes.List());
                tray.SetPages(_settings.SwipePages, _settings.RotatePagesSeconds, _settings.Pages, names);
                tray.SetSchedule(_settings.ScheduleEnabled, _settings.Schedule,
                    ScheduledLook.Active(_settings.Schedule, TimeOnly.FromDateTime(DateTime.Now), WindowsColourMode.IsDark()));
                tray.SetAway(_settings.AwayDimMinutes, _settings.AwayBrightness);
            }
        };
        tray.LayoutSelected += (_, name) => SelectLayout(name);
        tray.ImportLayoutRequested += (_, _) => ImportLayout();
        tray.GalleryRequested += (_, _) => OpenGallery();
        tray.GameLayoutsToggled += (_, enabled) => SetGameLayoutsEnabled(enabled);
        tray.SessionRecapsToggled += (_, on) =>
        {
            _settings.SessionRecaps = on;
            _settings.Save();
            AppLog.Info($"Session recaps {(on ? "on" : "off")}");
        };
        tray.ScheduleToggled += (_, enabled) =>
        {
            _settings.ScheduleEnabled = enabled;
            _settings.Save();
            AppLog.Info($"Schedule {(enabled ? "on" : "off")}");
            UpdateSchedule();
        };
        tray.ScheduleAddRequested += (_, _) => AddScheduleRule();
        tray.PagesChanged += (_, pages) =>
        {
            (_settings.SwipePages, _settings.RotatePagesSeconds) = (pages.Swipe, pages.RotateSeconds);
            _settings.Pages = [.. pages.Pages];
            _settings.Save();
            _window!.SwipesEnabled = _settings.SwipePages;
            _lastPageChange = DateTime.UtcNow;
        };
        tray.AwayChanged += (_, away) =>
        {
            (_settings.AwayDimMinutes, _settings.AwayBrightness) = away;
            _settings.Save();
            AppLog.Info(away.Minutes == 0 ? "Dimming when away: off" : $"Dimming when away: after {away.Minutes} minutes, to {away.Brightness}%");
        };
        tray.ScheduleRuleRemoved += (_, rule) =>
        {
            _settings.Schedule.Remove(rule);
            _settings.Save();
            AppLog.Info($"Removed from the schedule: {rule.Describe()}");
            UpdateSchedule();
        };
        tray.GameLinkRequested += (_, rule) => LinkGame(rule);
        tray.GameLinkRemoved += (_, rule) => UnlinkGame(rule);
        tray.FpsSourceSelected += (_, monitorId) =>
        {
            _settings.FpsMonitorId = monitorId;
            _settings.Save();
        };
        tray.MonitorSelected += (_, monitor) => MoveTo(monitor);
        tray.ExtraDashboardsChanged += (_, extras) =>
        {
            if (RefuseWhileEditing())
                return;
            _settings.ExtraDashboards = extras;
            _settings.Save();
            AppLog.Info(extras.Count == 0 ? "More dashboards: none" : $"More dashboards: {string.Join(", ", extras.Select(e => $"\"{e.Layout}\" on {e.MonitorDevice}"))}");
            ApplyExtraDashboards();
            Place();
        };
        tray.ExtraDashboardAddRequested += (_, monitor) => AddDashboard(monitor);
        tray.ExtraDashboardEditRequested += (_, extra) => EditDashboard(extra.MonitorId);
        tray.WindowOptionsChanged += (_, options) => SetWindowOptions(options);
        tray.ResetWindowRequested += (_, _) => ResetWindow();
        tray.BringToFrontRequested += (_, _) => _window?.BringForward();
        tray.TabletViewToggled += (_, on) => SetTabletView(on);
        tray.TabletButtonsToggled += (_, on) =>
        {
            _settings.TabletButtons = on;
            _settings.Save();
            if (_tablet is not null)
                _tablet.AllowTaps = on;
            AppLog.Info($"Tablets {(on ? "can" : "can't")} press buttons");
        };
        tray.UsbScreenChanged += (_, usb) =>
        {
            _settings.UsbScreen = usb;
            _settings.Save();
            AppLog.Info(usb.Enabled ? $"USB screen: on, port {usb.Port}, layout {usb.Layout ?? "same as the dashboard"}, {usb.Brightness}%, {usb.Orientation}" : "USB screen: off");
            ApplyUsbScreen();
        };
        tray.TabletLinkRequested += (_, _) => ShowTabletLink();
        tray.TabletLinkCopyRequested += (_, _) => CopyTabletLink();
        tray.TabletNewLinkRequested += (_, _) => NewTabletLink();
        tray.SensorServiceChangeRequested += async (_, install) => await ChangeSensorServiceAsync(install);
        tray.ConnectClaudeCodeRequested += (_, _) => ConnectClaudeCode();
        tray.WeatherLocationRequested += (_, _) => SetWeatherLocation(null);
        tray.CustomDataRequested += (_, _) => OpenCustomData();
        tray.AlertsRequested += (_, _) => OpenAlerts();
        tray.HotkeysRequested += (_, _) => EditHotkeys();
        tray.CalendarRequested += (_, _) => EditCalendars();
        tray.NewsRequested += (_, _) => EditNews();
        tray.LoggingRequested += (_, _) => EditLogging();
        tray.WeatherTurnedOff += (_, _) =>
        {
            _settings.WeatherLocation = null;
            _settings.Save();
            AppLog.Info("Weather off");
        };
        tray.WeatherUnitsSelected += (_, units) =>
        {
            _settings.WeatherUnits = units;
            _settings.Save();
        };
        tray.ReloadRequested += (_, _) => ApplyLayout();
        tray.EditLayoutRequested += (_, _) => OpenEditor();
        tray.ExitRequested += async (_, _) => await ExitAsync();
        tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(userAsked: true);
        tray.AboutRequested += (_, _) => ShowAbout();
    }

    private AboutWindow? _about;

    /// <summary>Tray → Help → About Impious Bonum: one window at a time.</summary>
    private void ShowAbout()
    {
        if (_about is not null)
        {
            BringToFront(_about);
            return;
        }
        _about = new AboutWindow(_updater.CurrentVersion, SensorStatus());
        _about.Closed += (_, _) => _about = null;
        _about.Show();
        BringToFront(_about);
    }

    /// <summary>A tap on a dashboard button or media control. Problems show as a notification, since the tap gave no other feedback.</summary>
    private void RunAction(WidgetAction action)
    {
        if (action.Kind == WidgetAction.Notify)
        {
            _tray?.ShowAlert(action.Target);
            return;
        }
        if (_actions?.Run(action) is not { } problem)
            return;
        AppLog.Warning($"Button ({action.Kind}): {problem}");
        _tray?.ShowError("Impious Bonum", problem);
    }

    private string? SwitchLayoutFromButton(string name)
    {
        if (_layouts?.Library.Find(name) is not { } found)
            return $"There's no layout called \"{name}\". Check the button's target in the editor.";
        SelectLayout(found);
        return null;
    }
}

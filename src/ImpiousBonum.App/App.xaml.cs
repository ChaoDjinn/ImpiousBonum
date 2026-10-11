using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Remote;
using ImpiousBonum.Core.Sensors;
using ImpiousBonum.Core.Logging;
using ImpiousBonum.Core.Sessions;
using Microsoft.Win32;

namespace ImpiousBonum.App;

/// <summary>
/// Command line:
///   --data-dir &lt;dir&gt;     use a different settings/layout folder (default %AppData%\ImpiousBonum)
///   --snapshot &lt;file.png&gt; render the layout to a PNG after a short warm-up, then exit
///   --layout &lt;name&gt;       with --snapshot, render this saved layout instead of the active one
///   --warmup &lt;seconds&gt;    how long to sample before a snapshot (default 3)
///   --widget-docs &lt;file&gt;  write the widget reference (docs/widgets.md) and exit
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = new();
    private Sampler? _sampler;
    private LayoutStore? _layouts;
    private TrayIcon? _tray;
    private ActionRunner? _actions;
    private DashboardWindow? _window;
    // One window for each of _settings.ExtraDashboards, in the same order.
    private readonly List<DashboardWindow> _extras = [];
    private readonly HashSet<DashboardWindow> _extrasHiddenForFullscreen = [];
    private DispatcherTimer? _tick;
    private EditorWindow? _editor;
    private TabletView? _tablet;
    private UsbScreenView? _usb;
    private TabletLinkWindow? _tabletLink;
    private readonly Updater _updater = new();
    private FrameRateTarget? _frameRateTarget;
    private readonly GameLayoutSwitcher _gameSwitcher = new(GameLayoutSwitcher.DefaultDelay);
    // "layout:<name>" or "theme:<name>" for links whose target has gone, so each is reported once.
    private readonly HashSet<string> _reportedMissingLinks = new(StringComparer.OrdinalIgnoreCase);
    private string? _gameLayoutApplied;
    private ScheduledLook? _scheduled;
    private DateTime _peekUntil;
    private DateTime _lastTouch = DateTime.MinValue;
    private DateTime _lastPageChange = DateTime.UtcNow;
    private string? _recentApp;

    /// <summary>The linked game in front, for session recaps; written on the UI thread, read by the sampler's.</summary>
    private volatile string? _trackedGame;
    private MetricLogger? _logger;
    private CsvLog? _csvLog;
    private DateTime _prunedOn;
    private bool _logFailed;
    private DispatcherTimer? _updateTimer;
    private bool _warnedOutdatedService;
    private bool _mirrorQueued;
    private bool _started;
    private bool _reportedError;
    private bool _hiddenForFullscreen;
    private bool _hiddenByUser;
    private GlobalHotkeys? _hotkeys;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = ParseArgs(e.Args);
        if (args.TryGetValue("data-dir", out var dataDir))
            AppPaths.UseDataDirectory(dataDir);

        if (args.TryGetValue("widget-docs", out var docsPath))
        {
            File.WriteAllText(docsPath, WidgetDocs.ToMarkdown());
            Shutdown();
            return;
        }

        if (args.TryGetValue("snapshot", out var snapshotPath))
        {
            var warmup = args.TryGetValue("warmup", out var w) && double.TryParse(w, out var seconds) ? seconds : 3;
            var rendered = await RenderSnapshotAsync(snapshotPath, TimeSpan.FromSeconds(warmup), args.GetValueOrDefault("layout"));
            Shutdown(rendered ? 0 : 1);
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\ImpiousBonum", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        AppLog.Start(_updater.CurrentVersion);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, fatal) => AppLog.Error("Fatal error", fatal.ExceptionObject as Exception ?? new Exception($"{fatal.ExceptionObject}"));
        TaskScheduler.UnobservedTaskException += (_, unobserved) =>
        {
            AppLog.Error("Unobserved task error", unobserved.Exception);
            unobserved.SetObserved();
        };

        _settings = AppSettings.Load();
        if (!_settings.HardwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        _frameRateTarget = new FrameRateTarget(() => _settings.FpsMonitorId);
        _sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(_settings.PingHost, _frameRateTarget.Get, AppPaths.ClaudeUsageFile, () => _settings.WeatherRequest(), () => _settings.CustomData, () => _settings.Calendars,
            () => _settings.NewsFeeds,
            () => _trackedGame, recap => Dispatcher.BeginInvoke(() => OnSessionEnded(recap))));
        _sampler.Start();

        _window = new DashboardWindow();
        _window.EditRequested += (_, _) => OpenEditor();
        _window.SwipesEnabled = _settings.SwipePages;
        _window.SwipeRequested += (_, step) => TurnPage(step);
        _window.Touched += (_, _) => _lastTouch = DateTime.UtcNow;
        _window.PeekRequested += (_, _) =>
        {
            _peekUntil = DateTime.UtcNow + Dimming.PeekFor;
            UpdateDimming();
        };
        _actions = new ActionRunner(SwitchLayoutFromButton);
        _window.ActionRequested += (_, action) => RunAction(action);
        _window.PlacementChanged += (_, rect) => OnWindowMoved(rect);
        _tray = new TrayIcon();
        _tray.MenuOpening += (_, _) =>
        {
            var monitors = DisplayMonitor.GetAll();
            _tray.SetMonitors(monitors, DisplayMonitor.Resolve(_settings, monitors), CurrentWindowOptions());
            _tray.SetSensorState(SensorStatus(), SensorServiceControl.IsInstalled, SensorServiceControl.IsOutdated(SensorVersion()));
            _tray.SetClaudeState(ClaudeStatus(), IsClaudeCodeConnected());
            _tray.SetWeatherState(WeatherStatus(), _settings.WeatherLocation is not null, _settings.WeatherUnits);
            _tray.SetFpsSources(monitors, _settings.FpsMonitorId);
            _tray.SetTabletView(_tablet?.IsRunning == true, _settings.TabletButtons);
            _tray.SetUsbScreen(_settings.UsbScreen, System.IO.Ports.SerialPort.GetPortNames().Order().ToList(), _layouts?.List() ?? [], _usb?.Status ?? "Off");
            if (_layouts is not null)
            {
                var names = _layouts.List();
                _tray.SetLayouts(names, _layouts.Active, _layouts.ActivePath);
                _tray.SetExtraDashboards(_settings.ExtraDashboards, monitors, DisplayMonitor.Resolve(_settings, monitors), names, _layouts.Active);
                _tray.SetGameLayouts(_settings.GameLayoutsEnabled, _settings.SessionRecaps, _settings.GameLayouts, _recentApp, names, _layouts.Themes.List());
                _tray.SetPages(_settings.SwipePages, _settings.RotatePagesSeconds, _settings.Pages, names);
                _tray.SetSchedule(_settings.ScheduleEnabled, _settings.Schedule,
                    ScheduledLook.Active(_settings.Schedule, TimeOnly.FromDateTime(DateTime.Now), WindowsColourMode.IsDark()));
                _tray.SetAway(_settings.AwayDimMinutes, _settings.AwayBrightness);
            }
        };
        _tray.LayoutSelected += (_, name) => SelectLayout(name);
        _tray.ImportLayoutRequested += (_, _) => ImportLayout();
        _tray.GalleryRequested += (_, _) => OpenGallery();
        _tray.GameLayoutsToggled += (_, enabled) => SetGameLayoutsEnabled(enabled);
        _tray.SessionRecapsToggled += (_, on) =>
        {
            _settings.SessionRecaps = on;
            _settings.Save();
            AppLog.Info($"Session recaps {(on ? "on" : "off")}");
        };
        _tray.ScheduleToggled += (_, enabled) =>
        {
            _settings.ScheduleEnabled = enabled;
            _settings.Save();
            AppLog.Info($"Schedule {(enabled ? "on" : "off")}");
            UpdateSchedule();
        };
        _tray.ScheduleAddRequested += (_, _) => AddScheduleRule();
        _tray.PagesChanged += (_, pages) =>
        {
            (_settings.SwipePages, _settings.RotatePagesSeconds) = (pages.Swipe, pages.RotateSeconds);
            _settings.Pages = [.. pages.Pages];
            _settings.Save();
            _window!.SwipesEnabled = _settings.SwipePages;
            _lastPageChange = DateTime.UtcNow;
        };
        _tray.AwayChanged += (_, away) =>
        {
            (_settings.AwayDimMinutes, _settings.AwayBrightness) = away;
            _settings.Save();
            AppLog.Info(away.Minutes == 0 ? "Dimming when away: off" : $"Dimming when away: after {away.Minutes} minutes, to {away.Brightness}%");
        };
        _tray.ScheduleRuleRemoved += (_, rule) =>
        {
            _settings.Schedule.Remove(rule);
            _settings.Save();
            AppLog.Info($"Removed from the schedule: {rule.Describe()}");
            UpdateSchedule();
        };
        _tray.GameLinkRequested += (_, rule) => LinkGame(rule);
        _tray.GameLinkRemoved += (_, rule) => UnlinkGame(rule);
        _tray.FpsSourceSelected += (_, monitorId) =>
        {
            _settings.FpsMonitorId = monitorId;
            _settings.Save();
        };
        _tray.MonitorSelected += (_, monitor) => MoveTo(monitor);
        _tray.ExtraDashboardsChanged += (_, extras) =>
        {
            _settings.ExtraDashboards = extras;
            _settings.Save();
            AppLog.Info(extras.Count == 0 ? "More dashboards: none" : $"More dashboards: {string.Join(", ", extras.Select(e => $"\"{e.Layout}\" on {e.MonitorDevice}"))}");
            ApplyExtraDashboards();
            Place();
        };
        _tray.WindowOptionsChanged += (_, options) => SetWindowOptions(options);
        _tray.ResetWindowRequested += (_, _) => ResetWindow();
        _tray.BringToFrontRequested += (_, _) => _window?.BringForward();
        _tray.TabletViewToggled += (_, on) => SetTabletView(on);
        _tray.TabletButtonsToggled += (_, on) =>
        {
            _settings.TabletButtons = on;
            _settings.Save();
            if (_tablet is not null)
                _tablet.AllowTaps = on;
            AppLog.Info($"Tablets {(on ? "can" : "can't")} press buttons");
        };
        _tray.UsbScreenChanged += (_, usb) =>
        {
            _settings.UsbScreen = usb;
            _settings.Save();
            AppLog.Info(usb.Enabled ? $"USB screen: on, port {usb.Port}, layout {usb.Layout ?? "same as the dashboard"}, {usb.Brightness}%, {usb.Orientation}" : "USB screen: off");
            ApplyUsbScreen();
        };
        _tray.TabletLinkRequested += (_, _) => ShowTabletLink();
        _tray.TabletLinkCopyRequested += (_, _) => CopyTabletLink();
        _tray.TabletNewLinkRequested += (_, _) => NewTabletLink();
        _tray.SensorServiceChangeRequested += async (_, install) => await ChangeSensorServiceAsync(install);
        _tray.ConnectClaudeCodeRequested += (_, _) => ConnectClaudeCode();
        _tray.WeatherLocationRequested += (_, _) => SetWeatherLocation(null);
        _tray.CustomDataRequested += (_, _) => OpenCustomData();
        _tray.AlertsRequested += (_, _) => OpenAlerts();
        _tray.HotkeysRequested += (_, _) => EditHotkeys();
        _tray.CalendarRequested += (_, _) => EditCalendars();
        _tray.NewsRequested += (_, _) => EditNews();
        _tray.LoggingRequested += (_, _) => EditLogging();
        _tray.WeatherTurnedOff += (_, _) =>
        {
            _settings.WeatherLocation = null;
            _settings.Save();
            AppLog.Info("Weather off");
        };
        _tray.WeatherUnitsSelected += (_, units) =>
        {
            _settings.WeatherUnits = units;
            _settings.Save();
        };
        _tray.ReloadRequested += (_, _) => ApplyLayout();
        _tray.EditLayoutRequested += (_, _) => OpenEditor();
        _tray.ExitRequested += async (_, _) => await ExitAsync();
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(userAsked: true);
        _tray.RestartToUpdateRequested += async (_, _) => await RestartToUpdateAsync();
        _tray.SetVersion(_updater.CurrentVersion, _updater.IsInstalled);
        StartUpdateChecks();
        if (_settings.LoadProblem is { } problem)
        {
            AppLog.Warning($"Started with default settings: {problem}");
            _tray.ShowError("Settings couldn't be read", "Impious Bonum started with its default settings. Details are in dashboard.log (tray → Open settings folder).");
        }

        _tablet = new TabletView { AllowTaps = _settings.TabletButtons };
        _tablet.ActionRequested += (_, action) => RunAction(action);
        _usb = new UsbScreenView();
        _layouts = new LayoutStore(_settings);
        _layouts.Changed += (_, _) => ApplyLayout();
        _layouts.ActiveChanged += (_, _) =>
        {
            if (_layouts is { } store)
                AppLog.Info($"Showing layout \"{store.Active}\"{(store.IsTemporary ? " (game layout)" : "")}");
            ApplyLayout();
        };
        _layouts.ThemeChanged += (_, _) =>
        {
            // A saved theme changed on disk, or a game's theme came or went. The editor restyles its own copy.
            _editor?.RefreshTheme();
            ApplyLayout();
        };
        ApplyLayout();
        ApplyUsbScreen();
        if (_settings.TabletView)
            StartTablet();

        ApplyWindowBehaviour();
        Place();
        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += (_, hotkey) => RunHotkey(hotkey);
        ApplyHotkeys();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Refresh();
        Refresh();
        _started = true;
        // Line ticks up with the wall clock so the minute flips on time.
        await Task.Delay(1000 - DateTime.Now.Millisecond);
        Refresh();
        _tick.Start();
    }

    /// <summary>
    /// Logs errors on the UI thread. Once running, a status display is more use carrying on after one bad tick than
    /// vanishing, so the error is swallowed and the tray says where the details are (once per run). During startup
    /// it isn't: a half-built app with no window or tray would sit invisibly holding the single-instance lock.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(_started ? "Unhandled error" : "Startup failed", e.Exception);
        if (!_started)
            return;

        e.Handled = true;
        if (!_reportedError)
        {
            _reportedError = true;
            _tray?.ShowError("Impious Bonum hit an error", "It's still running. Details are in dashboard.log (tray → Open settings folder).");
        }
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        _window?.Dashboard.Refresh(_sampler!.Store, now);
        foreach (var extra in _extras)
            extra.Dashboard.Refresh(_sampler!.Store, now);
        _editor?.Refresh(_sampler!.Store, now);
        _tablet?.Refresh(_sampler!.Store, now);
        _usb?.Refresh(_sampler!.Store, now);
        UpdateGameLayout();
        RotatePages();
        UpdateAlerts();
        UpdateLogging();
        UpdateDimming();
        UpdateFullscreenHide();

        // After an app update the installed service is still the old build; say so once per run.
        if (!_warnedOutdatedService && SensorServiceControl.IsOutdated(SensorVersion()))
        {
            _warnedOutdatedService = true;
            _tray?.ShowInfo("Sensor service needs updating", "It's from an older version of Impious Bonum. Tray → Sensors → Update sensor service.");
        }
    }

    private string? SensorVersion() =>
        _sampler is not null && _sampler.Store.TryGet(SensorHostProvider.Version, out var version) ? version.Text : null;

    /// <summary>Installed copies check GitHub shortly after starting and then every few hours; development builds never do.</summary>
    private void StartUpdateChecks()
    {
        if (!_updater.IsInstalled)
            return;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesAsync(userAsked: false);
        };
        _updateTimer.Start();
    }

    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (userAsked)
            _tray?.ShowInfo("Impious Bonum", "Checking for updates…");

        AppLog.Info("Checking for updates");
        var ready = await _updater.CheckAndDownloadAsync();
        _tray?.SetUpdateReady(ready);
        if (ready is not null)
            _tray?.ShowInfo($"Impious Bonum {ready} is ready", "Tray → Restart to update.");
        else if (userAsked)
            _tray?.ShowInfo("Impious Bonum", $"You're up to date ({_updater.CurrentVersion}).");
    }

    private async Task RestartToUpdateAsync()
    {
        AppLog.Info($"Restarting to update to {_updater.ReadyVersion}");
        if (await ReleaseEverythingAsync())
            _updater.RestartToUpdate();
    }

    private void OpenEditor()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            return;
        }

        var session = new LayoutSession(_layouts!.LoadOrDefault(), _layouts.Themes);
        _editor = new EditorWindow(session, _layouts, () =>
            !_settings.Fill && _settings.Area is { } area ? (area.Width, area.Height)
            : DisplayMonitor.Resolve(_settings, DisplayMonitor.GetAll()) is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height)
            : null);

        session.Changed += (_, change) => MirrorToDashboard(session, change);
        _editor.Setups = WidgetSetups;
        _editor.WidgetAdded += (_, descriptor) =>
        {
            if (descriptor.Type == BarsWidget.ClaudeDescriptor.Type && _editor is { } editor)
                OfferClaudeCodeConnection(editor);
            else if (descriptor.Type == WeatherWidget.Descriptor.Type && _settings.WeatherLocation is null && _editor is { } owner)
                SetWeatherLocation(owner);
        };
        _editor.Closed += (_, _) =>
        {
            _editor = null;
            _window?.EndEdit();
            // Catch up with any game that came or went while the editor had the layout.
            ApplyGameLayout();
            // Back to what's on disk: the saved layout, or the old one if changes were discarded.
            ApplyLayout();
        };
        _editor.Show();
        BringToFront(_editor);
        _window?.BeginEdit(session);
        Refresh();
    }

    /// <summary>The editor's buttons for what a widget needs set up outside the layout: the same as the tray's.</summary>
    internal IReadOnlyList<WidgetSetup> WidgetSetups(WidgetDescriptor descriptor)
    {
        if (descriptor.Type == WeatherWidget.Descriptor.Type)
        {
            return [new(_settings.WeatherLocation is null ? "Set location…" : "Change location…",
                () => _settings.WeatherLocation is { } place ? $"Forecasts for {place.Name}." : "No location yet, so there's no forecast.",
                SetWeatherLocation)];
        }
        if (descriptor.Type == AgendaWidget.Descriptor.Type)
            return [new("Calendars…", () => Count(_settings.Calendars.Count, "calendar", "No calendars yet, so the agenda is empty."), EditCalendars)];
        if (descriptor.Type == NewsWidget.Descriptor.Type)
            return [new("News feeds…", () => Count(_settings.NewsFeeds.Count, "news feed", "No news feeds yet, so there are no headlines."), EditNews)];
        if (descriptor.Type == BarsWidget.ClaudeDescriptor.Type)
        {
            return [new(IsClaudeCodeConnected() ? "Reconnect Claude Code" : "Connect Claude Code…",
                () => IsClaudeCodeConnected() ? "Claude Code is connected." : "Claude Code isn't connected yet, so there's no usage.",
                owner => ConnectClaudeCode(owner))];
        }
        return [];

        static string Count(int count, string what, string none) => count switch
        {
            0 => none,
            1 => $"1 {what}.",
            _ => $"{count} {what}s.",
        };
    }

    /// <summary>
    /// Windows won't let a background app take focus. When the editor is opened from the dashboard (which never
    /// activates), briefly making it topmost at least puts it in front where it can be seen.
    /// </summary>
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
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

    /// <summary>Tray → Layout. With the editor open, the editor switches (asking about unsaved changes first).</summary>
    private void SelectLayout(string name)
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            _editor.SwitchTo(name);
            return;
        }

        try
        {
            _layouts?.SetActive(name);
        }
        catch (FileNotFoundException ex)
        {
            _tray?.ShowError("Couldn't switch layout", ex.Message);
        }
    }

    /// <summary>Tray → Layout → Import…: adds the layout and shows it. With the editor open, the editor does it.</summary>
    /// <summary>Tray → Layout → Gallery…; shows what was imported. Not while the editor is open, which has its own copy of the layouts.</summary>
    private void OpenGallery()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            _tray?.ShowInfo("Layout gallery", "Close the layout editor first, then open the gallery from the tray.");
            return;
        }
        if (_layouts is null)
            return;
        var gallery = new GalleryWindow(_settings.GalleryAddress ?? Gallery.DefaultAddress, _layouts, _sampler?.Store);
        if (gallery.ShowDialog() != true || gallery.Imported is not { } name)
            return;
        _layouts.SetActive(name);
        AppLog.Info($"Imported layout \"{name}\" from the gallery");
    }

    private void ImportLayout()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            _editor.ImportLayout();
            return;
        }
        if (_layouts is null || LayoutTransfer.Import(null, _layouts) is not { } name)
            return;

        _layouts.SetActive(name);
        AppLog.Info($"Imported layout \"{name}\"");
    }

    /// <summary>
    /// Follows the app the user is looking at (the same one the FPS reading follows) and switches to a linked game's
    /// layout once it has been in front for a few seconds, and back once it has gone.
    /// </summary>
    private void UpdateGameLayout()
    {
        if (_frameRateTarget is null || _layouts is null)
            return;

        var (_, app) = _frameRateTarget.Get();
        if (app is not null && !NotGames.Contains(app))
            _recentApp = app;
        _trackedGame = _settings.SessionRecaps && GameLayoutSwitcher.Match(_settings.GameLayouts, app) is { } game
            ? game.Layout ?? GameLayoutRule.Normalize(game.Process)
            : null;

        if (_settings.GameLayoutsEnabled && _gameSwitcher.Update(_settings.GameLayouts, app, DateTime.UtcNow))
            ApplyGameLayout();
        UpdateSchedule();
    }

    /// <summary>Checks the schedule (each second, with the game check) and switches when what it wants changes.</summary>
    private void UpdateSchedule()
    {
        var look = _settings.ScheduleEnabled ? ScheduledLook.From(_settings.Schedule, TimeOnly.FromDateTime(DateTime.Now), WindowsColourMode.IsDark()) : null;
        if (look == _scheduled)
            return;
        _scheduled = look;
        AppLog.Info(look is null ? "Schedule: nothing scheduled now" : $"Schedule: {look.Describe()}");
        ApplyGameLayout();
    }

    /// <summary>
    /// Dims the dashboard for the schedule and when you're away, each second. A game drawing frames counts as someone
    /// there, since a gamepad doesn't count as input to Windows.
    /// </summary>
    private void UpdateDimming()
    {
        if (_window is null || _sampler is null)
            return;
        var drawing = _sampler.Store.TryGet(SensorAliases.FramesPerSecond, out var fps) && fps.Value is > 0;
        var brightness = Dimming.Brightness(
            _scheduled?.Brightness, Dimming.IdleTime(), _settings.AwayDimMinutes, _settings.AwayBrightness,
            drawing, DateTime.UtcNow < _peekUntil, _editor is not null);
        _window.SetBrightness(brightness);
        foreach (var extra in _extras)
            extra.SetBrightness(brightness);
    }

    /// <summary>
    /// Moves to the next page every few seconds when rotation is on: not while editing, while a game's layout is
    /// showing, or for a minute after someone touched the dashboard.
    /// </summary>
    private void RotatePages()
    {
        var now = DateTime.UtcNow;
        if (_settings.RotatePagesSeconds <= 0 || _layouts is null || _editor is not null || _layouts.IsTemporary
            || now - _lastTouch < Pages.RotationPauseAfterTouch || now - _lastPageChange < TimeSpan.FromSeconds(_settings.RotatePagesSeconds))
            return;
        TurnPage(1);
    }

    /// <summary>A swipe or rotation: shows the page <paramref name="step"/> away, as if picked from the tray, with page dots.</summary>
    private void TurnPage(int step)
    {
        _lastPageChange = DateTime.UtcNow;
        if (_layouts is null || _editor is not null)
            return;
        var pages = Pages.Of(_layouts.List(), _settings.Pages);
        if (Pages.Next(pages, _layouts.Active, step) is not { } next)
            return;
        SelectLayout(next);
        _window?.ShowPage(pages.ToList().FindIndex(p => string.Equals(p, next, StringComparison.OrdinalIgnoreCase)), pages.Count);
    }

    /// <summary>Tray → Layout → Schedule → Add…</summary>
    private void AddScheduleRule()
    {
        if (_layouts is null)
            return;
        if (ScheduleRuleWindow.Ask(_layouts.List(), _layouts.Themes.List(), "Night Watch") is not { } rule)
            return;
        _settings.Schedule.Add(rule);
        _settings.Save();
        AppLog.Info($"Added to the schedule: {rule.Describe()}");
        UpdateSchedule();
    }

    /// <summary>Apps offered for linking never include the shell, whose windows come to the front when you use the tray.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp",
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ImpiousBonum",
    };

    /// <summary>
    /// Shows the layout and theme a game in front wants, with the schedule filling in what the game's link doesn't set,
    /// unless the editor has the layout or they're already showing.
    /// </summary>
    private void ApplyGameLayout()
    {
        if (_layouts is null || _editor is not null)
            return;

        var rule = _gameSwitcher.Current;
        var scheduled = _scheduled;
        var layout = rule?.Layout ?? scheduled?.Layout;
        var theme = rule?.Theme ?? scheduled?.Theme;
        var wanted = layout is null && theme is null ? null : $"{layout}|{theme}";
        if (string.Equals(wanted, _gameLayoutApplied, StringComparison.OrdinalIgnoreCase))
            return;
        _gameLayoutApplied = wanted;

        if (layout is not null && _layouts.Library.Find(layout) is null)
        {
            ReportMissing(rule?.Layout is not null ? $"\"{rule.Process}\" is linked to" : "The schedule names", "layout", layout);
            layout = null;
        }
        if (theme is not null && _layouts.Themes.Find(theme) is null)
        {
            ReportMissing(rule?.Theme is not null ? $"\"{rule.Process}\" is linked to" : "The schedule names", "theme", theme);
            theme = null;
        }

        AppLog.Info(rule is not null ? $"\"{rule.Process}\" is in front; switching to {rule.Describe()}{(scheduled is null ? "" : $" (schedule: {scheduled.Describe()})")}"
            : scheduled is not null ? $"Scheduled: {scheduled.Describe()}"
            : "No linked game in front and nothing scheduled; back to the chosen layout");
        try
        {
            _layouts.ShowTemporarily(layout);
            _layouts.ShowThemeTemporarily(theme);
        }
        catch (FileNotFoundException ex)
        {
            // Deleted between the check and the switch.
            AppLog.Warning(ex.Message);
        }
    }

    /// <param name="who">"\"game\" is linked to" or "The schedule names".</param>
    private void ReportMissing(string who, string kind, string name)
    {
        if (!_reportedMissingLinks.Add($"{kind}:{name}"))
            return;
        AppLog.Warning($"{who} the {kind} \"{name}\", which doesn't exist; ignored");
        _tray?.ShowError($"{char.ToUpperInvariant(kind[0])}{kind[1..]} not found",
            $"{who} the {kind} \"{name}\", which doesn't exist any more. Tray → Layout → Game layouts or Schedule.");
    }

    private void SetGameLayoutsEnabled(bool enabled)
    {
        _settings.GameLayoutsEnabled = enabled;
        _settings.Save();
        AppLog.Info($"Automatic game layouts {(enabled ? "on" : "off")}");
        if (!enabled)
        {
            _gameSwitcher.Reset();
            ApplyGameLayout();
        }
    }

    private void LinkGame(GameLayoutRule rule)
    {
        _settings.GameLayouts.RemoveAll(r => r.Matches(rule.Process));
        _settings.GameLayouts.Add(rule);
        _settings.Save();
        _reportedMissingLinks.Remove($"layout:{rule.Layout}");
        _reportedMissingLinks.Remove($"theme:{rule.Theme}");
        AppLog.Info($"Linked \"{rule.Process}\" to {rule.Describe()}");
        _tray?.ShowInfo("Game layout linked", rule.Layout is null
            ? $"The theme \"{rule.Theme}\" will restyle the dashboard a few seconds after \"{rule.Process}\" comes to the front."
            : $"\"{rule.Describe()}\" will show a few seconds after \"{rule.Process}\" comes to the front.");
    }

    private void UnlinkGame(GameLayoutRule rule)
    {
        _settings.GameLayouts.Remove(rule);
        _settings.Save();
        AppLog.Info($"Removed the link from \"{rule.Process}\" to {rule.Describe()}");
    }

    /// <summary>Shows the editor's working copy on the real dashboard as you edit, so you see it at true size.</summary>
    private void MirrorToDashboard(LayoutSession session, LayoutChange change)
    {
        if (_window is null)
            return;

        if (change.Kind == ChangeKind.Geometry && change.WidgetIndex is { } index)
        {
            var r = LayoutSession.GeometryOf(session.Document.Widgets[index]);
            _window.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            _tablet?.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            foreach (var extra in ExtrasShowing(_layouts?.Active))
                extra.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            return;
        }

        if (_mirrorQueued)
            return;
        _mirrorQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _mirrorQueued = false;
            _window.Background = Theme.From(session.ResolvedTheme).Background;
            _window.Dashboard.Build(session.Document, session.ResolvedTheme);
            _tablet?.Build(session.Document, session.ResolvedTheme);
            foreach (var extra in ExtrasShowing(_layouts?.Active))
            {
                extra.Background = _window.Background;
                extra.Dashboard.Build(session.Document, session.ResolvedTheme);
            }
            Refresh();
        }, DispatcherPriority.Background);
    }

    private string SensorStatus() =>
        _sampler is not null && _sampler.Store.TryGet(SensorHostProvider.Status, out var status) && status.Text is { } text
            ? text
            : SensorHostProvider.NotRunning;

    /// <summary>"Leeds: 12° Light rain", or what's missing.</summary>
    private string WeatherStatus()
    {
        if (_settings.WeatherLocation is not { } place)
            return "No location set";
        var store = _sampler?.Store;
        if (store is null || !store.TryGet(WeatherProvider.Temperature, out var temperature) || temperature.Value is null)
            return $"{place.Name.Split(',')[0]}: waiting for the forecast";
        store.TryGet(WeatherProvider.Condition, out var condition);
        return $"{place.Name.Split(',')[0]}: {MetricFormatter.Format(temperature)} {condition.Text}".TrimEnd();
    }

    private CustomDataWindow? _customData;
    private AlertsWindow? _alertsWindow;
    private readonly AlertMonitor _alerts = new();

    /// <summary>Checks the alert rules each second: notifies when one goes off, and keeps the dashboard's banner current.</summary>
    private void UpdateAlerts()
    {
        if (_sampler is null || _window is null)
            return;
        foreach (var alert in _alerts.Update(_settings.Alerts, _sampler.Store, DateTime.UtcNow))
        {
            AppLog.Info(alert.Started ? $"Alert: {alert.Rule.Describe()}: {alert.Text}" : $"Alert over: {alert.Rule.Describe()}");
            if (alert.Started && alert.Rule.Notify)
                _tray?.ShowAlert(alert.Text);
        }
        _window.ShowAlerts(_alerts.Active.Where(rule => rule.Flash).Select(rule => (rule.Describe(), rule.Text(_sampler.Store))).ToList());
    }

    /// <summary>Tray → Logging…; changes start a fresh logger (and a new file if the columns changed).</summary>
    private void EditLogging()
    {
        if (LoggingWindow.Ask(_settings.Logging, _sampler?.Store) is not { } logging)
            return;
        _settings.Logging = logging;
        _settings.Save();
        _logger = null;
        _prunedOn = default;
        AppLog.Info(logging.Enabled
            ? $"Logging {string.Join(", ", logging.Metrics)} every {logging.EverySeconds} s to {AppPaths.LogsFolder}"
            : "Logging off");
    }

    /// <summary>Each tick: feeds the logger, writes a row when a period ends, and once a day removes old files.</summary>
    private void UpdateLogging()
    {
        var logging = _settings.Logging;
        if (!logging.Enabled || logging.Metrics.Count == 0 || _sampler is null)
        {
            _logger = null;
            return;
        }
        var now = DateTime.Now;
        _logger ??= new MetricLogger([.. logging.Metrics], TimeSpan.FromSeconds(Math.Max(1, logging.EverySeconds)));
        _csvLog ??= new CsvLog(AppPaths.LogsFolder);
        var store = _sampler.Store;
        if (_logger.Add(id => store.TryGet(id, out var sample) ? sample.Value : null, now) is not { } row)
            return;
        try
        {
            _csvLog.Append(_logger.Header, row, now);
            if (_prunedOn != now.Date)
            {
                _prunedOn = now.Date;
                if (CsvLog.Prune(AppPaths.LogsFolder, logging.KeepDays, now) is > 0 and var pruned)
                    AppLog.Info($"Logging: removed {pruned} old log file(s)");
            }
            _logFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Said once until it works again, not every period.
            if (!_logFailed)
                AppLog.Warning($"Couldn't write the log in {AppPaths.LogsFolder}: {ex.Message}");
            _logFailed = true;
        }
    }

    /// <summary>A game session ended: tell the user how it went and add it to sessions.csv.</summary>
    private void OnSessionEnded(SessionRecap recap)
    {
        AppLog.Info($"Session recap: {recap.Describe().ReplaceLineEndings("; ")}");
        try
        {
            SessionLog.Append(AppPaths.SessionsFile, recap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"Couldn't add the session to {AppPaths.SessionsFile}: {ex.Message}");
        }
        _tray?.ShowInfo("Session recap", recap.Describe());
    }

    /// <summary>Tray → News feeds…. The list is replaced, not changed in place, since the sampler reads it.</summary>
    private void EditNews(Window? owner = null)
    {
        if (LinkListWindow.AskFeeds(_settings.NewsFeeds, owner) is not { } feeds)
            return;
        _settings.NewsFeeds = [.. feeds];
        _settings.Save();
        AppLog.Info(feeds.Count == 0 ? "News feeds: none" : $"News feeds: {string.Join(", ", feeds.Select(f => f.Name))}");
    }

    /// <summary>Tray → Calendar…. The list is replaced, not changed in place, since the sampler reads it.</summary>
    private void EditCalendars(Window? owner = null)
    {
        if (LinkListWindow.AskCalendars(_settings.Calendars, owner) is not { } links)
            return;
        _settings.Calendars = [.. links];
        _settings.Save();
        AppLog.Info(links.Count == 0 ? "Calendars: none" : $"Calendars: {string.Join(", ", links.Select(l => l.Name))}");
    }

    /// <summary>Registers the hotkeys from settings; any that can't be set are reported once.</summary>
    private void ApplyHotkeys()
    {
        if (_hotkeys is null)
            return;
        var problems = _hotkeys.Apply(_settings.Hotkeys);
        foreach (var problem in problems)
            AppLog.Warning($"Hotkey not set: {problem}");
        if (problems.Count > 0)
            _tray?.ShowError(problems.Count == 1 ? "A hotkey isn't working" : "Some hotkeys aren't working", string.Join("\n", problems));
    }

    /// <summary>Tray → Hotkeys…. The current ones are let go while it's open, so pressing them sets them again rather than running them.</summary>
    private void EditHotkeys()
    {
        if (_layouts is null)
            return;
        _hotkeys?.Clear();
        var chosen = HotkeysWindow.Ask(_settings.Hotkeys, _layouts.List());
        if (chosen is not null)
        {
            _settings.Hotkeys = [.. chosen];
            _settings.Save();
            AppLog.Info(chosen.Count == 0 ? "Hotkeys: none" : $"Hotkeys: {string.Join(", ", chosen.Select(h => $"{h.Keys} = {h.Describe()}"))}");
        }
        ApplyHotkeys();
    }

    private void RunHotkey(Hotkey hotkey)
    {
        // Settings have the latest: a layout may have been renamed since the keys were registered.
        hotkey = _settings.Hotkeys.FirstOrDefault(h => string.Equals(h.Keys, hotkey.Keys, StringComparison.OrdinalIgnoreCase)) ?? hotkey;
        switch (hotkey.Action)
        {
            case Hotkey.ShowHide:
                ShowOrHide();
                break;
            case Hotkey.NextPage:
                TurnPage(1);
                break;
            case Hotkey.PreviousPage:
                TurnPage(-1);
                break;
            case Hotkey.Edit:
                OpenEditor();
                break;
            case Hotkey.SwitchLayout when hotkey.Layout is { } name:
                if (_layouts?.Library.Find(name) is { } found)
                    SelectLayout(found);
                else
                    _tray?.ShowError("Impious Bonum", $"There's no layout called \"{name}\" any more. Change its hotkey in tray → Hotkeys.");
                break;
        }
    }

    /// <summary>The show/hide hotkey. Hidden stays hidden (until pressed again or the app restarts), whatever else would show it.</summary>
    private void ShowOrHide()
    {
        if (_window is null)
            return;
        _hiddenByUser = !_hiddenByUser;
        AppLog.Info(_hiddenByUser ? "Dashboard hidden by hotkey" : "Dashboard shown by hotkey");
        if (_hiddenByUser)
        {
            _window.Hide();
            foreach (var extra in _extras)
                extra.Hide();
        }
        else
            Place();
    }

    /// <summary>Tray → Alerts…: one window at a time.</summary>
    private void OpenAlerts()
    {
        if (_alertsWindow is not null)
        {
            BringToFront(_alertsWindow);
            return;
        }
        _alertsWindow = new AlertsWindow(_settings, () => _sampler?.Store);
        _alertsWindow.Closed += (_, _) => _alertsWindow = null;
        _alertsWindow.Show();
        BringToFront(_alertsWindow);
    }

    /// <summary>Tray → Custom data…: one window at a time.</summary>
    private void OpenCustomData()
    {
        if (_customData is not null)
        {
            BringToFront(_customData);
            return;
        }
        _customData = new CustomDataWindow(_settings, id => _sampler?.Store.TryGet(id, out var status) == true ? status.Text : null);
        _customData.Closed += (_, _) => _customData = null;
        _customData.Show();
        BringToFront(_customData);
    }

    /// <summary>Tray → Weather → Set location…, or adding a weather widget when there's no location yet.</summary>
    private void SetWeatherLocation(Window? owner)
    {
        if (WeatherLocationWindow.Ask(owner, _settings.WeatherLocation) is not { } place)
            return;
        _settings.WeatherLocation = place;
        _settings.Save();
        AppLog.Info("Weather location set");
        _tray?.ShowInfo("Weather", $"Forecasts for {place.Name} will appear in a few seconds.");
    }

    private string ClaudeStatus() =>
        _sampler is not null && _sampler.Store.TryGet(ClaudeUsageProvider.Status, out var status) && status.Text is { } text
            ? text
            : "Not connected to Claude Code";

    private static bool IsClaudeCodeConnected()
    {
        try
        {
            var path = ClaudeCodeSetup.SettingsPath;
            return File.Exists(path) && ClaudeCodeSetup.Inspect(File.ReadAllText(path)).Kind == ClaudeCodeSetup.StatusLine.Ours;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The Claude usage widget has nothing to show until Claude Code's status line feeds it, and that step is easy to
    /// miss, so adding the widget offers it when it isn't set up yet.
    /// </summary>
    private void OfferClaudeCodeConnection(Window owner)
    {
        if (IsClaudeCodeConnected())
            return;
        var answer = MessageBox.Show(owner,
            "The Claude usage widget gets your plan usage from Claude Code's status line, which isn't set up yet.\n\n"
            + "Connect Claude Code now? This sets the status line in Claude Code's settings and keeps your other settings.\n\n"
            + "The numbers come from Claude Code in a terminal. The Claude Desktop app doesn't share usage with other apps, "
            + "though usage there still counts towards the bars.",
            "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
            ConnectClaudeCode(owner);
    }

    /// <summary>
    /// Sets Claude Code's status line to the sensor host's <c>claude-statusline</c> command, which feeds the Claude usage
    /// metrics. Asks before replacing a status line someone else set up, and keeps a copy of the file it changes.
    /// </summary>
    /// <param name="owner">The window to show the "replace your status line?" question over, if any.</param>
    private void ConnectClaudeCode(Window? owner = null)
    {
        var helper = ClaudeCodeSetup.HelperPath;
        if (!File.Exists(helper))
        {
            _tray?.ShowError("Couldn't connect Claude Code", $"{helper} is missing. Reinstalling Impious Bonum puts it back.");
            return;
        }

        var path = ClaudeCodeSetup.SettingsPath;
        try
        {
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            var (kind, command) = ClaudeCodeSetup.Inspect(existing);
            if (kind == ClaudeCodeSetup.StatusLine.Other)
            {
                var question =
                    $"Claude Code already has a status line:\n\n{command}\n\nReplace it with Impious Bonum's? It shows the model, context use "
                    + "and your plan usage, and passes the usage to the dashboard.\n\nTo keep yours, choose No: the user guide's Claude usage "
                    + "section shows the line to add to your own script.";
                var answer = owner is null
                    ? MessageBox.Show(question, "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    : MessageBox.Show(owner, question, "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var updated = ClaudeCodeSetup.Apply(existing, ClaudeCodeSetup.Command(helper, ClaudeCodeSetup.GitBashInstalled()));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (existing is not null)
                File.WriteAllText(path + ".impiousbonum.bak", existing);
            File.WriteAllText(path, updated);
            AppLog.Info($"Set Claude Code's status line in {path}");
            _tray?.ShowInfo("Claude Code connected", "Usage appears after Claude Code's next reply in a terminal. If Claude Code is already open, restart it.");
        }
        catch (JsonException ex)
        {
            _tray?.ShowError("Couldn't read Claude Code's settings", $"{path}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _tray?.ShowError("Couldn't update Claude Code's settings", ex.Message);
        }
    }

    private async Task ChangeSensorServiceAsync(bool install)
    {
        var error = await SensorServiceControl.RunAsync(install);
        AppLog.Info($"Sensor service {(install ? "install" : "removal")}: {error ?? "done"}");
        if (error is null)
            _tray?.ShowInfo("Impious Bonum", install ? "Sensor service installed. Temperatures will appear in a few seconds." : "Sensor service removed.");
        else if (error != "Cancelled.")
            _tray?.ShowError(install ? "Couldn't install the sensor service" : "Couldn't remove the sensor service", error);
    }

    private void ApplyLayout()
    {
        // While the editor is open it owns the layout (including our own saves, which trigger the file watcher).
        if (_layouts is null || _window is null || _editor is not null)
            return;
        ApplyExtraDashboards();

        LayoutDocument layout;
        try
        {
            layout = _layouts.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep showing the previous layout; an obviously broken file shouldn't blank the screen.
            _tray?.ShowError($"Couldn't read {Path.GetFileName(_layouts.ActivePath)}", ex.Message);
            if (_window.Dashboard.Children.Count > 0)
                return;
            layout = LayoutStore.LoadDefault();
        }

        var issues = LayoutValidator.Validate(layout, _layouts.Themes);
        if (issues.Count > 0)
            _tray?.ShowError($"{Path.GetFileName(_layouts.ActivePath)}: {issues.Count} problem{(issues.Count == 1 ? "" : "s")}", string.Join("\n", issues.Take(3)));

        var theme = _layouts.ResolveTheme(layout);
        _window.Background = Theme.From(theme).Background;
        _window.Dashboard.Build(layout, theme);
        _tablet?.Build(layout, theme);
        if (_settings.UsbScreen.Layout is null)
            _usb?.Build(layout, theme);
        else
            ApplyUsbScreen();
        Refresh();
    }

    /// <summary>Connects the USB screen (or not) and gives it its layout: its own, or the dashboard's.</summary>
    private void ApplyUsbScreen()
    {
        if (_usb is null || _layouts is null)
            return;
        var settings = _settings.UsbScreen;
        _usb.Apply(settings);
        if (!settings.Enabled)
            return;
        if (settings.Layout is { } name && _layouts.Library.Find(name) is { } found)
        {
            try
            {
                var layout = _layouts.Library.Load(found);
                _usb.Build(layout, _layouts.ResolveTheme(layout));
                return;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                AppLog.Warning($"USB screen: couldn't read layout \"{found}\": {ex.Message}");
            }
        }
        try
        {
            var current = _layouts.Load();
            _usb.Build(current, _layouts.ResolveTheme(current));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // ApplyLayout has already said what's wrong with it.
            AppLog.Warning($"USB screen: couldn't read the dashboard's layout: {ex.Message}");
        }
    }

    /// <summary>Tray → Display → Tablet view → Show on tablets. Turning it on shows the link to open.</summary>
    private void SetTabletView(bool on)
    {
        _settings.TabletView = on;
        _settings.Save();
        if (!on)
        {
            _tablet?.Stop();
            _tabletLink?.Close();
            AppLog.Info("Tablet view off");
            return;
        }
        if (StartTablet())
            ShowTabletLink();
    }

    private bool StartTablet()
    {
        if (_tablet is null)
            return false;
        if (string.IsNullOrEmpty(_settings.TabletToken))
        {
            _settings.TabletToken = TabletAccess.NewToken();
            _settings.Save();
        }

        try
        {
            _tablet.Start(_settings.TabletPort, _settings.TabletToken);
            AppLog.Info($"Tablet view on, port {_settings.TabletPort}");
            return true;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentOutOfRangeException)
        {
            // In use by another app, or a hand-edited port that isn't one.
            AppLog.Warning($"Tablet view couldn't use port {_settings.TabletPort}: {ex.Message}");
            _tray?.ShowError("Couldn't start the tablet view",
                $"Port {_settings.TabletPort} is in use or isn't a valid port. Change tabletPort in settings.json (tray → Open settings folder), then try again.");
            return false;
        }
    }

    private void ShowTabletLink()
    {
        if (_tabletLink is not null)
        {
            BringToFront(_tabletLink);
            return;
        }
        if (_tablet?.IsRunning != true || _settings.TabletToken is not { } token)
            return;

        _tabletLink = new TabletLinkWindow(_settings.TabletPort, token, NewTabletLink);
        _tabletLink.Closed += (_, _) => _tabletLink = null;
        _tabletLink.Show();
        BringToFront(_tabletLink);
    }

    private void CopyTabletLink()
    {
        if (_settings.TabletToken is not { } token)
            return;
        if (TabletLinkWindow.FirstLink(_settings.TabletPort, token) is not { } link)
            _tray?.ShowError("No tablet link", "This PC doesn't seem to be on a local network.");
        else if (TabletLinkWindow.CopyToClipboard(link))
            _tray?.ShowInfo("Tablet link copied", link);
    }

    /// <summary>A new secret for the link: tablets using the old one stop updating. Returns the new secret.</summary>
    private string NewTabletLink()
    {
        var token = TabletAccess.NewToken();
        _settings.TabletToken = token;
        _settings.Save();
        _tablet?.SetToken(token);
        AppLog.Info("Tablet view: new link");
        return token;
    }

    private void Place()
    {
        if (_window is null)
            return;

        var monitors = DisplayMonitor.GetAll();
        PlaceExtras(monitors);
        var monitor = DisplayMonitor.Resolve(_settings, monitors);
        if (monitor is null)
        {
            // Our monitor is unplugged or off. Wait for it rather than covering the main screen.
            _window.Hide();
            return;
        }

        if (_settings.MonitorId is null)
        {
            var windowed = _settings.PlaceFirstTime(monitor);
            _settings.MonitorId ??= monitor.Id;
            _settings.MonitorDevice ??= monitor.Device;
            _settings.Save();
            if (windowed)
            {
                AppLog.Info("No spare screen, so the dashboard starts as a window on the main screen");
                ApplyWindowBehaviour();
                _tray?.ShowInfo("The dashboard is a window for now",
                    "There's no spare screen, so it's on your main screen. Drag it where you like and resize it from its edges, then tray → Display → Lock position.");
            }
        }

        var b = monitor.Bounds;
        var rect = b;
        if (!_settings.Fill)
        {
            // Windowed: first time, the layout's shape centred on the monitor; after that, kept on screen.
            var saved = _settings.Area ?? WindowGeometry.DefaultArea(b, LayoutWidth, LayoutHeight);
            var area = WindowGeometry.Clamp(saved, b.Width, b.Height);
            if (area != _settings.Area)
            {
                _settings.Area = area;
                _settings.Save();
            }
            rect = new PixelRect(b.X + area.X, b.Y + area.Y, area.Width, area.Height);
        }

        _window.PlaceOn(rect);
        if (!_window.IsVisible && !_hiddenForFullscreen && !_hiddenByUser)
            _window.Show();
    }

    private double LayoutWidth => _window?.Dashboard.Width is > 0 and var w ? w : 1920;

    private double LayoutHeight => _window?.Dashboard.Height is > 0 and var h ? h : 480;

    private WindowOptions CurrentWindowOptions() =>
        new(!_settings.Fill, _settings.Locked, _settings.ClickThrough, _settings.AlwaysOnTop, _settings.HideOverFullscreen);

    private void ApplyWindowBehaviour()
    {
        var windowed = !_settings.Fill;
        _window?.SetBehaviour(
            movable: windowed && !_settings.Locked,
            clickThrough: windowed && _settings.Locked && _settings.ClickThrough,
            topmost: _settings.AlwaysOnTop);
        foreach (var extra in _extras)
            extra.SetBehaviour(movable: false, clickThrough: false, topmost: _settings.AlwaysOnTop);
    }

    /// <summary>Tray → Display: fill screen or windowed, lock, click-through, always on top.</summary>
    private void SetWindowOptions(WindowOptions options)
    {
        var becameWindowed = options.Windowed && _settings.Fill;
        _settings.Fill = !options.Windowed;
        // Unlock on the way into windowed mode so it can be put in place straight away.
        _settings.Locked = !becameWindowed && options.Locked;
        _settings.ClickThrough = options.ClickThrough;
        _settings.AlwaysOnTop = options.AlwaysOnTop;
        _settings.HideOverFullscreen = options.HideOverFullscreen;
        _settings.Save();
        AppLog.Info($"Window: {(options.Windowed ? "windowed" : "fill screen")}, {(_settings.Locked ? "locked" : "unlocked")}"
            + $"{(_settings.ClickThrough ? ", click-through" : "")}{(_settings.AlwaysOnTop ? ", on top" : "")}");

        ApplyWindowBehaviour();
        UpdateFullscreenHide();
        Place();
        if (becameWindowed)
            _tray?.ShowInfo("Dashboard windowed", "Drag it where you want and resize it from the edges, then tray → Display → Lock position.");
    }

    /// <summary>Back to the default windowed size, centred on the dashboard's monitor.</summary>
    private void ResetWindow()
    {
        _settings.Area = null;
        _settings.Save();
        Place();
        _window?.BringForward();
    }

    /// <summary>The window was dragged or resized: remember which monitor it's on and where.</summary>
    private void OnWindowMoved(PixelRect rect)
    {
        var monitors = DisplayMonitor.GetAll();
        var index = WindowGeometry.MostOverlapped(rect, monitors.Select(m => m.Bounds).ToList());
        if (index >= 0)
        {
            var monitor = monitors[index];
            var b = monitor.Bounds;
            _settings.MonitorId = monitor.Id;
            _settings.MonitorDevice = monitor.Device;
            _settings.Area = new PixelRect(rect.X - b.X, rect.Y - b.Y, rect.Width, rect.Height);
            _settings.Save();
        }
        // Dropped off every screen: Place puts it back where it was.
        Place();
    }

    /// <summary>
    /// With always on top and "hide over fullscreen apps", steps aside while a game or video fills the dashboard's
    /// monitor, rather than sitting over it (which can also cost the game frame pacing).
    /// </summary>
    private void UpdateFullscreenHide()
    {
        if (_window is null)
            return;
        UpdateExtrasFullscreenHide();

        var handle = new WindowInteropHelper(_window).Handle;
        var hide = handle != 0 && _editor is null && _settings.AlwaysOnTop && _settings.HideOverFullscreen
            && (_window.IsVisible || _hiddenForFullscreen) && FullscreenApp.IsInFrontOf(handle);
        if (hide == _hiddenForFullscreen)
            return;

        _hiddenForFullscreen = hide;
        if (hide)
            _window.Hide();
        else
            Place();
    }

    /// <summary>
    /// Makes a window for each of the extra dashboards (closing any no longer wanted) and gives each its layout. Called
    /// with <see cref="ApplyLayout"/>, so a layout saved or changed on disk shows on them too.
    /// </summary>
    private void ApplyExtraDashboards()
    {
        if (_layouts is null)
            return;
        var wanted = _settings.ExtraDashboards;
        while (_extras.Count > wanted.Count)
        {
            _extrasHiddenForFullscreen.Remove(_extras[^1]);
            _extras[^1].Close();
            _extras.RemoveAt(_extras.Count - 1);
        }
        while (_extras.Count < wanted.Count)
            _extras.Add(CreateExtraWindow());

        for (var i = 0; i < wanted.Count; i++)
        {
            LayoutDocument layout;
            var name = _layouts.Library.Find(wanted[i].Layout);
            try
            {
                layout = name is null ? LayoutStore.LoadDefault() : _layouts.Library.Load(name);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                AppLog.Warning($"Dashboard on {wanted[i].MonitorDevice}: couldn't read layout \"{name}\": {ex.Message}");
                layout = LayoutStore.LoadDefault();
            }
            if (name is null && _reportedMissingLinks.Add($"extra:{wanted[i].Layout}"))
                _tray?.ShowError("Impious Bonum", $"There's no layout called \"{wanted[i].Layout}\" any more. Pick another in tray → Display → More dashboards.");

            var theme = _layouts.ResolveTheme(layout);
            _extras[i].Background = Theme.From(theme).Background;
            _extras[i].Dashboard.Build(layout, theme);
        }
    }

    private DashboardWindow CreateExtraWindow()
    {
        var window = new DashboardWindow();
        window.EditRequested += (_, _) => OpenEditor();
        window.Touched += (_, _) => _lastTouch = DateTime.UtcNow;
        window.PeekRequested += (_, _) =>
        {
            _peekUntil = DateTime.UtcNow + Dimming.PeekFor;
            UpdateDimming();
        };
        window.ActionRequested += (_, action) => RunAction(action);
        window.SetBehaviour(movable: false, clickThrough: false, topmost: _settings.AlwaysOnTop);
        return window;
    }

    /// <summary>The extra dashboards showing the layout <paramref name="name"/>, which the editor's changes show on live.</summary>
    private IEnumerable<DashboardWindow> ExtrasShowing(string? name) =>
        _extras.Where((_, i) => i < _settings.ExtraDashboards.Count && string.Equals(_settings.ExtraDashboards[i].Layout, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fills each extra dashboard's monitor; one whose monitor isn't connected hides until it is.</summary>
    private void PlaceExtras(IReadOnlyList<DisplayMonitor> monitors)
    {
        for (var i = 0; i < _extras.Count && i < _settings.ExtraDashboards.Count; i++)
        {
            var window = _extras[i];
            if (_settings.ExtraDashboards[i].Resolve(monitors) is not { } monitor || _hiddenByUser || _extrasHiddenForFullscreen.Contains(window))
            {
                window.Hide();
                continue;
            }
            window.PlaceOn(monitor.Bounds);
            if (!window.IsVisible)
                window.Show();
        }
    }

    /// <summary>The extra dashboards step aside over fullscreen apps too, each for its own monitor.</summary>
    private void UpdateExtrasFullscreenHide()
    {
        var changed = false;
        foreach (var window in _extras)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var hidden = _extrasHiddenForFullscreen.Contains(window);
            var hide = handle != 0 && _editor is null && _settings.AlwaysOnTop && _settings.HideOverFullscreen
                && (window.IsVisible || hidden) && FullscreenApp.IsInFrontOf(handle);
            if (hide == hidden)
                continue;
            if (hide)
            {
                _extrasHiddenForFullscreen.Add(window);
                window.Hide();
            }
            else
            {
                _extrasHiddenForFullscreen.Remove(window);
                changed = true;
            }
        }
        if (changed)
            PlaceExtras(DisplayMonitor.GetAll());
    }

    private void MoveTo(DisplayMonitor monitor)
    {
        _settings.MonitorId = monitor.Id;
        _settings.MonitorDevice = monitor.Device;
        // One dashboard a screen: the main one takes the place of another already there.
        if (_settings.ExtraDashboards.FirstOrDefault(e => e.MonitorId == monitor.Id) is { } displaced)
        {
            _settings.ExtraDashboards = _settings.ExtraDashboards.Where(e => e != displaced).ToList();
            ApplyExtraDashboards();
            _tray?.ShowInfo("Impious Bonum", $"The dashboard showing \"{displaced.Layout}\" was on that screen, so it's been removed.");
        }
        _settings.Save();
        Place();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        // Raised off the UI thread, often several times while displays settle.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(750);
            Place();
        });

    private async Task ExitAsync()
    {
        if (await ReleaseEverythingAsync())
            Shutdown();
    }

    /// <summary>
    /// Closes the editor (which may ask to save), removes the tray icon and stops sampling.
    /// Returns false if the user cancelled the editor's save prompt, in which case nothing else is touched.
    /// </summary>
    private async Task<bool> ReleaseEverythingAsync()
    {
        _editor?.Close();
        if (_editor is not null)
            return false;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tick?.Stop();
        _updateTimer?.Stop();
        _layouts?.Dispose();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _tray?.Dispose();
        _tray = null;
        _tabletLink?.Close();
        _tablet?.Dispose();
        _usb?.Dispose();
        _usb = null;
        foreach (var extra in _extras)
            extra.Close();
        _extras.Clear();
        _window?.Close();
        if (_sampler is not null)
            await _sampler.DisposeAsync();
        _singleInstance?.ReleaseMutex();
        _singleInstance = null;
        return true;
    }

    /// <summary>Returns false if <paramref name="layoutName"/> isn't a saved layout.</summary>
    private static async Task<bool> RenderSnapshotAsync(string path, TimeSpan warmup, string? layoutName)
    {
        var settings = AppSettings.Load();
        var library = new LayoutLibrary(AppPaths.DataDirectory);
        library.EnsureSeeded();
        var name = layoutName is null ? library.Resolve(settings.ActiveLayout) : library.Find(layoutName);
        if (name is null)
            return false;

        await using var sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(settings.PingHost, claudeUsagePath: AppPaths.ClaudeUsageFile, weather: settings.WeatherRequest, customData: () => settings.CustomData, calendars: () => settings.Calendars, news: () => settings.NewsFeeds));
        sampler.Start();
        await Task.Delay(warmup);

        var layout = library.Load(name);

        var view = new DashboardView();
        view.Build(layout, new ThemeLibrary(AppPaths.DataDirectory).Resolve(layout.Theme));
        view.Refresh(sampler.Store, DateTime.Now);
        var size = new Size(layout.Width, layout.Height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)layout.Width, (int)layout.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var file = File.Create(path);
        encoder.Save(file);
        return true;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            result[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        }
        return result;
    }
}

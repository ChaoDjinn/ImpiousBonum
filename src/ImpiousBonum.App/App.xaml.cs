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
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Remote;
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
    private DashboardWindow? _window;
    private DispatcherTimer? _tick;
    private EditorWindow? _editor;
    private TabletView? _tablet;
    private TabletLinkWindow? _tabletLink;
    private readonly Updater _updater = new();
    private FrameRateTarget? _frameRateTarget;
    private readonly GameLayoutSwitcher _gameSwitcher = new(GameLayoutSwitcher.DefaultDelay);
    // "layout:<name>" or "theme:<name>" for links whose target has gone, so each is reported once.
    private readonly HashSet<string> _reportedMissingLinks = new(StringComparer.OrdinalIgnoreCase);
    private string? _gameLayoutApplied;
    private string? _recentApp;
    private DispatcherTimer? _updateTimer;
    private bool _warnedOutdatedService;
    private bool _mirrorQueued;
    private bool _started;
    private bool _reportedError;
    private bool _hiddenForFullscreen;

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
        _sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(_settings.PingHost, _frameRateTarget.Get));
        _sampler.Start();

        _window = new DashboardWindow();
        _window.EditRequested += (_, _) => OpenEditor();
        _window.PlacementChanged += (_, rect) => OnWindowMoved(rect);
        _tray = new TrayIcon();
        _tray.MenuOpening += (_, _) =>
        {
            var monitors = DisplayMonitor.GetAll();
            _tray.SetMonitors(monitors, DisplayMonitor.Resolve(_settings, monitors), CurrentWindowOptions());
            _tray.SetSensorState(SensorStatus(), SensorServiceControl.IsInstalled, SensorServiceControl.IsOutdated(SensorVersion()));
            _tray.SetFpsSources(monitors, _settings.FpsMonitorId);
            _tray.SetTabletView(_tablet?.IsRunning == true);
            if (_layouts is not null)
            {
                var names = _layouts.List();
                _tray.SetLayouts(names, _layouts.Active, _layouts.ActivePath);
                _tray.SetGameLayouts(_settings.GameLayoutsEnabled, _settings.GameLayouts, _recentApp, names, _layouts.Themes.List());
            }
        };
        _tray.LayoutSelected += (_, name) => SelectLayout(name);
        _tray.ImportLayoutRequested += (_, _) => ImportLayout();
        _tray.GameLayoutsToggled += (_, enabled) => SetGameLayoutsEnabled(enabled);
        _tray.GameLinkRequested += (_, rule) => LinkGame(rule);
        _tray.GameLinkRemoved += (_, rule) => UnlinkGame(rule);
        _tray.FpsSourceSelected += (_, monitorId) =>
        {
            _settings.FpsMonitorId = monitorId;
            _settings.Save();
        };
        _tray.MonitorSelected += (_, monitor) => MoveTo(monitor);
        _tray.WindowOptionsChanged += (_, options) => SetWindowOptions(options);
        _tray.ResetWindowRequested += (_, _) => ResetWindow();
        _tray.BringToFrontRequested += (_, _) => _window?.BringForward();
        _tray.TabletViewToggled += (_, on) => SetTabletView(on);
        _tray.TabletLinkRequested += (_, _) => ShowTabletLink();
        _tray.TabletLinkCopyRequested += (_, _) => CopyTabletLink();
        _tray.TabletNewLinkRequested += (_, _) => NewTabletLink();
        _tray.SensorServiceChangeRequested += async (_, install) => await ChangeSensorServiceAsync(install);
        _tray.ReloadRequested += (_, _) => ApplyLayout();
        _tray.EditLayoutRequested += (_, _) => OpenEditor();
        _tray.ExitRequested += async (_, _) => await ExitAsync();
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(userAsked: true);
        _tray.RestartToUpdateRequested += async (_, _) => await RestartToUpdateAsync();
        _tray.SetVersion(_updater.CurrentVersion, _updater.IsInstalled);
        StartUpdateChecks();

        _tablet = new TabletView();
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
        if (_settings.TabletView)
            StartTablet();

        ApplyWindowBehaviour();
        Place();
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
        _editor?.Refresh(_sampler!.Store, now);
        _tablet?.Refresh(_sampler!.Store, now);
        UpdateGameLayout();
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

        if (_settings.GameLayoutsEnabled && _gameSwitcher.Update(_settings.GameLayouts, app, DateTime.UtcNow))
            ApplyGameLayout();
    }

    /// <summary>Apps offered for linking never include the shell, whose windows come to the front when you use the tray.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp",
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ImpiousBonum",
    };

    /// <summary>Shows the layout and theme the game switcher wants, unless the editor has the layout or they're already showing.</summary>
    private void ApplyGameLayout()
    {
        if (_layouts is null || _editor is not null)
            return;

        var rule = _gameSwitcher.Current;
        var wanted = rule is null ? null : $"{rule.Layout}|{rule.Theme}";
        if (string.Equals(wanted, _gameLayoutApplied, StringComparison.OrdinalIgnoreCase))
            return;
        _gameLayoutApplied = wanted;

        var layout = rule?.Layout;
        var theme = rule?.Theme;
        if (rule is not null && layout is not null && _layouts.Library.Find(layout) is null)
        {
            ReportMissingLink(rule, "layout", layout);
            layout = null;
        }
        if (rule is not null && theme is not null && _layouts.Themes.Find(theme) is null)
        {
            ReportMissingLink(rule, "theme", theme);
            theme = null;
        }

        AppLog.Info(rule is null ? "No linked game in front; back to the chosen layout" : $"\"{rule.Process}\" is in front; switching to {rule.Describe()}");
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

    private void ReportMissingLink(GameLayoutRule rule, string kind, string name)
    {
        if (!_reportedMissingLinks.Add($"{kind}:{name}"))
            return;
        AppLog.Warning($"Game {kind} for \"{rule.Process}\" ignored: there's no {kind} called \"{name}\"");
        _tray?.ShowError($"Game {kind} not found", $"\"{rule.Process}\" is linked to the {kind} \"{name}\", which doesn't exist any more. Tray → Layout → Game layouts.");
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
            Refresh();
        }, DispatcherPriority.Background);
    }

    private string SensorStatus() =>
        _sampler is not null && _sampler.Store.TryGet(SensorHostProvider.Status, out var status) && status.Text is { } text
            ? text
            : SensorHostProvider.NotRunning;

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
        Refresh();
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
        var monitor = DisplayMonitor.Resolve(_settings, monitors);
        if (monitor is null)
        {
            // Our monitor is unplugged or off. Wait for it rather than covering the main screen.
            _window.Hide();
            return;
        }

        if (_settings.MonitorId is null)
        {
            _settings.MonitorId = monitor.Id;
            _settings.MonitorDevice = monitor.Device;
            _settings.Save();
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
        if (!_window.IsVisible && !_hiddenForFullscreen)
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

    private void MoveTo(DisplayMonitor monitor)
    {
        _settings.MonitorId = monitor.Id;
        _settings.MonitorDevice = monitor.Device;
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
        _tray?.Dispose();
        _tray = null;
        _tabletLink?.Close();
        _tablet?.Dispose();
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

        await using var sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(settings.PingHost));
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

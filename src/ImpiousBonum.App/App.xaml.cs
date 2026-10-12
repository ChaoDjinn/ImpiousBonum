using System.IO;
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
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Logging;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using Microsoft.Win32;

namespace ImpiousBonum.App;

/// <summary>
/// Command line:
///   --data-dir &lt;dir&gt;     use a different settings/layout folder (default %AppData%\ImpiousBonum)
///   --snapshot &lt;file.png&gt; render the layout to a PNG after a short warm-up, then exit
///   --layout &lt;name&gt;       with --snapshot, render this saved layout instead of the active one
///   --warmup &lt;seconds&gt;    how long to sample before a snapshot (default 3)
///   --demo                made-up readings instead of this PC's, for screenshots (with --snapshot, at 21:47);
///                         see build/screenshots.ps1
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
    // The dashboard the editor is on: null for the main one, else an extra dashboard's monitor id.
    private string? _editingScreen;
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
            var rendered = await RenderSnapshotAsync(snapshotPath, TimeSpan.FromSeconds(warmup), args.GetValueOrDefault("layout"), args.ContainsKey("demo"));
            Shutdown(rendered ? 0 : 1);
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, AppPaths.InstanceName, out var isFirstInstance);
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
        _sampler = args.ContainsKey("demo")
            ? DemoSampler()
            : new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(_settings.PingHost, _frameRateTarget.Get, AppPaths.ClaudeUsageFile, () => _settings.WeatherRequest(), () => _settings.CustomData, () => _settings.Calendars,
                () => _settings.NewsFeeds,
                () => _trackedGame, recap => Dispatcher.BeginInvoke(() => OnSessionEnded(recap))));
        _sampler.Start();

        _window = new DashboardWindow();
        _window.EditRequested += (_, _) => EditDashboard(null);
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
        ConnectTray(_tray);
        _tray.RestartToUpdateRequested += async (_, _) => await RestartToUpdateAsync();
        _tray.SetVersion(_updater.CurrentVersion, _updater.IsInstalled);
        StartUpdateChecks();
        if (_settings.LoadProblem is { } problem)
        {
            AppLog.Warning($"Started with default settings: {problem}");
            _tray.ShowError("Settings couldn't be read", "Impious Bonum started with its default settings. Click for dashboard.log, which has the details.", TrayIcon.ShowLog);
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
            _tray?.ShowError("Impious Bonum hit an error", "It's still running. Click for dashboard.log, which has the details.", TrayIcon.ShowLog);
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

    /// <summary>A sampler with nothing to sample: the made-up readings stay as they are, for screenshots.</summary>
    private static Sampler DemoSampler()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, DateTime.Now, DemoArtwork.Png());
        return new Sampler(store, []);
    }

    /// <summary>Returns false if <paramref name="layoutName"/> isn't a saved layout.</summary>
    /// <param name="demo">Made-up readings (<see cref="DemoReadings"/>) instead of sampling this PC's.</param>
    private static async Task<bool> RenderSnapshotAsync(string path, TimeSpan warmup, string? layoutName, bool demo)
    {
        var settings = AppSettings.Load();
        var library = new LayoutLibrary(AppPaths.DataDirectory);
        library.EnsureSeeded();
        var name = layoutName is null ? library.Resolve(settings.ActiveLayout) : library.Find(layoutName);
        if (name is null)
            return false;

        await using var sampler = demo ? null : new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(settings.PingHost, claudeUsagePath: AppPaths.ClaudeUsageFile, weather: settings.WeatherRequest, customData: () => settings.CustomData, calendars: () => settings.Calendars, news: () => settings.NewsFeeds));
        var store = sampler?.Store ?? new MetricStore();
        var now = DateTime.Now;
        if (sampler is not null)
        {
            sampler.Start();
            await Task.Delay(warmup);
        }
        else
        {
            now = DateTime.Today.AddHours(21).AddMinutes(47);
            DemoReadings.Fill(store, now, DemoArtwork.Png());
        }

        var layout = library.Load(name);

        var view = new DashboardView { AnimateBackground = false };
        view.Build(layout, new ThemeLibrary(AppPaths.DataDirectory).Resolve(layout.Theme));
        view.Refresh(store, now);
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

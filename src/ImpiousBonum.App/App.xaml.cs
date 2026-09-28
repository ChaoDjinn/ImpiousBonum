using System.IO;
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
using Microsoft.Win32;

namespace ImpiousBonum.App;

/// <summary>
/// Command line:
///   --data-dir &lt;dir&gt;     use a different settings/layout folder (default %AppData%\ImpiousBonum)
///   --snapshot &lt;file.png&gt; render the layout to a PNG after a short warm-up, then exit
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
    private readonly Updater _updater = new();
    private DispatcherTimer? _updateTimer;
    private bool _warnedOutdatedService;
    private bool _mirrorQueued;
    private bool _started;
    private bool _reportedError;

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
            await RenderSnapshotAsync(snapshotPath, TimeSpan.FromSeconds(warmup));
            Shutdown();
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
        var frameRateTarget = new FrameRateTarget(() => _settings.FpsMonitorId);
        _sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(_settings.PingHost, frameRateTarget.Get));
        _sampler.Start();

        _window = new DashboardWindow();
        _window.EditRequested += (_, _) => OpenEditor();
        _tray = new TrayIcon();
        _tray.MenuOpening += (_, _) =>
        {
            var monitors = DisplayMonitor.GetAll();
            _tray.SetMonitors(monitors, DisplayMonitor.Resolve(_settings, monitors));
            _tray.SetSensorState(SensorStatus(), SensorServiceControl.IsInstalled, SensorServiceControl.IsOutdated(SensorVersion()));
            _tray.SetFpsSources(monitors, _settings.FpsMonitorId);
        };
        _tray.FpsSourceSelected += (_, monitorId) =>
        {
            _settings.FpsMonitorId = monitorId;
            _settings.Save();
        };
        _tray.MonitorSelected += (_, monitor) => MoveTo(monitor);
        _tray.SensorServiceChangeRequested += async (_, install) => await ChangeSensorServiceAsync(install);
        _tray.ReloadRequested += (_, _) => ApplyLayout();
        _tray.EditLayoutRequested += (_, _) => OpenEditor();
        _tray.ExitRequested += async (_, _) => await ExitAsync();
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(userAsked: true);
        _tray.RestartToUpdateRequested += async (_, _) => await RestartToUpdateAsync();
        _tray.SetVersion(_updater.CurrentVersion, _updater.IsInstalled);
        StartUpdateChecks();

        _layouts = new LayoutStore();
        _layouts.Changed += (_, _) => ApplyLayout();
        ApplyLayout();

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

        var session = new LayoutSession(LoadSavedLayout());
        _editor = new EditorWindow(session, LoadSavedLayout, () =>
            DisplayMonitor.Resolve(_settings, DisplayMonitor.GetAll()) is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height) : null);

        session.Changed += (_, change) => MirrorToDashboard(session, change);
        _editor.SaveRequested += (_, layout) => _layouts?.Save(layout);
        _editor.Closed += (_, _) =>
        {
            _editor = null;
            _window?.EndEdit();
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

    private LayoutDocument LoadSavedLayout()
    {
        try
        {
            return _layouts!.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return LayoutStore.LoadDefault();
        }
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
            return;
        }

        if (_mirrorQueued)
            return;
        _mirrorQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _mirrorQueued = false;
            _window.Background = Theme.From(session.Document.Theme).Background;
            _window.Dashboard.Build(session.Document);
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
            _tray?.ShowError("Couldn't read layout.json", ex.Message);
            if (_window.Dashboard.Children.Count > 0)
                return;
            layout = LayoutStore.LoadDefault();
        }

        var issues = LayoutValidator.Validate(layout);
        if (issues.Count > 0)
            _tray?.ShowError($"layout.json: {issues.Count} problem{(issues.Count == 1 ? "" : "s")}", string.Join("\n", issues.Take(3)));

        _window.Background = Theme.From(layout.Theme).Background;
        _window.Dashboard.Build(layout);
        Refresh();
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
        var rect = _settings.Fill || _settings.Area is not { } area
            ? b
            : new PixelRect(b.X + area.X, b.Y + area.Y, area.Width, area.Height);

        _window.PlaceOn(rect);
        if (!_window.IsVisible)
            _window.Show();
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
        _window?.Close();
        if (_sampler is not null)
            await _sampler.DisposeAsync();
        _singleInstance?.ReleaseMutex();
        _singleInstance = null;
        return true;
    }

    private static async Task RenderSnapshotAsync(string path, TimeSpan warmup)
    {
        var settings = AppSettings.Load();
        await using var sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(settings.PingHost));
        sampler.Start();
        await Task.Delay(warmup);

        LayoutDocument layout;
        using (var layouts = new LayoutStore())
            layout = layouts.Load();

        var view = new DashboardView();
        view.Build(layout);
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

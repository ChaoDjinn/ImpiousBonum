using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = ParseArgs(e.Args);
        if (args.TryGetValue("data-dir", out var dataDir))
            AppPaths.UseDataDirectory(dataDir);

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

        _settings = AppSettings.Load();
        if (!_settings.HardwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var frameRateTarget = new FrameRateTarget(() => _settings.FpsMonitorId);
        _sampler = new Sampler(new MetricStore(), Sampler.CreateDefaultProviders(_settings.PingHost, frameRateTarget.Get));
        _sampler.Start();

        _window = new DashboardWindow();
        _tray = new TrayIcon();
        _tray.MenuOpening += (_, _) =>
        {
            var monitors = DisplayMonitor.GetAll();
            _tray.SetMonitors(monitors, DisplayMonitor.Resolve(_settings, monitors));
            _tray.SetSensorState(SensorStatus(), SensorServiceControl.IsInstalled);
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
        _tray.ExitRequested += async (_, _) => await ExitAsync();

        _layouts = new LayoutStore();
        _layouts.Changed += (_, _) => ApplyLayout();
        ApplyLayout();

        Place();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Refresh();
        Refresh();
        // Line ticks up with the wall clock so the minute flips on time.
        await Task.Delay(1000 - DateTime.Now.Millisecond);
        Refresh();
        _tick.Start();
    }

    private void Refresh() => _window?.Dashboard.Refresh(_sampler!.Store, DateTime.Now);

    private string SensorStatus() =>
        _sampler is not null && _sampler.Store.TryGet(SensorHostProvider.Status, out var status) && status.Text is { } text
            ? text
            : SensorHostProvider.NotRunning;

    private async Task ChangeSensorServiceAsync(bool install)
    {
        var error = await SensorServiceControl.RunAsync(install);
        if (error is null)
            _tray?.ShowInfo("Impious Bonum", install ? "Sensor service installed. Temperatures will appear in a few seconds." : "Sensor service removed.");
        else if (error != "Cancelled.")
            _tray?.ShowError(install ? "Couldn't install the sensor service" : "Couldn't remove the sensor service", error);
    }

    private void ApplyLayout()
    {
        if (_layouts is null || _window is null)
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
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tick?.Stop();
        _layouts?.Dispose();
        _tray?.Dispose();
        _window?.Close();
        if (_sampler is not null)
            await _sampler.DisposeAsync();
        _singleInstance?.ReleaseMutex();
        Shutdown();
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

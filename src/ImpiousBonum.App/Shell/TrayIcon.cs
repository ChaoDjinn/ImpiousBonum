using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImpiousBonum.App.Shell;

/// <summary>The notification-area icon: the only UI besides the dashboard itself until the layout editor exists.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ToolStripMenuItem _displays = new("Display");
    private readonly ToolStripMenuItem _fpsSource = new("FPS from");
    private readonly ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _sensors = new("Sensors");
    private readonly ToolStripMenuItem _sensorStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _sensorInstall = new();
    private readonly ToolStripMenuItem _sensorRemove = new("Remove sensor service…");

    public TrayIcon()
    {
        _image = DrawIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Impious Bonum") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_displays);
        menu.Items.Add(_sensors);
        menu.Items.Add(_fpsSource);
        menu.Items.Add("Edit layout…", null, (_, _) => Open(AppPaths.LayoutFile));
        menu.Items.Add("Open settings folder", null, (_, _) => Open(AppPaths.DataDirectory));
        menu.Items.Add("Reload layout", null, (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _sensors.DropDownItems.Add(_sensorStatus);
        _sensors.DropDownItems.Add(_sensorInstall);
        _sensors.DropDownItems.Add(_sensorRemove);
        // Installing over an existing service replaces it, so "Update" is the same operation.
        _sensorInstall.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, true);
        _sensorRemove.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, false);

        _startup.Checked = StartupRegistration.IsEnabled;
        _startup.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startup.Checked);
        menu.Opening += (_, _) =>
        {
            _startup.Checked = StartupRegistration.IsEnabled;
            MenuOpening?.Invoke(this, EventArgs.Empty);
        };

        _icon = new NotifyIcon { Icon = _image, Text = "Impious Bonum", ContextMenuStrip = menu, Visible = true };
    }

    public event EventHandler? MenuOpening;

    public event EventHandler? ReloadRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<DisplayMonitor>? MonitorSelected;

    /// <summary>A monitor id, or null for "whichever app is in the foreground".</summary>
    public event EventHandler<string?>? FpsSourceSelected;

    public void SetFpsSources(IReadOnlyList<DisplayMonitor> monitors, string? currentMonitorId)
    {
        _fpsSource.DropDownItems.Clear();
        var foreground = new ToolStripMenuItem("Foreground app") { Checked = currentMonitorId is null };
        foreground.Click += (_, _) => FpsSourceSelected?.Invoke(this, null);
        _fpsSource.DropDownItems.Add(foreground);
        _fpsSource.DropDownItems.Add(new ToolStripSeparator());

        foreach (var monitor in monitors)
        {
            var item = new ToolStripMenuItem($"Top app on {monitor.DisplayName}") { Checked = monitor.Id == currentMonitorId };
            item.Click += (_, _) => FpsSourceSelected?.Invoke(this, monitor.Id);
            _fpsSource.DropDownItems.Add(item);
        }
    }

    /// <summary>True to install the sensor service, false to remove it.</summary>
    public event EventHandler<bool>? SensorServiceChangeRequested;

    public void SetSensorState(string status, bool serviceInstalled)
    {
        _sensorStatus.Text = status;
        _sensorInstall.Text = serviceInstalled ? "Update sensor service…" : "Install sensor service…";
        _sensorRemove.Visible = serviceInstalled;
    }

    public void SetMonitors(IReadOnlyList<DisplayMonitor> monitors, DisplayMonitor? current)
    {
        _displays.DropDownItems.Clear();
        foreach (var monitor in monitors)
        {
            var item = new ToolStripMenuItem(monitor.DisplayName) { Checked = monitor.Id == current?.Id };
            item.Click += (_, _) => MonitorSelected?.Invoke(this, monitor);
            _displays.DropDownItems.Add(item);
        }
    }

    public void ShowError(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Warning);

    public void ShowInfo(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No handler registered for .json; fall back to Notepad.
            Process.Start("notepad.exe", $"\"{path}\"");
        }
    }

    /// <summary>A small orange tile with three bars, drawn at runtime so there is no binary asset to maintain.</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tile = new GraphicsPath();
            tile.AddArc(1, 1, 10, 10, 180, 90);
            tile.AddArc(21, 1, 10, 10, 270, 90);
            tile.AddArc(21, 21, 10, 10, 0, 90);
            tile.AddArc(1, 21, 10, 10, 90, 90);
            tile.CloseFigure();
            using var orange = new SolidBrush(Color.FromArgb(0xFF, 0x98, 0x00));
            g.FillPath(orange, tile);
            g.FillRectangle(Brushes.Black, 7, 17, 4, 8);
            g.FillRectangle(Brushes.Black, 14, 9, 4, 16);
            g.FillRectangle(Brushes.Black, 21, 13, 4, 12);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

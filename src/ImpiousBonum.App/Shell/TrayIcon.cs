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
    private readonly ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = true };

    public TrayIcon()
    {
        _image = DrawIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Impious Bonum") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_displays);
        menu.Items.Add("Edit layout…", null, (_, _) => Open(AppPaths.LayoutFile));
        menu.Items.Add("Open settings folder", null, (_, _) => Open(AppPaths.DataDirectory));
        menu.Items.Add("Reload layout", null, (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

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

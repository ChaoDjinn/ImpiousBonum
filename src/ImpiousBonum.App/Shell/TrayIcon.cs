using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImpiousBonum.App.Shell;

/// <summary>The notification-area icon and its menu: displays, layouts, sensors, FPS source, the layout editor, updates and exit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ToolStripMenuItem _displays = new("Display");
    private readonly ToolStripMenuItem _layouts = new("Layout");
    private readonly ToolStripMenuItem _gameLayouts = new("Game layouts");
    private readonly ToolStripMenuItem _fpsSource = new("FPS from");
    private readonly ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _sensors = new("Sensors");
    private readonly ToolStripMenuItem _sensorStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _sensorInstall = new();
    private readonly ToolStripMenuItem _sensorRemove = new("Remove sensor service…");
    private readonly ToolStripMenuItem _header = new("Impious Bonum") { Enabled = false };
    private readonly ToolStripMenuItem _checkUpdates = new("Check for updates") { Visible = false };
    private readonly ToolStripMenuItem _restartToUpdate = new() { Visible = false };
    private string? _activeLayoutPath;

    public TrayIcon()
    {
        _image = DrawIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_header);
        menu.Items.Add(_restartToUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_displays);
        menu.Items.Add(_layouts);
        menu.Items.Add(_sensors);
        menu.Items.Add(_fpsSource);
        menu.Items.Add(new ToolStripMenuItem("Edit layout…", null, (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty)) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add("Open layout file", null, (_, _) =>
        {
            if (_activeLayoutPath is not null)
                Open(_activeLayoutPath);
        });
        menu.Items.Add("Open settings folder", null, (_, _) => Open(AppPaths.DataDirectory));
        menu.Items.Add("Reload layout", null, (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_startup);
        menu.Items.Add(_checkUpdates);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _sensors.DropDownItems.Add(_sensorStatus);
        _sensors.DropDownItems.Add(_sensorInstall);
        _sensors.DropDownItems.Add(_sensorRemove);
        // Installing over an existing service replaces it, so "Update" is the same operation.
        _sensorInstall.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, true);
        _sensorRemove.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, false);

        _restartToUpdate.Font = new Font(menu.Font, FontStyle.Bold);
        _restartToUpdate.Click += (_, _) => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);
        _checkUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        _startup.Checked = StartupRegistration.IsEnabled;
        _startup.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startup.Checked);
        menu.Opening += (_, _) =>
        {
            _startup.Checked = StartupRegistration.IsEnabled;
            MenuOpening?.Invoke(this, EventArgs.Empty);
        };

        _icon = new NotifyIcon { Icon = _image, Text = "Impious Bonum", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? MenuOpening;

    public event EventHandler? ReloadRequested;

    /// <summary>Open the layout editor (menu, or double-click the icon).</summary>
    public event EventHandler? EditLayoutRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<DisplayMonitor>? MonitorSelected;

    public event EventHandler? CheckForUpdatesRequested;

    public event EventHandler? RestartToUpdateRequested;

    /// <summary>Shows the version in the menu header and, for installed copies, the update items.</summary>
    public void SetVersion(string? version, bool updatesAvailable)
    {
        _header.Text = version is null ? "Impious Bonum (development build)" : $"Impious Bonum {version}";
        _checkUpdates.Visible = updatesAvailable;
    }

    /// <summary>A downloaded update is waiting; offer to restart into it.</summary>
    public void SetUpdateReady(string? version)
    {
        _restartToUpdate.Visible = version is not null;
        _restartToUpdate.Text = $"Restart to update to {version}";
    }

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

    public void SetSensorState(string status, bool serviceInstalled, bool outdated)
    {
        _sensorStatus.Text = outdated ? "Sensor service is from an older version" : status;
        _sensorInstall.Text = serviceInstalled ? "Update sensor service…" : "Install sensor service…";
        _sensorInstall.Font = outdated ? new Font(_sensorInstall.Owner?.Font ?? SystemFonts.MenuFont!, FontStyle.Bold) : null;
        _sensorRemove.Visible = serviceInstalled;
    }

    /// <summary>A saved layout's name.</summary>
    public event EventHandler<string>? LayoutSelected;

    public void SetLayouts(IReadOnlyList<string> names, string active, string activePath)
    {
        _activeLayoutPath = activePath;
        _layouts.DropDownItems.Clear();
        foreach (var name in names)
        {
            var item = new ToolStripMenuItem(Escape(name)) { Checked = name == active };
            item.Click += (_, _) => LayoutSelected?.Invoke(this, name);
            _layouts.DropDownItems.Add(item);
        }
        _layouts.DropDownItems.Add(new ToolStripSeparator());
        _layouts.DropDownItems.Add("Import…", null, (_, _) => ImportLayoutRequested?.Invoke(this, EventArgs.Empty));
        _layouts.DropDownItems.Add(_gameLayouts);
    }

    /// <summary>Tray → Layout → Import…</summary>
    public event EventHandler? ImportLayoutRequested;

    /// <summary>Tray → Layout → Game layouts → Switch automatically.</summary>
    public event EventHandler<bool>? GameLayoutsToggled;

    /// <summary>Link a process name to a layout or a theme.</summary>
    public event EventHandler<GameLayoutRule>? GameLinkRequested;

    public event EventHandler<GameLayoutRule>? GameLinkRemoved;

    /// <summary>
    /// Fills the Game layouts submenu. <paramref name="recentApp"/> is the last app seen in front (the tray itself is in
    /// front while its menu is open) and is offered for linking to any of the saved layouts in <paramref name="names"/>,
    /// or to any of the <paramref name="themes"/> to restyle whichever layout is showing.
    /// </summary>
    public void SetGameLayouts(bool enabled, IReadOnlyList<GameLayoutRule> rules, string? recentApp, IReadOnlyList<string> names, IReadOnlyList<string> themes)
    {
        _gameLayouts.DropDownItems.Clear();
        var toggle = new ToolStripMenuItem("Switch automatically") { Checked = enabled };
        toggle.Click += (_, _) => GameLayoutsToggled?.Invoke(this, !enabled);
        _gameLayouts.DropDownItems.Add(toggle);

        if (recentApp is { } app)
        {
            var linked = GameLayoutSwitcher.Match(rules, app);
            var link = new ToolStripMenuItem($"Link \"{Escape(app)}\" to");
            foreach (var name in names)
            {
                var item = new ToolStripMenuItem(Escape(name)) { Checked = linked?.Theme is null && string.Equals(name, linked?.Layout, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => GameLinkRequested?.Invoke(this, new GameLayoutRule(app, name));
                link.DropDownItems.Add(item);
            }

            var themed = new ToolStripMenuItem("Theme only") { ToolTipText = "Keep the layout that's showing and restyle it with a theme" };
            foreach (var theme in themes)
            {
                var item = new ToolStripMenuItem(Escape(theme)) { Checked = linked?.Layout is null && string.Equals(theme, linked?.Theme, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => GameLinkRequested?.Invoke(this, new GameLayoutRule(app, null, theme));
                themed.DropDownItems.Add(item);
            }
            link.DropDownItems.Add(new ToolStripSeparator());
            link.DropDownItems.Add(themed);
            _gameLayouts.DropDownItems.Add(link);
        }

        if (rules.Count == 0)
            return;
        _gameLayouts.DropDownItems.Add(new ToolStripSeparator());
        foreach (var rule in rules)
        {
            var item = new ToolStripMenuItem($"{Escape(rule.Process)} → {Escape(rule.Describe())}");
            item.DropDownItems.Add("Remove link", null, (_, _) => GameLinkRemoved?.Invoke(this, rule));
            _gameLayouts.DropDownItems.Add(item);
        }
    }

    // "&" would otherwise underline the next letter as a mnemonic.
    private static string Escape(string text) => text.Replace("&", "&&");

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
            using var orange = new SolidBrush(Color.FromArgb(0xFF, 0xAA, 0x00));
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

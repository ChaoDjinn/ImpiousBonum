using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// The notification-area icon, its notifications and its menu. The menu keeps to the basics (open the app, edit or
/// switch the layout, restart into an update, exit); everything else is in the app's window.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ToolStripMenuItem _layouts = new("Layout") { ToolTipText = "What the main dashboard shows" };
    private readonly ToolStripMenuItem _header = new("Impious Bonum") { Enabled = false, Tag = TrayMenuStyle.HeaderTag.Instance };
    private readonly ToolStripMenuItem _restartToUpdate = new() { Visible = false };
    private readonly Font _bold;

    /// <summary>What clicking the notification showing now does, if anything.</summary>
    private Action? _onNotificationClicked;

    public TrayIcon()
    {
        _image = LoadIcon();

        // The bold item is what double-clicking the icon does.
        var menu = new ContextMenuStrip();
        _bold = new Font(menu.Font, FontStyle.Bold);
        _header.Font = _bold;
        _header.Image = AppIconBitmap(menu.DeviceDpi);
        menu.Items.Add(_header);
        menu.Items.Add(_restartToUpdate.WithIcon(TrayMenuStyle.Icons.Restart));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Impious Bonum", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = _bold,
            ToolTipText = "Layouts, screens, readings, hotkeys, updates and the rest",
        }.WithIcon(TrayMenuStyle.Icons.Settings));
        menu.Items.Add(new ToolStripMenuItem("Edit layout…", null, (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.Edit));
        menu.Items.Add(_layouts.WithIcon(TrayMenuStyle.Icons.Layout));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)).WithIcon(TrayMenuStyle.Icons.Power));

        _restartToUpdate.Font = _bold;
        _restartToUpdate.Click += (_, _) => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);
        menu.Opening += (_, _) => MenuOpening?.Invoke(this, EventArgs.Empty);

        // Dark or light with the taskbar, rounded, with icons: Windows 11's look rather than Windows Forms' default.
        TrayMenuStyle.Attach(menu);

        _icon = new NotifyIcon { Icon = _image, Text = "Impious Bonum", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClicked += (_, _) => _onNotificationClicked?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _onNotificationClicked = null;
    }

    public event EventHandler? MenuOpening;

    /// <summary>Open the app's window (the bold item, or double-click the icon).</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Open the window on the layout editor, for the main dashboard.</summary>
    public event EventHandler? EditLayoutRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? RestartToUpdateRequested;

    /// <summary>Shows the version in the menu header.</summary>
    public void SetVersion(string? version) =>
        // The name, with the version on the right in the secondary colour.
        _header.ShortcutKeyDisplayString = version ?? "development build";

    /// <summary>A downloaded update is waiting; offer to restart into it.</summary>
    public void SetUpdateReady(string? version)
    {
        _restartToUpdate.Visible = version is not null;
        _restartToUpdate.Text = $"Restart to update to {version}";
    }

    /// <summary>A saved layout's name, to show on the main dashboard.</summary>
    public event EventHandler<string>? LayoutSelected;

    public void SetLayouts(IReadOnlyList<string> names, string active)
    {
        _layouts.DropDownItems.Clear();
        foreach (var name in names)
        {
            // "&" would otherwise underline the next letter as a mnemonic.
            var item = new ToolStripMenuItem(name.Replace("&", "&&")) { Checked = name == active };
            item.Click += (_, _) => LayoutSelected?.Invoke(this, name);
            _layouts.DropDownItems.Add(item);
        }
    }

    /// <param name="onClick">What clicking the notification does, e.g. show the log it mentions.</param>
    public void ShowError(string title, string message, Action? onClick = null) =>
        Notify(5000, title, message, ToolTipIcon.Warning, onClick);

    /// <summary>An alert: a notification that stands out from the app's own messages.</summary>
    public void ShowAlert(string message) =>
        Notify(10000, "Impious Bonum alert", message, ToolTipIcon.Warning, null);

    /// <summary>A weather warning: it stands out like an alert, and stays as long.</summary>
    /// <param name="onClick">What clicking it does: open the warning's page.</param>
    public void ShowWarning(string title, string message, Action? onClick) =>
        Notify(10000, title, message, ToolTipIcon.Warning, onClick);

    /// <param name="onClick">What clicking the notification does, e.g. restart into a downloaded update.</param>
    public void ShowInfo(string title, string message, Action? onClick = null) =>
        Notify(5000, title, message, ToolTipIcon.Info, onClick);

    private void Notify(int timeout, string title, string message, ToolTipIcon icon, Action? onClick)
    {
        _onNotificationClicked = onClick;
        _icon.ShowBalloonTip(timeout, title, message, icon);
    }

    /// <summary>Opens the settings folder with dashboard.log selected, for notifications about errors.</summary>
    public static void ShowLog()
    {
        var arguments = File.Exists(AppPaths.LogFile) ? $"/select,\"{AppPaths.LogFile}\"" : $"\"{AppPaths.DataDirectory}\"";
        try
        {
            Process.Start("explorer.exe", arguments);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No Explorer to show it in; the notification has already said where the log is.
        }
    }

    /// <summary>
    /// The app's own icon (assets/ImpiousBonum.ico, built in) at the size the notification area wants at the screen's
    /// scaling, so it stays sharp at 125% or 150% rather than being one 32-pixel drawing scaled down.
    /// </summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource)!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    internal const string IconResource = "ImpiousBonum.icon.ico";

    /// <summary>The app's icon at menu-icon size for this <paramref name="dpi"/>, for the menu's header.</summary>
    private static Bitmap AppIconBitmap(int dpi)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource)!;
        var size = (int)Math.Round(16 * dpi / 96f);
        using var icon = new Icon(stream, size, size);
        return icon.ToBitmap();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
        _bold.Dispose();
    }
}

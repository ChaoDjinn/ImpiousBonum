using System.IO;
using System.Text.Json;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.App.Shell;

/// <summary>Per-machine settings: which monitor the dashboard lives on and where on it, how its window behaves, which layout it shows, and game layouts.</summary>
public sealed class AppSettings
{
    private List<GameLayoutRule> _gameLayouts = [];

    /// <summary>Stable monitor interface path (survives reboots and display renumbering).</summary>
    public string? MonitorId { get; set; }

    /// <summary>GDI device name such as \\.\DISPLAY7; a fallback when the id can't be matched.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Fill the whole monitor. When false, <see cref="Area"/> is used.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Area relative to the monitor's top-left, in physical pixels.</summary>
    public PixelRect? Area { get; set; }

    /// <summary>When windowed, stops the window being dragged or resized.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>When windowed and locked, clicks pass through to whatever is underneath.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Keep the dashboard above other windows.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>With <see cref="AlwaysOnTop"/>, hide while a fullscreen app is in front on the dashboard's monitor.</summary>
    public bool HideOverFullscreen { get; set; } = true;

    /// <summary>Name of the saved layout to show (a file in the layouts folder), or null for "Default".</summary>
    public string? ActiveLayout { get; set; }

    public string PingHost { get; set; } = "1.1.1.1";

    /// <summary>Games linked to layouts or themes, shown while the game is in front (see <see cref="GameLayoutSwitcher"/>).</summary>
    public List<GameLayoutRule> GameLayouts
    {
        get => _gameLayouts;
        set => _gameLayouts = value ?? [];
    }

    /// <summary>Switch to a game's linked layout automatically. Off leaves the links in place but unused.</summary>
    public bool GameLayoutsEnabled { get; set; } = true;

    /// <summary>Monitor whose top app the FPS reading follows (a <see cref="DisplayMonitor.Id"/>), or null for the foreground app.</summary>
    public string? FpsMonitorId { get; set; }

    /// <summary>
    /// GPU-accelerated drawing. Off by default: the dashboard redraws once a second, and software rendering
    /// avoids loading the graphics driver into the process, which saves a lot of memory.
    /// </summary>
    public bool HardwareRendering { get; set; }

    /// <summary>Serve the dashboard to browsers on the local network (tray → Display → Tablet view). Off by default.</summary>
    public bool TabletView { get; set; }

    public int TabletPort { get; set; } = TabletServer.DefaultPort;

    /// <summary>The secret in the tablet link. Created when the tablet view is first turned on; replaced by "New link".</summary>
    public string? TabletToken { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonDefaults.Options) is { } settings)
            {
                // Hand edits: drop links missing a process, or both a layout and a theme, rather than tripping over them later.
                settings.GameLayouts.RemoveAll(rule => rule is null || !rule.IsUsable);
                return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt settings file shouldn't stop the dashboard starting; it will be rewritten on next save.
        }

        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonDefaults.Options));
    }
}

public sealed record PixelRect(int X, int Y, int Width, int Height);

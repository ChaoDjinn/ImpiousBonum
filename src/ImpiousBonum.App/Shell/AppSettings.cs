using System.IO;
using System.Text.Json;

namespace ImpiousBonum.App.Shell;

/// <summary>Per-machine settings: which monitor the dashboard lives on and where on it.</summary>
public sealed class AppSettings
{
    /// <summary>Stable monitor interface path (survives reboots and display renumbering).</summary>
    public string? MonitorId { get; set; }

    /// <summary>GDI device name such as \\.\DISPLAY7; a fallback when the id can't be matched.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Fill the whole monitor. When false, <see cref="Area"/> is used.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Area relative to the monitor's top-left, in physical pixels.</summary>
    public PixelRect? Area { get; set; }

    public string PingHost { get; set; } = "1.1.1.1";

    /// <summary>Monitor whose top app the FPS reading follows (a <see cref="DisplayMonitor.Id"/>), or null for the foreground app.</summary>
    public string? FpsMonitorId { get; set; }

    /// <summary>
    /// GPU-accelerated drawing. Off by default: the dashboard redraws once a second, and software rendering
    /// avoids loading the graphics driver into the process, which saves a lot of memory.
    /// </summary>
    public bool HardwareRendering { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonDefaults.Options) ?? new();
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

using System.IO;

namespace ImpiousBonum.App;

/// <summary>Where settings and layouts live. Defaults to %AppData%\ImpiousBonum; <c>--data-dir</c> overrides it for testing.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImpiousBonum");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogFile => Path.Combine(DataDirectory, "dashboard.log");

    public static void UseDataDirectory(string directory) => DataDirectory = Path.GetFullPath(directory);

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}

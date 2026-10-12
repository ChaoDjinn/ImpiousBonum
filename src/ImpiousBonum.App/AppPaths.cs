using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ImpiousBonum.App;

/// <summary>Where settings and layouts live. Defaults to %AppData%\ImpiousBonum; <c>--data-dir</c> overrides it for testing.</summary>
public static class AppPaths
{
    private static readonly string Default = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImpiousBonum");

    public static string DataDirectory { get; private set; } = Default;

    /// <summary>
    /// Names the single-instance lock. Each data folder has its own, so a copy run with <c>--data-dir</c> (to test, or to
    /// take screenshots) can run alongside the installed one instead of quitting.
    /// </summary>
    public static string InstanceName => string.Equals(DataDirectory, Default, StringComparison.OrdinalIgnoreCase)
        ? @"Local\ImpiousBonum"
        : $@"Local\ImpiousBonum-{Hash(DataDirectory)}";

    /// <summary>Set by a second copy started while this one runs, asking it to open its window.</summary>
    public static string OpenSignalName => InstanceName + ".Open";

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..16];

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogFile => Path.Combine(DataDirectory, "dashboard.log");

    /// <summary>Where logging (the Logging page) writes its daily CSV files.</summary>
    public static string LogsFolder => Path.Combine(DataDirectory, "logs");

    /// <summary>One line per game session (see <see cref="Core.Sessions.SessionLog"/>).</summary>
    public static string SessionsFile => Path.Combine(DataDirectory, "sessions.csv");

    /// <summary>Written by Claude Code's status line (<see cref="Core.Claude.ClaudeStatusLine"/>), which always uses the default folder.</summary>
    public static string ClaudeUsageFile => Path.Combine(DataDirectory, Core.Claude.ClaudeUsageFile.FileName);

    public static void UseDataDirectory(string directory) => DataDirectory = Path.GetFullPath(directory);

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}

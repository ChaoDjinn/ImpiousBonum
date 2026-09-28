using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ImpiousBonum.Core.Sensors;
using Microsoft.Win32;

namespace ImpiousBonum.App.Shell;

/// <summary>Installs or removes the sensor service by running the bundled host elevated (one UAC prompt).</summary>
public static class SensorServiceControl
{
    private const int ErrorCancelled = 1223;

    public static string HostPath => Path.Combine(AppContext.BaseDirectory, "sensors", "ImpiousBonum.Sensors.exe");

    /// <summary>Version of the host shipped with this copy of the app (what "Install/Update" would install).</summary>
    public static string? BundledVersion
    {
        get
        {
            var dll = Path.ChangeExtension(HostPath, ".dll");
            return File.Exists(dll) ? FileVersionInfo.GetVersionInfo(dll).ProductVersion : null;
        }
    }

    /// <summary>True when the running service is a different build from the one bundled with the app (e.g. after an app update).</summary>
    public static bool IsOutdated(string? runningVersion) =>
        runningVersion is not null && BundledVersion is { } bundled && !string.Equals(runningVersion, bundled, StringComparison.Ordinal);

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{SensorProtocol.ServiceName}");
            return key is not null;
        }
    }

    /// <summary>Runs <c>install</c> or <c>uninstall</c> elevated. Returns null on success, or a message describing what went wrong.</summary>
    public static async Task<string?> RunAsync(bool install)
    {
        if (!File.Exists(HostPath))
            return $"Sensor host not found at {HostPath}.";

        try
        {
            using var process = Process.Start(new ProcessStartInfo(HostPath, install ? "install" : "uninstall")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return "Couldn't start the sensor host.";

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : $"The sensor host exited with code {process.ExitCode}.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return "Cancelled.";
        }
    }
}

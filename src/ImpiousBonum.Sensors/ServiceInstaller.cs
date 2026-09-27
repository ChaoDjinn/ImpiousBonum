using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Sensors;

/// <summary>
/// Installs the host as an auto-start LocalSystem service. The files are copied to Program Files first:
/// a SYSTEM service must never run from a folder ordinary users can write to, or anyone could swap the exe and gain SYSTEM.
/// </summary>
internal static class ServiceInstaller
{
    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Impious Bonum", "Sensors");

    public static int Install()
    {
        if (!EnsureAdmin())
            return 1;

        StopAndDelete(quiet: true);

        var source = AppContext.BaseDirectory;
        Console.WriteLine($"Copying sensor host to {InstallDirectory}");
        CopyDirectory(source, InstallDirectory);

        var exe = Path.Combine(InstallDirectory, "ImpiousBonum.Sensors.exe");
        if (Sc("create", SensorProtocol.ServiceName, $"binPath= \"{exe}\"", "start= auto", "DisplayName= \"Impious Bonum Sensors\"") != 0)
            return 1;
        Sc("description", SensorProtocol.ServiceName, "\"Reads hardware sensors (temperatures, fans, power) for the Impious Bonum dashboard.\"");
        // Restart after a crash: 5s, 30s, then give up until the next day's reset.
        Sc("failure", SensorProtocol.ServiceName, "reset= 86400", "actions= restart/5000/restart/30000//");

        if (Sc("start", SensorProtocol.ServiceName) != 0)
            return 1;

        Console.WriteLine("Sensor service installed and started.");
        return 0;
    }

    public static int Uninstall()
    {
        if (!EnsureAdmin())
            return 1;

        StopAndDelete(quiet: false);

        if (Directory.Exists(InstallDirectory) && !IsSameDirectory(AppContext.BaseDirectory, InstallDirectory))
        {
            try
            {
                Directory.Delete(InstallDirectory, recursive: true);
                var parent = Path.GetDirectoryName(InstallDirectory)!;
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Couldn't remove {InstallDirectory}: {ex.Message}");
            }
        }

        Console.WriteLine("Sensor service removed.");
        return 0;
    }

    public static bool IsInstalled()
    {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == SensorProtocol.ServiceName);
        return service is not null;
    }

    private static void StopAndDelete(bool quiet)
    {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == SensorProtocol.ServiceName);
        if (service is null)
        {
            if (!quiet)
                Console.WriteLine("Sensor service isn't installed.");
            return;
        }

        if (service.Status != ServiceControllerStatus.Stopped)
        {
            Console.WriteLine("Stopping sensor service…");
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }

        Sc("delete", SensorProtocol.ServiceName);
        // The SCM releases the exe shortly after deletion; give it a moment before files are touched.
        Thread.Sleep(500);
    }

    private static bool EnsureAdmin()
    {
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return true;
        Console.Error.WriteLine("This needs administrator rights. Run it from an elevated prompt, or use the dashboard's tray menu.");
        return false;
    }

    private static int Sc(params string[] args)
    {
        var info = new ProcessStartInfo("sc.exe", string.Join(' ', args)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            Console.Error.WriteLine($"sc {args[0]} failed ({process.ExitCode}): {output.Trim()}");
        return process.ExitCode;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsSameDirectory(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App;

// Restoring a backup. The settings are read once at startup and held all over the app (the dashboards, the tablet view,
// hotkeys, what the sampler fetches, even whether it draws in software), so rather than reload each piece in place, it
// closes everything, swaps the files while nothing can write over them, and starts again.
public partial class App
{
    private async Task RestoreBackupAsync(string path)
    {
        if (!await ReleaseEverythingAsync())
            return;

        AppLog.Info($"Restoring the backup {Path.GetFileName(path)}");
        try
        {
            var before = SettingsBackup.Restore(path, AppPaths.DataDirectory, DateTimeOffset.Now);
            AppLog.Info($"Restored the backup; the setup from before is in {before}");
            MessageBox.Show($"Restored the backup. Impious Bonum starts again now to use it.\n\nYour setup from before is saved in {before}.",
                "Impious Bonum", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"Couldn't restore the backup: {ex.Message}");
            MessageBox.Show($"Couldn't restore the backup: {ex.Message}", "Impious Bonum", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        StartAgain();
    }

    /// <summary>Starts a new copy with the same command line (--data-dir and all), then closes this one, which has let go of everything.</summary>
    private void StartAgain()
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The app's own path isn't known."));
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
                start.ArgumentList.Add(argument);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Warning($"Couldn't start again: {ex.Message}");
            MessageBox.Show("Impious Bonum couldn't start again by itself. Start it from the Start menu.", "Impious Bonum", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Shutdown();
    }
}

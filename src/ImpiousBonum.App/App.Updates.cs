using System.Windows.Threading;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App;

// Update checks for installed copies, and restarting into a downloaded update.
public partial class App
{
    /// <summary>Installed copies check GitHub shortly after starting and then every few hours; development builds never do.</summary>
    private void StartUpdateChecks()
    {
        if (!_updater.IsInstalled)
            return;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesAsync(userAsked: false);
        };
        _updateTimer.Start();
    }

    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (userAsked)
            _tray?.ShowInfo("Impious Bonum", "Checking for updates…");

        AppLog.Info("Checking for updates");
        var ready = await _updater.CheckAndDownloadAsync();
        _tray?.SetUpdateReady(ready);
        if (ready is not null)
            _tray?.ShowInfo($"Impious Bonum {ready} is ready", "Click here, or tray → Restart to update, to restart into it.", () => _ = RestartToUpdateAsync());
        else if (userAsked)
            _tray?.ShowInfo("Impious Bonum", $"You're up to date ({_updater.CurrentVersion}).");
    }

    private async Task RestartToUpdateAsync()
    {
        AppLog.Info($"Restarting to update to {_updater.ReadyVersion}");
        if (await ReleaseEverythingAsync())
            _updater.RestartToUpdate();
    }
}

using System.Windows.Threading;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App;

// Update checks for installed copies, and restarting into a downloaded update.
public partial class App
{
    private bool _checkingForUpdates;
    private DateTime? _demoChecked;
    private int? _demoProgress;
    private string? _demoReady;

    /// <summary>Installed copies check GitHub shortly after starting and then every few hours; development builds never do.</summary>
    private void StartUpdateChecks()
    {
        if (!_updater.IsInstalled)
            return;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesAsync(notify: true);
        };
        _updateTimer.Start();
    }

    /// <summary>
    /// Checks for an update and downloads it. The About page shows how it's going; <paramref name="notify"/> (for the
    /// checks it makes by itself) also says with a notification when one is ready.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool notify)
    {
        if (_checkingForUpdates)
            return;
        if (_demo)
        {
            await FindDemoUpdateAsync();
            return;
        }
        _checkingForUpdates = true;
        AppLog.Info("Checking for updates");
        string? ready;
        try
        {
            ready = await _updater.CheckAndDownloadAsync();
        }
        finally
        {
            _checkingForUpdates = false;
        }

        _tray?.SetUpdateReady(ready);
        if (ready is not null && notify)
        {
            _tray?.ShowInfo($"Impious Bonum {ready} is ready", "Click here to restart into it now, or later from the tray or the About page.",
                () => _ = RestartToUpdateAsync());
        }
    }

    /// <summary>
    /// With --demo: an update to look at, found and downloaded in a few seconds, for screenshots of the About page and
    /// to try it on a build that can't update itself. Restarting into it only says it's a demo.
    /// </summary>
    private async Task FindDemoUpdateAsync()
    {
        _checkingForUpdates = true;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        for (var percent = 0; percent <= 100; percent += 20)
        {
            _demoProgress = percent;
            await Task.Delay(TimeSpan.FromSeconds(0.6));
        }
        var version = typeof(App).Assembly.GetName().Version ?? new Version(0, 1, 0);
        _demoReady = $"{version.Major}.{version.Minor + 1}.0";
        (_checkingForUpdates, _demoProgress, _demoChecked) = (false, null, DateTime.Now);
        _tray?.SetUpdateReady(_demoReady);
    }

    /// <summary>Where updating has got to, for the About page.</summary>
    private UpdateStatus UpdateStatus() => _demo
        ? new(true, _checkingForUpdates, _demoReady, _demoChecked, false, _checkingForUpdates ? _demoProgress : null)
        : new(_updater.IsInstalled, _checkingForUpdates, _updater.ReadyVersion, _updater.LastChecked, _updater.LastCheckFailed,
            _checkingForUpdates ? _updater.DownloadProgress : null);

    private async Task RestartToUpdateAsync()
    {
        if (_demo)
        {
            _tray?.ShowInfo("Impious Bonum", "This is a demo, so there's nothing to install. An installed copy would restart into the update now.");
            return;
        }
        AppLog.Info($"Restarting to update to {_updater.ReadyVersion}");
        if (await ReleaseEverythingAsync())
            _updater.RestartToUpdate();
    }
}

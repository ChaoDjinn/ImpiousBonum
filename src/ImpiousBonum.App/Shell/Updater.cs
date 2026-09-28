using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Checks GitHub Releases for new versions, downloads them in the background and applies them on request.
/// Does nothing when running from a build folder rather than an installed copy.
/// </summary>
public sealed class Updater
{
    public const string RepositoryUrl = "https://github.com/ChaoDjinn/ImpiousBonum";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));
    private VelopackAsset? _ready;

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Version downloaded and waiting for a restart, if any.</summary>
    public string? ReadyVersion => (_ready ?? _manager.UpdatePendingRestart)?.Version?.ToString();

    /// <summary>Checks for a newer release and downloads it. Returns the new version when one is ready to apply.</summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return null;

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                AppLog.Info(ReadyVersion is { } ready ? $"{ready} is downloaded and waiting for a restart" : $"Up to date ({CurrentVersion})");
                return ReadyVersion;
            }

            AppLog.Info($"Downloading {update.TargetFullRelease.Version}{(update.DeltasToTarget.Any() ? " (delta)" : " (full)")}");
            await _manager.DownloadUpdatesAsync(update, null, cancellationToken).ConfigureAwait(false);
            _ready = update.TargetFullRelease;
            AppLog.Info($"Downloaded {ReadyVersion}; ready to apply");
            return ReadyVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Offline, GitHub rate limits and the like: try again next time.
            Trace.TraceWarning($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Exits, installs the downloaded version and starts it again.</summary>
    public void RestartToUpdate()
    {
        if ((_ready ?? _manager.UpdatePendingRestart) is { } asset)
            _manager.ApplyUpdatesAndRestart(asset);
    }
}

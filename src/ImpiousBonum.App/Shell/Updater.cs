using System.Diagnostics;
using System.IO;
using System.Net.Http;
using ImpiousBonum.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Checks GitHub Releases for new versions, downloads them in the background and applies them on request.
/// Only releases signed with our update key are downloaded (see <see cref="ReleaseSignature"/>).
/// Does nothing when running from a build folder rather than an installed copy.
/// </summary>
public sealed class Updater
{
    public const string RepositoryUrl = "https://github.com/ChaoDjinn/ImpiousBonum";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));
    private readonly byte[] _publicKey = LoadPublicKey();
    private VelopackAsset? _ready;

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Verified version downloaded and waiting for a restart, if any.</summary>
    public string? ReadyVersion => _ready?.Version?.ToString();

    /// <summary>Checks for a newer signed release and downloads it. Returns the new version when one is ready to apply.</summary>
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

            var target = update.TargetFullRelease;
            if (!await IsSignedAsync(target, cancellationToken).ConfigureAwait(false))
                return ReadyVersion;

            // Velopack checks the package it ends up with (downloaded whole or rebuilt from deltas) against target.SHA256,
            // which the signature has just vouched for.
            AppLog.Info($"Downloading {target.Version}{(update.DeltasToTarget.Any() ? " (delta)" : " (full)")}");
            await _manager.DownloadUpdatesAsync(update, null, cancellationToken).ConfigureAwait(false);
            _ready = target;
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
        if (_ready is { } asset)
            _manager.ApplyUpdatesAndRestart(asset);
    }

    private async Task<bool> IsSignedAsync(VelopackAsset target, CancellationToken cancellationToken)
    {
        var version = target.Version?.ToString() ?? "";
        if (_publicKey.Length == 0)
        {
            AppLog.Warning($"Not updating to {version}: this build has no update signing key.");
            return false;
        }

        var url = $"{RepositoryUrl}/releases/download/v{version}/{ReleaseSignature.AssetName(version)}";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Also the case for a few seconds while a release is being published, before its signature is uploaded.
            AppLog.Warning($"Not updating to {version}: no signature ({(int)response.StatusCode}).");
            return false;
        }

        var document = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (ReleaseSignature.Verify(document, _publicKey, version, target.FileName ?? "", target.SHA256))
        {
            AppLog.Info($"{version} is signed with the update key");
            return true;
        }

        AppLog.Warning($"Not updating to {version}: its signature doesn't match {target.FileName}.");
        return false;
    }

    /// <summary>The public half of the update signing key, built in from assets/update-signing-key.pub.</summary>
    private static byte[] LoadPublicKey()
    {
        using var stream = typeof(Updater).Assembly.GetManifestResourceStream("ImpiousBonum.update-signing-key.pub");
        if (stream is null)
            return [];

        using var reader = new StreamReader(stream);
        try
        {
            return Convert.FromBase64String(reader.ReadToEnd().Trim());
        }
        catch (FormatException)
        {
            return [];
        }
    }
}

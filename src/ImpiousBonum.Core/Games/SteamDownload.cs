using System.Globalization;

namespace ImpiousBonum.Core.Games;

/// <summary>Where a Steam download has got to, best first: what's going now, then what's waiting.</summary>
public enum SteamDownloadState
{
    Downloading,

    /// <summary>Downloaded, and being written into the game's folder.</summary>
    Installing,

    Paused,

    /// <summary>Started, and waiting its turn in Steam's queue (or for Steam to start).</summary>
    Queued,
}

/// <summary>
/// A game Steam is downloading, updating or installing, as its <c>appmanifest_&lt;id&gt;.acf</c> says: how much is done
/// of how much, which while <see cref="SteamDownloadState.Installing"/> is how much has been written into its folder.
/// </summary>
public sealed record SteamDownload(int AppId, string Name, SteamDownloadState State, long Done, long Total)
{
    /// <summary>Bytes a second, from how <see cref="Done"/> has grown between Steam's writes; null until it has grown, or once it stops.</summary>
    public double? Speed { get; init; }

    /// <summary>How far it has got, 0 to 100; null if Steam hasn't said how big it is.</summary>
    public double? Percent => Total > 0 ? Math.Clamp(100.0 * Done / Total, 0, 100) : null;

    /// <summary>How long the rest will take at <see cref="Speed"/>; null without a speed.</summary>
    public TimeSpan? TimeLeft => Speed is { } speed && speed > 0 && Total > Done ? TimeSpan.FromSeconds((Total - Done) / speed) : null;

    /// <summary>The state in words, for the <c>steam.download.state</c> reading and the downloads widget.</summary>
    public static string StateName(SteamDownloadState state) => state switch
    {
        SteamDownloadState.Downloading => "Downloading",
        SteamDownloadState.Installing => "Installing",
        SteamDownloadState.Paused => "Paused",
        _ => "Queued",
    };

    // Steam's EAppState flags, as StateFlags in the manifest holds them.
    private const int UpdateRequired = 2;
    private const int UpdateRunning = 256;
    private const int UpdatePaused = 512;
    private const int UpdateStarted = 1024;
    private const int Preallocating = 1 << 19;
    private const int Downloading = 1 << 20;
    private const int Staging = 1 << 21;
    private const int Committing = 1 << 22;

    /// <summary>The flags that mean a download has been started, whether or not it's running now.</summary>
    private const int Started = UpdateRunning | UpdatePaused | UpdateStarted | Preallocating | Downloading | Staging | Committing;

    /// <summary>
    /// The download a manifest's <c>AppState</c> describes, or null if there's none: the game is installed and up to
    /// date, or has an update waiting that hasn't been started (Steam's "update when launched").
    /// </summary>
    internal static SteamDownload? FromManifest(VdfBlock state)
    {
        if (!int.TryParse(state.Text("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            return null;
        var flags = Number(state, "StateFlags");
        var toDownload = Number(state, "BytesToDownload");
        var downloaded = Number(state, "BytesDownloaded");
        var toStage = Number(state, "BytesToStage");
        var staged = Number(state, "BytesStaged");
        var partly = (flags & UpdateRequired) != 0 && downloaded > 0 && downloaded < toDownload;
        if ((flags & Started) == 0 && !partly)
            return null;

        var name = state.Text("name") is { Length: > 0 } named ? named.Trim() : state.Text("installdir") ?? appId.ToString(CultureInfo.InvariantCulture);
        var downloadedAll = toDownload <= 0 || downloaded >= toDownload;
        var installing = (flags & (Staging | Committing)) != 0 || (downloadedAll && toStage > 0 && staged < toStage && (flags & UpdateRunning) != 0);
        var stateNow = (flags & UpdatePaused) != 0 ? SteamDownloadState.Paused
            : installing ? SteamDownloadState.Installing
            : (flags & (UpdateRunning | Downloading | Preallocating)) != 0 ? SteamDownloadState.Downloading
            : SteamDownloadState.Queued;
        return stateNow == SteamDownloadState.Installing && toStage > 0
            ? new SteamDownload(appId, name, stateNow, staged, toStage)
            : new SteamDownload(appId, name, stateNow, downloaded, toDownload);
    }

    private static long Number(VdfBlock block, string key) =>
        long.TryParse(block.Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
}

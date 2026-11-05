using System.Globalization;

namespace ImpiousBonum.Core.Games;

/// <summary>Where a Steam download has got to, best first: what's going now, then what's waiting.</summary>
public enum SteamDownloadState
{
    Downloading,

    /// <summary>Downloaded, and being written into the game's folder.</summary>
    Installing,

    /// <summary>Stopped partway.</summary>
    Paused,

    /// <summary>Started, and waiting its turn in Steam's queue.</summary>
    Queued,
}

/// <summary>
/// A game Steam is downloading, updating or installing: how much is done of how much, which while
/// <see cref="SteamDownloadState.Installing"/> is how much has been written into its folder.
/// </summary>
public sealed record SteamDownload(int AppId, string Name, SteamDownloadState State, long Done, long Total)
{
    /// <summary>Bytes a second, from how far it got between Steam's writes; null until it has moved, or once it stops.</summary>
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
}

/// <summary>
/// What a game's <c>appmanifest_&lt;id&gt;.acf</c> says about downloading it, as Steam last wrote it: its state flags
/// and how many bytes there are to download and to write into its folder, and how many are done.
/// </summary>
/// <remarks>
/// Steam's flags are its EAppState values. In practice a download that's going has UpdateStarted (1024) set, along with
/// UpdateRequired (2), and loses it when it's paused or stops, keeping its partial byte counts; the more particular
/// flags (UpdateRunning, Downloading and so on) are seldom written. So the flags tell going from stopped, and Steam's
/// own <c>Updating</c> value for the game in the registry, which it keeps up to the moment, settles it when there is one.
/// </remarks>
public sealed record SteamManifest(int AppId, string Name, int Flags, long ToDownload, long Downloaded, long ToStage, long Staged)
{
    private const int UpdateRequired = 2;
    private const int UpdateRunning = 256;
    private const int UpdatePaused = 512;
    private const int UpdateStarted = 1024;
    private const int Preallocating = 1 << 19;
    private const int Downloading = 1 << 20;
    private const int Staging = 1 << 21;
    private const int Committing = 1 << 22;

    private const int Going = UpdateStarted | UpdateRunning | Preallocating | Downloading | Staging | Committing;

    /// <summary>
    /// Whether a download has been started, whether or not it's going now; one that hasn't, such as an update Steam is
    /// holding until the game's launched, isn't a download at all.
    /// </summary>
    public bool Started => (Flags & (Going | UpdatePaused)) != 0 || ((Flags & UpdateRequired) != 0 && Downloaded > 0 && Downloaded < ToDownload);

    /// <summary>The manifest's <c>AppState</c>, or null if it has no app id.</summary>
    internal static SteamManifest? Parse(VdfBlock state)
    {
        if (!int.TryParse(state.Text("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            return null;
        var name = state.Text("name") is { Length: > 0 } named ? named.Trim() : state.Text("installdir") ?? appId.ToString(CultureInfo.InvariantCulture);
        return new SteamManifest(appId, name, (int)Number(state, "StateFlags"),
            Number(state, "BytesToDownload"), Number(state, "BytesDownloaded"), Number(state, "BytesToStage"), Number(state, "BytesStaged"));
    }

    /// <summary>
    /// The download now, or null if none has been started. <paramref name="updating"/> is Steam's own word on whether
    /// it's working on the game, from the registry; null where Steam doesn't say, and then the flags decide.
    /// </summary>
    public SteamDownload? Download(bool? updating)
    {
        if (!Started)
            return null;
        var going = updating ?? ((Flags & Going) != 0 && (Flags & UpdatePaused) == 0);
        var downloadedAll = ToDownload <= 0 || Downloaded >= ToDownload;
        var installing = going && ((Flags & (Staging | Committing)) != 0 || (downloadedAll && ToStage > 0 && Staged < ToStage));
        var state = installing ? SteamDownloadState.Installing
            : going ? SteamDownloadState.Downloading
            // Stopped partway is paused; started and not begun is waiting its turn.
            : (Flags & UpdatePaused) != 0 || Downloaded > 0 ? SteamDownloadState.Paused
            : SteamDownloadState.Queued;
        return state == SteamDownloadState.Installing && ToStage > 0
            ? new SteamDownload(AppId, Name, state, Staged, ToStage)
            : new SteamDownload(AppId, Name, state, Downloaded, ToDownload);
    }

    private static long Number(VdfBlock block, string key) =>
        long.TryParse(block.Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
}

using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// What Steam is downloading, updating or installing, from the manifest it keeps for each game
/// (<c>appmanifest_&lt;id&gt;.acf</c>), which it rewrites as a download goes: the list as an attachment (a list of
/// <see cref="SteamDownload"/>) for the downloads widget, and readings for the first in it. Nothing is fetched and
/// Steam needn't be running. Every few seconds it looks at when each manifest was last written, which listing Steam's
/// folders gives for free, and reads only the ones that changed; for the few downloads started, it asks the registry
/// whether Steam is working on them now. There's no reading while nothing is downloading.
/// </summary>
public sealed class SteamDownloadsProvider : IMetricProvider
{
    /// <summary>The downloads, best first (see <see cref="SteamDownloadState"/>), as a <see cref="MetricStore"/> attachment; null while there are none.</summary>
    public const string List = "steam.downloads.list";

    public const string Count = "steam.downloads";
    public const string Game = "steam.download.game";
    public const string State = "steam.download.state";
    public const string Progress = "steam.download.progress";
    public const string Done = "steam.download.done";
    public const string Size = "steam.download.size";
    public const string Speed = "steam.download.speed";
    public const string TimeLeft = "steam.download.left";

    /// <summary>A download whose progress hasn't moved for this long has no speed: Steam may be between files, or stuck.</summary>
    public static readonly TimeSpan SpeedLapse = TimeSpan.FromMinutes(2);

    private readonly IGameFiles _files;
    private readonly SteamLibrary _steam;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Seen> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _libraries = [];
    private DateTimeOffset _librariesRead = DateTimeOffset.MinValue;

    /// <param name="files">Steam's files; the real ones unless a test swaps them.</param>
    public SteamDownloadsProvider(IGameFiles? files = null, TimeProvider? time = null)
    {
        _files = files ?? new GameFiles();
        _steam = new SteamLibrary(_files);
        _time = time ?? TimeProvider.System;
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Count, "Steam: games downloading", "Steam", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Game, "Steam download: game", "Steam", MetricUnit.Text));
        store.Register(new MetricDefinition(State, "Steam download: state", "Steam", MetricUnit.Text));
        store.Register(new MetricDefinition(Progress, "Steam download: progress", "Steam", MetricUnit.Percent, 100, "0"));
        store.Register(new MetricDefinition(Done, "Steam download: done", "Steam", MetricUnit.Bytes));
        store.Register(new MetricDefinition(Size, "Steam download: size", "Steam", MetricUnit.Bytes));
        store.Register(new MetricDefinition(Speed, "Steam download: speed", "Steam", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(TimeLeft, "Steam download: time left", "Steam", MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        // Libraries are added rarely, and reading them means reading libraryfolders.vdf.
        if (now - _librariesRead >= GameFinder.LibraryLifetime)
        {
            _libraries = _steam.LibraryPaths();
            _librariesRead = now;
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in _libraries)
        {
            foreach (var (path, written) in _files.FilesWritten(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
            {
                present.Add(path);
                _manifests.TryGetValue(path, out var seen);
                if (seen is not null && seen.Written == written)
                    continue;
                // One that can't be read now (Steam writing it, say) is read again next time, rather than forgotten.
                if (_files.ReadText(path) is not { } text)
                    continue;
                _manifests[path] = Seen.After(seen, written, SteamLibrary.DownloadManifest(text));
            }
        }
        foreach (var gone in _manifests.Keys.Where(path => !present.Contains(path)).ToList())
            _manifests.Remove(gone);

        // Only the few downloads that have been started ask Steam whether they're going.
        Publish(store, [.. _manifests.Values
            .Where(seen => seen.Manifest is { Started: true })
            .Select(seen => seen.Now(now, _files.SteamUpdating(seen.Manifest!.AppId)))
            .OfType<SteamDownload>()
            .OrderBy(download => download.State)
            .ThenBy(download => download.Name, StringComparer.CurrentCultureIgnoreCase)]);
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes the downloads and the first one's readings, or clears them all for none; the demo readings use it too.</summary>
    public static void Publish(MetricStore store, IReadOnlyList<SteamDownload> downloads)
    {
        var first = downloads.Count > 0 ? downloads[0] : null;
        store.SetAttachment(List, first is null ? null : downloads);
        store.Set(Count, first is null ? null : downloads.Count);
        store.SetText(Game, first?.Name);
        store.SetText(State, first is null ? null : SteamDownload.StateName(first.State));
        store.Set(Progress, first?.Percent);
        store.Set(Done, first?.Done);
        store.Set(Size, first is { Total: > 0 } ? first.Total : null);
        store.Set(Speed, first?.Speed);
        store.SetText(TimeLeft, first?.TimeLeft is { } left ? DurationText.Format(left) : null);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// A manifest as last read: when Steam wrote it, what it said, and since when its download has been as far along
    /// as it is (by Steam's writes, not by when the app looked), with the speed it got there at.
    /// </summary>
    private sealed record Seen(DateTime Written, SteamManifest? Manifest, DateTimeOffset? Since, double? Speed)
    {
        /// <summary>The manifest as just read, after <paramref name="before"/>: the speed is how far it came between Steam's two writes.</summary>
        public static Seen After(Seen? before, DateTime written, SteamManifest? manifest)
        {
            // A file's time is only known when the file system gives one.
            DateTimeOffset? at = written > DateTime.MinValue ? new DateTimeOffset(DateTime.SpecifyKind(written, DateTimeKind.Utc)) : null;
            if (manifest is null || before is null || before.Manifest is not { } was || was.AppId != manifest.AppId || manifest.Downloaded < was.Downloaded)
                return new Seen(written, manifest, at, null);
            if (manifest.Downloaded == was.Downloaded)
                return before with { Written = written, Manifest = manifest };
            var seconds = at is { } now && before.Since is { } since ? (now - since).TotalSeconds : 0;
            return new Seen(written, manifest, at, seconds > 0 ? (manifest.Downloaded - was.Downloaded) / seconds : null);
        }

        /// <summary>The download now, with its speed while it's downloading and has moved lately.</summary>
        public SteamDownload? Now(DateTimeOffset now, bool? updating) =>
            Manifest?.Download(updating) is not { } download ? null
            : download.State == SteamDownloadState.Downloading && Speed is not null && Since is { } since && now - since < SpeedLapse ? download with { Speed = Speed }
            : download;
    }
}

using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// What's playing in any app that shows in Windows' media controls (Spotify, browsers, Media Player…): title, artist,
/// album, progress, and the album art as an attachment. Nothing has a reading while nothing is playing or paused, so
/// widgets that hide without a reading disappear.
/// </summary>
public sealed class MediaProvider : IMetricProvider
{
    public const string Title = "media.title";
    public const string Artist = "media.artist";
    public const string Album = "media.album";
    public const string App = "media.app";
    public const string Status = "media.status";
    public const string Position = "media.position";
    public const string Duration = "media.duration";
    public const string Progress = "media.progress";

    /// <summary>The album art's image bytes (a <c>byte[]</c>), as a <see cref="MetricStore"/> attachment.</summary>
    public const string Artwork = "media.artwork";

    /// <summary>
    /// Seconds after a track change to read the album art again: some players publish the new title before the new
    /// picture, so the first read can get the old one or none.
    /// </summary>
    private static readonly int[] ArtworkRereads = [2, 5];

    private readonly MediaSessionReader _reader = new();
    private readonly TimeProvider _time;
    private string? _artworkKey;
    private int _sinceTrackChange;

    public MediaProvider(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Title, "Now playing: title", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Artist, "Now playing: artist", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Album, "Now playing: album", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(App, "Now playing: app", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Status, "Now playing: Playing, Paused or Stopped", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Position, "Now playing: position", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Duration, "Now playing: length", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition(Progress, "Now playing: progress", "Media", MetricUnit.Percent, 100));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        // Read the album art when the track changes, and again shortly after.
        _sinceTrackChange++;
        var rereadArtwork = ArtworkRereads.Contains(_sinceTrackChange);
        var snapshot = _reader.Read(rereadArtwork ? null : _artworkKey);
        Publish(store, snapshot, _time.GetUtcNow().UtcDateTime);

        if (snapshot is null)
        {
            _artworkKey = null;
            store.SetAttachment(Artwork, null);
        }
        else if (snapshot.ArtworkRead)
        {
            if (snapshot.Key != _artworkKey)
                _sinceTrackChange = 0;
            _artworkKey = snapshot.Key;
            store.SetAttachment(Artwork, snapshot.Artwork);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes a snapshot's readings, estimating the position since the player last reported it.</summary>
    internal static void Publish(MetricStore store, MediaSnapshot? snapshot, DateTime nowUtc)
    {
        // A session that's closed, or has nothing loaded yet, counts as nothing playing.
        var active = snapshot is not null
            && snapshot.Status is not MediaPlaybackStatus.Closed
            && (snapshot.Title.Length > 0 || snapshot.Artist.Length > 0);

        store.SetText(Title, active ? NullIfEmpty(snapshot!.Title) : null);
        store.SetText(Artist, active ? NullIfEmpty(snapshot!.Artist) : null);
        store.SetText(Album, active ? NullIfEmpty(snapshot!.Album) : null);
        store.SetText(App, active ? AppName(snapshot!.AppId) : null);
        store.SetText(Status, active ? StatusText(snapshot!.Status) : null);

        if (!active || snapshot!.Duration <= TimeSpan.Zero)
        {
            // Live streams and some browsers don't report a timeline.
            store.SetText(Position, null);
            store.SetText(Duration, null);
            store.Set(Progress, null);
            return;
        }

        var position = snapshot.Position;
        if (snapshot.Status == MediaPlaybackStatus.Playing && nowUtc > snapshot.UpdatedUtc)
            position += nowUtc - snapshot.UpdatedUtc;
        if (position > snapshot.Duration)
            position = snapshot.Duration;

        store.SetText(Position, Clock(position));
        store.SetText(Duration, Clock(snapshot.Duration));
        store.Set(Progress, position / snapshot.Duration * 100);
    }

    /// <summary>"3:07", or "1:02:45" for an hour or more.</summary>
    internal static string Clock(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    /// <summary>
    /// A readable name from an AppUserModelID: "Spotify.exe" → "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "ZuneMusic".
    /// </summary>
    internal static string? AppName(string appId)
    {
        var name = appId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0)
            name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1)
            name = name[(dot + 1)..];
        name = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
        return NullIfEmpty(name);
    }

    private static string StatusText(MediaPlaybackStatus status) => status switch
    {
        MediaPlaybackStatus.Playing => "Playing",
        MediaPlaybackStatus.Paused => "Paused",
        MediaPlaybackStatus.Changing => "Loading",
        _ => "Stopped",
    };

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public void Dispose() => _reader.Dispose();
}

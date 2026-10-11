using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class MediaProviderTests
{
    private static readonly DateTime Reported = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private static MetricStore Store()
    {
        var store = new MetricStore();
        using var provider = new MediaProvider();
        provider.Initialize(store);
        return store;
    }

    private static MediaSnapshot Track(MediaPlaybackStatus status, double position = 65, double duration = 214, string title = "Midnight Strip Lights") =>
        new("Spotify.exe", title, "The Impious Bonums", "Wicked Good", status,
            TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), Reported, ArtworkRead: false, Artwork: null);

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    [Fact]
    public void A_playing_track_moves_on_from_where_the_player_last_reported()
    {
        var store = Store();
        MediaProvider.Publish(store, Track(MediaPlaybackStatus.Playing), Reported.AddSeconds(7));

        Assert.Equal("Midnight Strip Lights", Text(store, MediaProvider.Title));
        Assert.Equal("The Impious Bonums", Text(store, MediaProvider.Artist));
        Assert.Equal("Wicked Good", Text(store, MediaProvider.Album));
        Assert.Equal("Spotify", Text(store, MediaProvider.App));
        Assert.Equal("Playing", Text(store, MediaProvider.Status));
        Assert.Equal("1:12", Text(store, MediaProvider.Position));
        Assert.Equal("3:34", Text(store, MediaProvider.Duration));
        Assert.Equal(72.0 / 214 * 100, Value(store, MediaProvider.Progress)!.Value, 6);
    }

    [Fact]
    public void A_paused_track_stays_put_and_the_position_never_passes_the_end()
    {
        var store = Store();
        MediaProvider.Publish(store, Track(MediaPlaybackStatus.Paused), Reported.AddMinutes(10));
        Assert.Equal("Paused", Text(store, MediaProvider.Status));
        Assert.Equal("1:05", Text(store, MediaProvider.Position));

        MediaProvider.Publish(store, Track(MediaPlaybackStatus.Playing, position: 210), Reported.AddMinutes(10));
        Assert.Equal("3:34", Text(store, MediaProvider.Position));
        Assert.Equal(100, Value(store, MediaProvider.Progress));
    }

    [Fact]
    public void Live_streams_without_a_timeline_have_no_position()
    {
        var store = Store();
        MediaProvider.Publish(store, Track(MediaPlaybackStatus.Playing, position: 0, duration: 0), Reported);
        Assert.Equal("Midnight Strip Lights", Text(store, MediaProvider.Title));
        Assert.Null(Text(store, MediaProvider.Position));
        Assert.Null(Text(store, MediaProvider.Duration));
        Assert.Null(Value(store, MediaProvider.Progress));
    }

    [Fact]
    public void Nothing_has_a_reading_when_nothing_is_playing()
    {
        var store = Store();
        MediaProvider.Publish(store, Track(MediaPlaybackStatus.Playing), Reported);

        foreach (var gone in new MediaSnapshot?[] { null, Track(MediaPlaybackStatus.Closed), Track(MediaPlaybackStatus.Playing, title: "") with { Artist = "" } })
        {
            MediaProvider.Publish(store, gone, Reported);
            Assert.Null(Text(store, MediaProvider.Title));
            Assert.Null(Text(store, MediaProvider.Status));
            Assert.Null(Value(store, MediaProvider.Progress));
        }
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:59")]
    [InlineData(214, "3:34")]
    [InlineData(3765, "1:02:45")]
    public void Positions_read_like_a_player(double seconds, string expected) =>
        Assert.Equal(expected, MediaProvider.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("chrome", "Chrome")]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude", "Claude")]
    [InlineData("", null)]
    public void App_names_come_from_the_app_id(string appId, string? expected) =>
        Assert.Equal(expected, MediaProvider.AppName(appId));

    [Fact]
    public void Attachments_are_kept_by_id_and_cleared_with_null()
    {
        var store = new MetricStore();
        var art = new byte[] { 1, 2, 3 };
        store.SetAttachment(MediaProvider.Artwork, art);
        Assert.Same(art, store.GetAttachment("MEDIA.ARTWORK"));
        store.SetAttachment(MediaProvider.Artwork, null);
        Assert.Null(store.GetAttachment(MediaProvider.Artwork));
    }
}

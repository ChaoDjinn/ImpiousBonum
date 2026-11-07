using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class SoundProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 20, 0, 0, TimeSpan.Zero);

    private static readonly OutputVolume Headphones = new("Headphones (Arctis Nova 7)", 0.456, Muted: false);

    private static StreamVolume Stream(string key, string name, double volume, bool muted = false, bool playing = false) =>
        new(key, name, $@"C:\Apps\{key}.exe", volume, muted, playing);

    [Fact]
    public void Each_app_s_streams_are_one_row_in_name_order_with_Windows_own_sounds_last()
    {
        var apps = AppVolumeReader.Group(
        [
            new StreamVolume(AppVolume.SystemSounds, "System sounds", null, 1, false, false),
            Stream("spotify", "Spotify", 0.3, playing: true),
            Stream("chrome", "Google Chrome", 0.5, muted: true),
            Stream("chrome", "Google Chrome", 0.8, muted: false, playing: true),
            Stream("discord", "Discord", 1, muted: true),
        ]);

        Assert.Equal(["Discord", "Google Chrome", "Spotify", "System sounds"], apps.Select(a => a.Name));
        var chrome = apps[1];
        // The loudest of its streams, muted only when they all are, and playing when any is.
        Assert.Equal((0.8, false, true), (chrome.Volume, chrome.Muted, chrome.Playing));
        Assert.True(apps[0].Muted);
    }

    [Theory]
    [InlineData("Spotify", true)]
    [InlineData("spotify.exe", true)]
    [InlineData(" Google Chrome ", true)]
    [InlineData("chrome", true)]
    [InlineData("Chrom", false)]
    public void An_app_is_named_by_its_program_or_the_name_it_shows(string typed, bool names) =>
        Assert.Equal(names, AppVolumeControl.Names(typed, typed.Contains("hrome", StringComparison.OrdinalIgnoreCase) ? "chrome" : "spotify",
            typed.Contains("hrome", StringComparison.OrdinalIgnoreCase) ? "Google Chrome" : "Spotify"));

    [Fact]
    public void The_device_s_volume_is_a_reading_and_the_apps_are_only_read_while_a_mixer_wants_them()
    {
        var store = new MetricStore();
        var time = new FakeTime(Start);
        var asked = new List<bool>();
        IReadOnlyList<AppVolume> apps = [new AppVolume("spotify", "Spotify", null, 0.3, false, true)];
        using var provider = new SoundProvider(lookAgain =>
        {
            asked.Add(lookAgain);
            return apps;
        }, () => Headphones, time);

        provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(46, store.TryGet(SoundProvider.Volume, out var volume) ? volume.Value : null);
        Assert.Equal(0, store.TryGet(SoundProvider.Muted, out var muted) ? muted.Value : null);
        Assert.Equal("Headphones (Arctis Nova 7)", store.TryGet(SoundProvider.Device, out var device) ? device.Text : null);
        Assert.Empty(asked);
        Assert.Null(store.GetAttachment(SoundProvider.Mixer));

        // A mixer comes on screen: the apps are read each second, and looked for again every few.
        SoundProvider.Want(store, time.Now);
        for (var i = 0; i < 4; i++)
        {
            provider.SampleAsync(store, CancellationToken.None);
            time.Now += TimeSpan.FromSeconds(1);
        }
        Assert.Equal([true, false, false, true], asked);
        var state = Assert.IsType<MixerState>(store.GetAttachment(SoundProvider.Mixer));
        Assert.Equal(Headphones, state.Output);
        Assert.Equal(["Spotify"], SoundProvider.Names(store));

        // It goes: a few seconds later, nothing more is read.
        time.Now += SoundProvider.WantedFor;
        provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4, asked.Count);
        Assert.Null(store.GetAttachment(SoundProvider.Mixer));
    }

    [Fact]
    public void Without_a_sound_device_there_s_no_reading()
    {
        var store = new MetricStore();
        using var provider = new SoundProvider(_ => [], () => null);

        provider.Publish(store, null, null);

        Assert.False(store.TryGet(SoundProvider.Volume, out _));
    }

    [Fact]
    public void The_names_seen_build_up_in_name_order()
    {
        var store = new MetricStore();
        using var provider = new SoundProvider(_ => [], () => null);

        provider.Publish(store, Headphones, [new AppVolume("spotify", "Spotify", null, 1, false, true)]);
        provider.Publish(store, Headphones, [new AppVolume("discord", "Discord", null, 1, false, true)]);

        Assert.Equal(["Discord", "Spotify"], SoundProvider.Names(store));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

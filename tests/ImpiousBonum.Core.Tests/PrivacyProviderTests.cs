using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;
using Microsoft.Win32;

namespace ImpiousBonum.Core.Tests;

public sealed class PrivacyProviderTests
{
    private const string Discord = @"C:#Users#Sam#AppData#Local#Discord#app-1.0.9260#Discord.exe";
    private const string OldDiscord = @"C:#Users#Sam#AppData#Local#Discord#app-1.0.9007#Discord.exe";
    private const string Obs = @"C:#Program Files#obs-studio#bin#64bit#obs64.exe";
    private const string Camera = "Microsoft.WindowsCamera_8wekyb3d8bbwe";

    [Theory]
    [InlineData(100, 0, true)]     // started, not stopped yet
    [InlineData(300, 200, true)]   // started again since it last stopped
    [InlineData(100, 200, false)]  // stopped since
    [InlineData(0, 0, false)]      // allowed, but never used
    public void An_app_is_using_it_from_when_it_starts_until_it_stops(long start, long stop, bool inUse) =>
        Assert.Equal(inUse, new ConsentEntry(Camera, true, start, stop).InUse);

    [Theory]
    [InlineData(Discord, false, "Discord")]
    [InlineData(Obs, false, "OBS")]
    [InlineData(@"C:#Program Files#Google#Chrome#Application#chrome.exe", false, "Chrome")]
    [InlineData(@"D:#Programs#Steam#steamapps#common#ProjectZomboid#ProjectZomboid64.exe", false, "ProjectZomboid")]
    [InlineData(@"D:#Programs#Steam#steamapps#common#Tabletop Simulator#Tabletop Simulator.exe", false, "Tabletop Simulator")]
    [InlineData(@"E:#SteamLibrary#steamapps#common#Gloomhaven#GH.exe", false, "GH")]
    [InlineData(@"D:#Programs#Steam#steamapps#common#Ride#Ride#Binaries#Win64#Ride-Win64-Shipping.exe", false, "Ride-Win64-Shipping")]
    [InlineData(Camera, true, "WindowsCamera")]
    [InlineData("MSTeams_8wekyb3d8bbwe", true, "MSTeams")]
    [InlineData("com.tinyspeck.slackdesktop_8yrtsj140pw4g", true, "Slackdesktop")]
    public void Apps_have_readable_names(string key, bool packaged, string expected) =>
        Assert.Equal(expected, PrivacyProvider.AppName(new ConsentEntry(key, packaged, 1, 0)));

    /// <summary>A sound stream's app is its process's program name, which has no .exe.</summary>
    [Theory]
    [InlineData("Discord", "Discord")]
    [InlineData("obs64", "OBS")]
    [InlineData("chrome", "Chrome")]
    [InlineData("SteelSeriesSonar", "SteelSeriesSonar")]
    [InlineData("ProjectZomboid64", "ProjectZomboid")]
    [InlineData("vlc", "VLC")]
    public void Programs_have_readable_names(string program, string expected) =>
        Assert.Equal(expected, PrivacyProvider.ProgramName(program));

    [Fact]
    public void The_apps_using_the_camera_are_listed_once_each_in_order()
    {
        IReadOnlyList<ConsentEntry> entries =
        [
            new(Obs, false, 500, 0),
            new(Discord, false, 400, 0),
            // An old version of Discord, which used it once: Windows keeps a key for every version.
            new(OldDiscord, false, 100, 0),
            new(Camera, true, 100, 200),
        ];
        Assert.Equal("Discord, OBS", PrivacyProvider.AppsUsing(entries));
        Assert.Null(PrivacyProvider.AppsUsing([new(Camera, true, 100, 200)]));
        Assert.Null(PrivacyProvider.AppsUsing([]));
        Assert.Null(PrivacyProvider.AppsUsing(null));
    }

    [Fact]
    public void The_apps_using_the_microphone_are_those_recording_now()
    {
        IReadOnlyList<AppSound> sounds =
        [
            Recording(40, "obs64"),
            // Discord records on two microphones (one a router's, like Sonar's), and plays its sound out: it's listed once.
            Recording(12, "Discord"),
            Recording(12, "Discord"),
            new(12, "Discord", SoundFlow.Out, true, 0.3),
            Recording(33, "SteelSeriesSonar"),
            // Open, but stopped: Windows doesn't count it as using the microphone, nor light its icon for it.
            Recording(50, "chrome", active: false),
            // Playing sound only.
            new(60, "Spotify", SoundFlow.Out, true, 0.5),
        ];
        Assert.Equal("Discord, OBS, SteelSeriesSonar", PrivacyProvider.AppsRecording(sounds));
        Assert.Null(PrivacyProvider.AppsRecording([Recording(50, "chrome", active: false), new(60, "Spotify", SoundFlow.Out, true, 0.5)]));
        Assert.Null(PrivacyProvider.AppsRecording([]));
    }

    [Fact]
    public async Task Metrics_appear_once_there_is_a_microphone_or_a_camera_has_been_used()
    {
        MicrophoneState? mic = null;
        IReadOnlyList<AppSound> recording = [];
        IReadOnlyList<ConsentEntry>? cameraApps = null;
        using var provider = new PrivacyProvider(() => mic, () => recording, () => cameraApps);
        var store = new MetricStore();
        provider.Initialize(store);

        // No microphone, no streams and no keys: nothing to show.
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Empty(store.Definitions);

        // Apps allowed to use the camera, but none ever has.
        cameraApps = [new(Camera, true, 0, 0)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Empty(store.Definitions);

        // A microphone is enough for its metrics; the camera's wait for an app to have used one.
        mic = new MicrophoneState("Headset Microphone (Arctis 7)", Muted: false);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, Value(store, PrivacyProvider.MicMuted));
        Assert.Equal("Headset Microphone (Arctis 7)", Text(store, PrivacyProvider.MicName));
        Assert.Equal(0, Value(store, PrivacyProvider.MicInUse));
        Assert.Null(Text(store, PrivacyProvider.MicApps));
        Assert.False(store.TryGet(PrivacyProvider.CameraInUse, out _));

        mic = mic with { Muted = true };
        recording = [Recording(12, "Discord"), Recording(40, "obs64", active: false)];
        cameraApps = [new(Camera, true, 100, 200)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, Value(store, PrivacyProvider.MicMuted));
        Assert.Equal(1, Value(store, PrivacyProvider.MicInUse));
        Assert.Equal("Discord", Text(store, PrivacyProvider.MicApps));
        Assert.Equal(0, Value(store, PrivacyProvider.CameraInUse));
        Assert.Null(Text(store, PrivacyProvider.CameraApps));

        cameraApps = [new(Camera, true, 300, 0)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, Value(store, PrivacyProvider.CameraInUse));
        Assert.Equal("WindowsCamera", Text(store, PrivacyProvider.CameraApps));

        // Discord leaves the call: its stream stops, though it's still open.
        recording = [Recording(12, "Discord", active: false)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, Value(store, PrivacyProvider.MicInUse));
        Assert.Null(Text(store, PrivacyProvider.MicApps));

        // The microphone unplugged, and the keys gone: the metrics stay, with no mute or name and nothing in use.
        mic = null;
        recording = [];
        cameraApps = null;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Value(store, PrivacyProvider.MicMuted));
        Assert.Null(Text(store, PrivacyProvider.MicName));
        Assert.Equal(0, Value(store, PrivacyProvider.MicInUse));
        Assert.Equal(0, Value(store, PrivacyProvider.CameraInUse));
    }

    [Fact]
    public async Task An_app_recording_without_a_default_microphone_still_shows()
    {
        using var provider = new PrivacyProvider(() => null, () => [Recording(12, "Discord")], () => null);
        var store = new MetricStore();
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Null(Value(store, PrivacyProvider.MicMuted));
        Assert.Equal(1, Value(store, PrivacyProvider.MicInUse));
        Assert.Equal("Discord", Text(store, PrivacyProvider.MicApps));
        Assert.False(store.TryGet(PrivacyProvider.CameraInUse, out _));
    }

    /// <summary>The records as Windows keeps them, in a key of the test's own under HKEY_CURRENT_USER.</summary>
    [Fact]
    public void Records_are_read_from_the_registry_and_again_after_they_change()
    {
        var root = $@"Software\ImpiousBonum.Tests.{Guid.NewGuid():N}";
        var path = $@"{root}\webcam";
        using var reader = new ConsentStoreReader(path);
        try
        {
            Assert.Null(reader.Read());

            using (var key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue("Value", "Allow");
                using (var camera = key.CreateSubKey(Camera))
                {
                    camera.SetValue("Value", "Allow");
                }
                using (var desktop = key.CreateSubKey(@"NonPackaged\" + Discord))
                {
                    desktop.SetValue("LastUsedTimeStart", 400L, RegistryValueKind.QWord);
                    desktop.SetValue("LastUsedTimeStop", 0L, RegistryValueKind.QWord);
                }
                using (var desktop = key.CreateSubKey(@"NonPackaged\" + Obs))
                {
                    desktop.SetValue("LastUsedTimeStart", 100L, RegistryValueKind.QWord);
                    desktop.SetValue("LastUsedTimeStop", 200L, RegistryValueKind.QWord);
                }
            }

            var entries = reader.Read()!;
            Assert.Equal(3, entries.Count);
            Assert.Contains(new ConsentEntry(Camera, true, 0, 0), entries);
            Assert.Contains(new ConsentEntry(Discord, false, 400, 0), entries);
            Assert.Contains(new ConsentEntry(Obs, false, 100, 200), entries);
            Assert.Equal("Discord", PrivacyProvider.AppsUsing(entries));

            // Unchanged, it isn't read again.
            Assert.Same(entries, reader.Read());

            // Discord stops and OBS starts: Windows' notification says to read it again.
            using (var desktop = Registry.CurrentUser.CreateSubKey($@"{path}\NonPackaged\{Discord}"))
                desktop.SetValue("LastUsedTimeStop", 500L, RegistryValueKind.QWord);
            using (var desktop = Registry.CurrentUser.CreateSubKey($@"{path}\NonPackaged\{Obs}"))
                desktop.SetValue("LastUsedTimeStart", 600L, RegistryValueKind.QWord);
            var changed = entries;
            for (var tries = 0; tries < 50 && ReferenceEquals(changed, entries); tries++)
            {
                Thread.Sleep(20);
                changed = reader.Read()!;
            }
            Assert.Equal("OBS", PrivacyProvider.AppsUsing(changed));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    // These read the real microphone, sound streams and records, so they only check anything on a PC with them (CI
    // has none).
    [Fact]
    public async Task Reading_this_pc_gives_sensible_values()
    {
        using var provider = new PrivacyProvider();
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        await provider.SampleAsync(store, CancellationToken.None);

        if (store.TryGet(PrivacyProvider.MicMuted, out var muted) && muted.Value is { } value)
            Assert.Contains(value, new double[] { 0, 1 });
        if (store.TryGet(PrivacyProvider.MicInUse, out var inUse))
        {
            Assert.Contains(inUse.Value, new double?[] { 0, 1 });
            Assert.Equal(inUse.Value == 1, store.TryGet(PrivacyProvider.MicApps, out var apps) && apps.Text is { Length: > 0 });
        }
    }

    [Fact]
    public void Every_apps_recording_streams_are_found_and_nothing_else()
    {
        using var reader = new AudioSessionReader();
        var sounds = reader.Read(null, lookAgain: true, SoundFlow.In);

        Assert.All(sounds, s =>
        {
            Assert.Equal(SoundFlow.In, s.Flow);
            Assert.NotEqual(0, s.ProcessId);
            Assert.False(string.IsNullOrEmpty(s.App));
            Assert.InRange(s.Peak, 0, 1);
        });
        // Read again without looking, the same streams.
        Assert.Equal(sounds.Select(s => (s.ProcessId, s.App)), reader.Read(null, lookAgain: false, SoundFlow.In).Select(s => (s.ProcessId, s.App)));
    }

    private static AppSound Recording(int processId, string app, bool active = true) => new(processId, app, SoundFlow.In, active, active ? 0.05 : 0);

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;
}

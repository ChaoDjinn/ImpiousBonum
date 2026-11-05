using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class SteamDownloadsTests
{
    private const string Apps = @"D:\Programs\Steam\steamapps";
    private const long Megabyte = 1_000_000;

    // Steam's EAppState flags, as a manifest's StateFlags combines them.
    private const int FullyInstalled = 4;
    private const int UpdateRequired = 2;
    private const int Running = UpdateRequired | FullyInstalled | 256 | 1024;
    private const int Paused = UpdateRequired | FullyInstalled | 512 | 1024;
    private const int Queued = UpdateRequired | FullyInstalled | 1024;
    private const int Staging = Running | (1 << 21);
    private const int NewInstall = UpdateRequired | 1024 | (1 << 20);

    private static string Manifest(
        int appId, string name, int flags, long toDownload = 0, long downloaded = 0, long toStage = 0, long staged = 0, string folder = "Its Folder") => $$"""
        "AppState"
        {
            "appid"        "{{appId}}"
            "name"        "{{name}}"
            "StateFlags"        "{{flags}}"
            "installdir"        "{{folder}}"
            "BytesToDownload"        "{{toDownload}}"
            "BytesDownloaded"        "{{downloaded}}"
            "BytesToStage"        "{{toStage}}"
            "BytesStaged"        "{{staged}}"
        }
        """;

    [Fact]
    public void A_manifest_says_whether_a_game_is_downloading_and_how_far_it_has_got()
    {
        Assert.Null(SteamLibrary.Download(Manifest(1, "Hades", FullyInstalled, 1000, 1000)));
        // An update that waits to be launched hasn't started.
        Assert.Null(SteamLibrary.Download(Manifest(1, "Hades", UpdateRequired | FullyInstalled, 1000)));
        Assert.Null(SteamLibrary.Download("not a manifest"));

        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Running, 1000, 400)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 0, 9000), SteamLibrary.Download(Manifest(2, "Tunic", NewInstall, 9000)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Paused, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Paused, 1000, 400)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Queued, 0, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Queued, 1000)));
        // Partly downloaded, then left waiting.
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Queued, 300, 1000), SteamLibrary.Download(Manifest(2, "Tunic", UpdateRequired | FullyInstalled, 1000, 300)));
        // Downloaded, and being written into its folder: how much is written of how much.
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Installing, 700, 2000), SteamLibrary.Download(Manifest(2, "Tunic", Staging, 1000, 1000, 2000, 700)));

        // No name: its folder's.
        Assert.Equal("Tunic", SteamLibrary.Download(Manifest(2, "", Running, 1000, 400, folder: "Tunic"))!.Name);
    }

    [Fact]
    public void Progress_and_time_left_come_from_the_sizes_and_the_speed()
    {
        var download = new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 400, 1000);
        Assert.Equal(40, download.Percent);
        Assert.Null(download.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(60), (download with { Speed = 10 }).TimeLeft);
        Assert.Null((download with { Total = 0 }).Percent);
        Assert.Equal("Installing", SteamDownload.StateName(SteamDownloadState.Installing));
        Assert.Equal("Queued", SteamDownload.StateName(SteamDownloadState.Queued));
    }

    [Fact]
    public async Task Downloads_come_from_manifests_that_changed_best_first_with_a_speed_from_how_far_they_moved()
    {
        var files = new FakeGameFiles { Steam = "D:/Programs/Steam" };
        files.Texts[$@"{Apps}\appmanifest_1.acf"] = Manifest(1, "Hades", FullyInstalled);
        files.Texts[$@"{Apps}\appmanifest_2.acf"] = Manifest(2, "Tunic", Running, 1000 * Megabyte, 100 * Megabyte);
        files.Texts[$@"{Apps}\appmanifest_3.acf"] = Manifest(3, "Celeste", Queued, 500 * Megabyte);
        files.Texts[$@"{Apps}\appmanifest_4.acf"] = Manifest(4, "Abzu", Paused, 800 * Megabyte, 200 * Megabyte);
        var time = new FakeTime(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));
        using var provider = new SteamDownloadsProvider(files, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["Tunic", "Abzu", "Celeste"], Downloads(store).Select(d => d.Name));
        Assert.Equal(3, Number(store, SteamDownloadsProvider.Count));
        Assert.Equal("Tunic", Text(store, SteamDownloadsProvider.Game));
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        Assert.Equal(10, Number(store, SteamDownloadsProvider.Progress));
        Assert.Equal(100 * Megabyte, Number(store, SteamDownloadsProvider.Done));
        Assert.Equal(1000 * Megabyte, Number(store, SteamDownloadsProvider.Size));
        // No speed until it has moved.
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        Assert.Null(Text(store, SteamDownloadsProvider.TimeLeft));
        var reads = files.TextReads;

        // Steam writes its progress: only that manifest is read, and the speed is how far it came since.
        time.Advance(TimeSpan.FromSeconds(30));
        Write(files, 2, Manifest(2, "Tunic", Running, 1000 * Megabyte, 400 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(reads + 1, files.TextReads);
        Assert.Equal(10 * Megabyte, Number(store, SteamDownloadsProvider.Speed));
        Assert.Equal("1m", Text(store, SteamDownloadsProvider.TimeLeft));
        Assert.Equal(10 * Megabyte, Downloads(store)[0].Speed);

        // Nothing written since: nothing read, and the speed stays a while.
        time.Advance(TimeSpan.FromSeconds(5));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(reads + 1, files.TextReads);
        Assert.Equal(10 * Megabyte, Number(store, SteamDownloadsProvider.Speed));

        // Stuck for long enough, it has no speed.
        time.Advance(SteamDownloadsProvider.SpeedLapse);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        Assert.Equal(40, Number(store, SteamDownloadsProvider.Progress));

        // Finished, unpaused and taken out of the queue: nothing is downloading, so there's no reading at all.
        Write(files, 2, Manifest(2, "Tunic", FullyInstalled, 1000 * Megabyte, 1000 * Megabyte), time);
        Write(files, 4, Manifest(4, "Abzu", FullyInstalled, 800 * Megabyte, 800 * Megabyte), time);
        files.Texts.Remove($@"{Apps}\appmanifest_3.acf");
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(SteamDownloadsProvider.List));
        Assert.Null(Number(store, SteamDownloadsProvider.Count));
        Assert.Null(Text(store, SteamDownloadsProvider.Game));
        Assert.Null(Text(store, SteamDownloadsProvider.State));
        Assert.Null(Number(store, SteamDownloadsProvider.Progress));
    }

    [Fact]
    public async Task Without_steam_there_are_no_downloads()
    {
        using var provider = new SteamDownloadsProvider(new FakeGameFiles());
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(SteamDownloadsProvider.List));
        Assert.Null(Number(store, SteamDownloadsProvider.Count));
    }

    private static void Write(FakeGameFiles files, int appId, string manifest, FakeTime time)
    {
        var path = $@"{Apps}\appmanifest_{appId}.acf";
        files.Texts[path] = manifest;
        files.Written[path] = time.GetUtcNow().UtcDateTime;
    }

    private static IReadOnlyList<SteamDownload> Downloads(MetricStore store) =>
        Assert.IsAssignableFrom<IReadOnlyList<SteamDownload>>(store.GetAttachment(SteamDownloadsProvider.List));

    private static double? Number(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

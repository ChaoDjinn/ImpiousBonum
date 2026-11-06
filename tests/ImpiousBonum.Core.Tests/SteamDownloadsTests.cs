using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class SteamDownloadsTests
{
    private const string Apps = @"D:\Programs\Steam\steamapps";
    private const long Megabyte = 1_000_000;

    // Steam's EAppState flags, as a manifest's StateFlags combines them. Real downloads have been seen as 1026 and 1030
    // while going, and 70 once paused; the others are written less often.
    private const int FullyInstalled = 4;
    private const int UpdateRequired = 2;
    private const int Going = UpdateRequired | FullyInstalled | 1024;
    private const int NewInstall = UpdateRequired | 1024;
    private const int Running = Going | 256;
    private const int Paused = Going | 512;
    private const int SeenPaused = 70;
    private const int Staging = Going | (1 << 21);

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

        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Going, 1000, 400)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Running, 1000, 400)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 0, 9000), SteamLibrary.Download(Manifest(2, "Tunic", NewInstall, 9000)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Paused, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", Paused, 1000, 400)));
        // Stopped partway, as Steam writes it in practice: no started flag, but bytes to go.
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Paused, 400, 1000), SteamLibrary.Download(Manifest(2, "Tunic", SeenPaused, 1000, 400)));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Paused, 300, 1000), SteamLibrary.Download(Manifest(2, "Tunic", UpdateRequired | FullyInstalled, 1000, 300)));
        // Downloaded, and being written into its folder: how much is written of how much.
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Installing, 700, 2000) { Overall = 35 }, SteamLibrary.Download(Manifest(2, "Tunic", Staging, 1000, 1000, 2000, 700)));

        // Steam's registry has the last word on whether it's going: a started download it isn't working on is waiting,
        // or paused if it got partway; one it is working on is going, whatever the flags.
        Assert.Equal(SteamDownloadState.Queued, SteamLibrary.Download(Manifest(2, "Tunic", Going, 1000), updating: false)!.State);
        Assert.Equal(SteamDownloadState.Paused, SteamLibrary.Download(Manifest(2, "Tunic", Going, 1000, 400), updating: false)!.State);
        Assert.Equal(SteamDownloadState.Downloading, SteamLibrary.Download(Manifest(2, "Tunic", SeenPaused, 1000, 400), updating: true)!.State);
        Assert.Equal(SteamDownloadState.Installing, SteamLibrary.Download(Manifest(2, "Tunic", Going, 1000, 1000, 2000, 700), updating: true)!.State);

        // No name: its folder's.
        Assert.Equal("Tunic", SteamLibrary.Download(Manifest(2, "", Running, 1000, 400, folder: "Tunic"))!.Name);
    }

    [Fact]
    public void Steam_working_on_an_update_it_hasn_t_written_as_started_is_a_download()
    {
        // For a few seconds after a download starts, Steam sizes it and makes room before it writes the started flag;
        // as seen for a 13 GB update, the manifest still says only that an update is wanted, and the registry that Steam
        // is working on it.
        var waiting = SteamLibrary.DownloadManifest(Manifest(2, "Tunic", UpdateRequired | FullyInstalled))!;
        Assert.False(waiting.Started);
        Assert.True(waiting.ToDo);
        Assert.Null(waiting.Download(updating: null));
        Assert.Null(waiting.Download(updating: false));
        Assert.Equal(new SteamDownload(2, "Tunic", SteamDownloadState.Downloading, 0, 0), waiting.Download(updating: true));
        // An installed game with nothing to do isn't asked about.
        Assert.False(SteamLibrary.DownloadManifest(Manifest(1, "Hades", FullyInstalled, 1000, 1000))!.ToDo);
    }

    [Fact]
    public void How_far_the_whole_update_has_got_is_the_download_or_the_writing_whichever_is_behind()
    {
        // As Steam wrote it on pausing a 13.8 GB update: 96% downloaded and 65% written. The bar shows 65, then carries
        // on from there as the writing finishes, rather than dropping from 96 to whatever the writing is at.
        var paused = SteamLibrary.Download(Manifest(2, "Frosthaven", Going, 13_829_622_784, 13_323_208_464, 25_270_250_554, 16_389_762_847), updating: false)!;
        Assert.Equal(SteamDownloadState.Paused, paused.State);
        Assert.Equal(13_323_208_464, paused.Done);
        Assert.Equal(64.86, paused.Percent!.Value, 2);
        var installing = SteamLibrary.Download(Manifest(2, "Frosthaven", Going, 13_829_622_784, 13_829_622_784, 25_270_250_554, 19_104_132_507), updating: true)!;
        Assert.Equal(SteamDownloadState.Installing, installing.State);
        Assert.Equal(75.6, installing.Percent!.Value, 1);
        // A download that writes nothing into the folder is how much of it is downloaded.
        Assert.Equal(40, SteamLibrary.Download(Manifest(2, "Tunic", Going, 1000, 400))!.Percent);
        // Nothing to download, only writing: how much is written.
        Assert.Equal(25, SteamLibrary.Download(Manifest(2, "Tunic", Going, 0, 0, 2000, 500), updating: true)!.Percent);
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
        var time = new FakeTime(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));
        files.Texts[$@"{Apps}\appmanifest_1.acf"] = Manifest(1, "Hades", FullyInstalled);
        Write(files, 2, Manifest(2, "Tunic", Going, 1000 * Megabyte, 100 * Megabyte), time);
        // Queued behind Tunic: started, but Steam isn't working on it yet.
        files.Texts[$@"{Apps}\appmanifest_3.acf"] = Manifest(3, "Celeste", Going, 500 * Megabyte);
        files.Updating[3] = false;
        files.Texts[$@"{Apps}\appmanifest_4.acf"] = Manifest(4, "Abzu", Paused, 800 * Megabyte, 200 * Megabyte);
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
        Write(files, 2, Manifest(2, "Tunic", Going, 1000 * Megabyte, 400 * Megabyte), time);
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

        // Steam hasn't written for a while, as it doesn't while a download's going: no speed, and only the size, as how
        // far it had got has fallen behind.
        time.Advance(SteamDownloadsProvider.SpeedLapse);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        Assert.Null(Number(store, SteamDownloadsProvider.Progress));
        Assert.Null(Number(store, SteamDownloadsProvider.Done));
        Assert.Equal(1000 * Megabyte, Number(store, SteamDownloadsProvider.Size));
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        Assert.True(Downloads(store)[0].Stale);
        // A paused download's progress is as Steam wrote it on pausing, so it isn't out of date however long it waits.
        Assert.False(Downloads(store).Single(d => d.Name == "Abzu").Stale);
        Assert.Equal(25, Downloads(store).Single(d => d.Name == "Abzu").Percent);

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
    public async Task A_speed_is_timed_from_steam_s_last_write_not_from_before_the_download_started_or_across_a_pause()
    {
        var files = new FakeGameFiles { Steam = "D:/Programs/Steam" };
        var time = new FakeTime(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));
        // An update Steam has known about for an hour without starting it.
        Write(files, 2, Manifest(2, "Icarus", UpdateRequired | FullyInstalled, 1300 * Megabyte), time);
        using var provider = new SteamDownloadsProvider(files, time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(SteamDownloadsProvider.List));

        // Steam starts it, writing that it has, then 6 seconds later how far it got. Timed from the manifest written an
        // hour before, as it once was, 24 MB came to 7 KB/s, with a day and a half left; from Steam's last write, 4 MB/s.
        time.Advance(TimeSpan.FromHours(1));
        files.Updating[2] = true;
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        time.Advance(TimeSpan.FromSeconds(6));
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 24 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4 * Megabyte, Number(store, SteamDownloadsProvider.Speed));
        Assert.Equal("5m", Text(store, SteamDownloadsProvider.TimeLeft));

        // Paused three minutes on, with nothing written in between: too long a gap to time a speed over.
        time.Advance(TimeSpan.FromMinutes(3));
        files.Updating[2] = false;
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 800 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Paused", Text(store, SteamDownloadsProvider.State));
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));

        // Resumed an hour later, which Steam writes without having moved, and 10 seconds on 100 MB further: 10 MB/s, not
        // 100 MB over the hour and ten seconds since it last moved (0.03 MB/s, and 4 hours left).
        time.Advance(TimeSpan.FromHours(1));
        files.Updating[2] = true;
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 800 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        time.Advance(TimeSpan.FromSeconds(10));
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 900 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(10 * Megabyte, Number(store, SteamDownloadsProvider.Speed));

        // Paused again for an hour, and resumed without a write of its own: the first write after has no speed, rather
        // than one timed across the pause, and the next has the right one.
        time.Advance(TimeSpan.FromSeconds(10));
        files.Updating[2] = false;
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 1000 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        files.Updating[2] = true;
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 1100 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Number(store, SteamDownloadsProvider.Speed));
        time.Advance(TimeSpan.FromSeconds(20));
        Write(files, 2, Manifest(2, "Icarus", Going, 1300 * Megabyte, 1250 * Megabyte), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(7.5 * Megabyte, Number(store, SteamDownloadsProvider.Speed));
    }

    [Fact]
    public async Task How_far_a_download_has_got_stands_for_half_a_minute_after_steam_writes_it()
    {
        var files = new FakeGameFiles { Steam = "D:/Programs/Steam" };
        var time = new FakeTime(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));
        files.Updating[2] = true;
        Write(files, 2, Manifest(2, "Frosthaven", Going, 13_829_622_784, 13_323_208_464), time);
        using var provider = new SteamDownloadsProvider(files, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(96.34, Number(store, SteamDownloadsProvider.Progress)!.Value, 2);
        time.Advance(SteamDownloadsProvider.FreshFor - TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.NotNull(Number(store, SteamDownloadsProvider.Progress));

        // Steam has carried on without writing: how far it had got is out of date.
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Number(store, SteamDownloadsProvider.Progress));
        Assert.Equal(13_829_622_784, Number(store, SteamDownloadsProvider.Size));
        Assert.True(Downloads(store)[0].Stale);

        // Steam getting ready to start another: a download, with nothing to say yet but that it's going.
        files.Updating[3] = true;
        Write(files, 3, Manifest(3, "Age of Wonders 4", UpdateRequired | FullyInstalled), time);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["Age of Wonders 4", "Frosthaven"], Downloads(store).Select(d => d.Name));
    }

    [Fact]
    public async Task Steam_s_registry_is_asked_each_time_and_a_manifest_that_can_t_be_read_is_read_again()
    {
        var files = new FakeGameFiles { Steam = "D:/Programs/Steam" };
        var time = new FakeTime(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));
        Write(files, 2, Manifest(2, "Tunic", Going, 1000 * Megabyte, 400 * Megabyte), time);
        files.Unreadable.Add($@"{Apps}\appmanifest_2.acf");
        using var provider = new SteamDownloadsProvider(files, time);
        var store = new MetricStore();
        provider.Initialize(store);

        // Steam's writing it: nothing yet, but it isn't forgotten.
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(SteamDownloadsProvider.List));
        files.Unreadable.Clear();
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        var reads = files.TextReads;

        // Paused in Steam: the registry says so straight away, with the manifest as it was.
        files.Updating[2] = false;
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Paused", Text(store, SteamDownloadsProvider.State));
        files.Updating[2] = true;
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Downloading", Text(store, SteamDownloadsProvider.State));
        Assert.Equal(reads, files.TextReads);
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

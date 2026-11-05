using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class RecentGamesTests
{
    private const string SteamPath = "D:/Programs/Steam";
    private const string Steam = @"D:\Programs\Steam";
    private const string Cache = @"D:\Programs\Steam\appcache\librarycache";
    private const string Config = @"D:\Programs\Steam\userdata\12345\config\localconfig.vdf";
    private const string EpicData = @"C:\ProgramData\Epic\EpicGamesLauncher\Data";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CoverJpeg = [0xFF, 0xD8, 1];
    private static readonly byte[] IconPng = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static readonly Game Fortnite = new("Fortnite", null, new GameArt(null, null, null, IconPng), GameStore.Epic, "Fortnite");

    private static string Manifest(int appId, string name) =>
        $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"{name}\" \"StateFlags\" \"4\" \"installdir\" \"{name}\" }}";

    /// <summary>A localconfig.vdf as Steam writes it, with when each game was last played and its minutes.</summary>
    private static string LocalConfig(params (int AppId, DateTimeOffset? Last, int Minutes)[] apps) =>
        "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { "
        + string.Concat(apps.Select(a =>
            $"\"{a.AppId}\" {{ \"LastPlayed\" \"{a.Last?.ToUnixTimeSeconds() ?? 0}\" \"Playtime\" \"{a.Minutes}\" \"cloud\" {{ \"last_sync_state\" \"synchronized\" }} }} "))
        + "} } } } }";

    /// <summary>
    /// Steam with Hades (played two days ago, 41 hours), Hollow Knight (three hours ago, half an hour) and Terraria (never
    /// launched here) installed, and a game played an hour ago that isn't installed; and Fortnite from Epic.
    /// </summary>
    private static FakeGameFiles Library()
    {
        var files = new FakeGameFiles { Steam = SteamPath, Epic = EpicData };
        files.Texts[$@"{Steam}\steamapps\appmanifest_1145360.acf"] = Manifest(1145360, "Hades");
        files.Texts[$@"{Steam}\steamapps\appmanifest_367520.acf"] = Manifest(367520, "Hollow Knight");
        files.Texts[$@"{Steam}\steamapps\appmanifest_105600.acf"] = Manifest(105600, "Terraria");
        files.Texts[$@"{EpicData}\Manifests\F.item"] =
            """{ "AppName": "Fortnite", "DisplayName": "Fortnite", "InstallLocation": "C:\\Games\\Fortnite", "LaunchExecutable": "Fortnite.exe", "AppCategories": ["games"] }""";
        files.Pictures[$@"{Cache}\1145360\library_600x900.jpg"] = CoverJpeg;
        files.Icons[@"C:\Games\Fortnite\Fortnite.exe"] = IconPng;
        files.Texts[Config] = LocalConfig((1145360, Now.AddDays(-2), 2460), (367520, Now.AddHours(-3), 30), (105600, null, 600), (999, Now.AddHours(-1), 5));
        return files;
    }

    [Fact]
    public void Steam_s_record_gives_each_game_s_last_play_and_minutes()
    {
        var history = SteamLibrary.PlayHistory(LocalConfig((1145360, Now.AddDays(-2), 2460), (105600, null, 600), (7, null, 0)));
        Assert.Equal(new SteamPlay(Now.AddDays(-2), 2460), history[1145360]);
        Assert.Equal(new SteamPlay(null, 600), history[105600]);
        // Neither played nor timed: left out.
        Assert.False(history.ContainsKey(7));

        // Older Steam called the block Apps.
        Assert.Single(SteamLibrary.PlayHistory(LocalConfig((1, Now, 1)).Replace("\"apps\"", "\"Apps\"")));
        Assert.Empty(SteamLibrary.PlayHistory("\"UserLocalConfigStore\" { }"));
        Assert.Empty(SteamLibrary.PlayHistory("not a config"));
    }

    [Fact]
    public void The_newest_record_is_the_one_read_when_more_than_one_person_signs_in()
    {
        var files = new FakeGameFiles { Steam = SteamPath };
        const string First = @"D:\Programs\Steam\userdata\1\config\localconfig.vdf";
        const string Second = @"D:\Programs\Steam\userdata\2\config\localconfig.vdf";
        files.Texts[First] = "";
        files.Texts[Second] = "";
        files.Texts[@"D:\Programs\Steam\userdata\2\7\remote\settings.vdf"] = "";
        files.Written[First] = Now.UtcDateTime;
        files.Written[Second] = Now.UtcDateTime.AddDays(-1);

        Assert.Equal((First, Now.UtcDateTime), new SteamLibrary(files).LocalConfig());
        Assert.Null(new SteamLibrary(new FakeGameFiles()).LocalConfig());
        Assert.Null(new SteamLibrary(new FakeGameFiles { Steam = SteamPath }).LocalConfig());
    }

    [Fact]
    public async Task Installed_games_played_lately_come_newest_first_from_steam_s_record_and_the_app_s_own()
    {
        var played = TempFile();
        try
        {
            PlayedGames.Write(played, new Dictionary<string, PlayedGame> { ["epic:Fortnite"] = new(Now.AddHours(-5), 90) });
            using var provider = new RecentGamesProvider(played, Library(), new FakeTime(Now));
            var store = new MetricStore();
            provider.Initialize(store);

            await provider.SampleAsync(store, CancellationToken.None);
            var recent = Recent(store);
            // Terraria has never been played here, and the game played an hour ago isn't installed.
            Assert.Equal(["Hollow Knight", "Fortnite", "Hades"], recent.Select(r => r.Game.Name));
            Assert.Equal<double?>([0.5, 1.5, 41], recent.Select(r => r.Hours));
            Assert.Equal([Now.AddHours(-3), Now.AddHours(-5), Now.AddDays(-2)], recent.Select(r => r.LastPlayed));
            Assert.Equal("steam://rungameid/367520", recent[0].Game.Launch);
            Assert.Same(IconPng, recent[1].Art.Icon);
            Assert.Same(CoverJpeg, recent[2].Art.Cover);
            Assert.Equal("Hollow Knight", Text(store, RecentGamesProvider.Last));
            Assert.Null(Number(store, RecentGamesProvider.Hours));

            // Steam's own count for a Steam game in front.
            store.SetAttachment(GameArtProvider.Current, new Game("Hades", 1145360, new GameArt(CoverJpeg, null, null, null), GameStore.Steam, "1145360"));
            await provider.SampleAsync(store, CancellationToken.None);
            Assert.Equal(41, Number(store, RecentGamesProvider.Hours));
        }
        finally
        {
            File.Delete(played);
        }
    }

    [Fact]
    public async Task An_epic_game_s_time_in_front_is_counted_and_kept_once_it_leaves()
    {
        var played = TempFile();
        try
        {
            var time = new FakeTime(Now);
            using var provider = new RecentGamesProvider(played, Library(), time);
            var store = new MetricStore();
            provider.Initialize(store);

            store.SetAttachment(GameArtProvider.Current, Fortnite);
            await provider.SampleAsync(store, CancellationToken.None);
            Assert.Equal("Fortnite", Recent(store)[0].Game.Name);
            Assert.Equal(0, Number(store, RecentGamesProvider.Hours));

            // Two minutes in front.
            for (var i = 0; i < 8; i++)
            {
                time.Advance(provider.Interval);
                await provider.SampleAsync(store, CancellationToken.None);
            }
            Assert.Equal(2.0 / 60, Number(store, RecentGamesProvider.Hours)!.Value, 6);
            Assert.False(File.Exists(played));

            // It leaves: what was counted is saved straight away.
            store.SetAttachment(GameArtProvider.Current, null);
            time.Advance(provider.Interval);
            await provider.SampleAsync(store, CancellationToken.None);
            Assert.Null(Number(store, RecentGamesProvider.Hours));
            var saved = PlayedGames.Read(played)["epic:Fortnite"];
            Assert.Equal(2, saved.Minutes, 6);
            Assert.Equal(Now.AddMinutes(2), saved.Last);

            // Back in front, then the PC sleeps for an hour: the gap doesn't count.
            store.SetAttachment(GameArtProvider.Current, Fortnite);
            time.Advance(provider.Interval);
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromHours(1));
            await provider.SampleAsync(store, CancellationToken.None);
            Assert.Equal(2.0 / 60, Number(store, RecentGamesProvider.Hours)!.Value, 6);
        }
        finally
        {
            File.Delete(played);
        }

        // A record that can't be read is none.
        var broken = TempFile();
        try
        {
            File.WriteAllText(broken, "{ not json");
            Assert.Empty(PlayedGames.Read(broken));
            File.WriteAllText(broken, """{ "epic:Odd": { "last": "2026-11-05T22:00:00+00:00", "minutes": -5 }, "gog:1": null }""");
            Assert.Empty(PlayedGames.Read(broken));
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public async Task Steam_s_record_is_read_again_only_when_steam_rewrites_it_and_the_list_changes_only_with_it()
    {
        var files = Library();
        var time = new FakeTime(Now);
        using var provider = new RecentGamesProvider(null, files, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        var list = store.GetAttachment(RecentGamesProvider.Recent);
        var reads = files.TextReads;

        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(reads, files.TextReads);
        Assert.Same(list, store.GetAttachment(RecentGamesProvider.Recent));

        // A session of Terraria: Steam rewrites its record.
        files.Texts[Config] = LocalConfig((1145360, Now.AddDays(-2), 2460), (367520, Now.AddHours(-3), 30), (105600, Now.AddMinutes(1), 660));
        files.Written[Config] = Now.UtcDateTime.AddMinutes(1);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(reads + 1, files.TextReads);
        Assert.Equal(["Terraria", "Hollow Knight", "Hades"], Recent(store).Select(r => r.Game.Name));
        Assert.Equal("Terraria", Text(store, RecentGamesProvider.Last));
    }

    [Fact]
    public async Task Each_launcher_keeps_its_own_recent_games_so_a_widget_showing_one_launcher_has_them()
    {
        var files = new FakeGameFiles { Steam = SteamPath, Epic = EpicData };
        var steamGames = Enumerable.Range(1, RecentGamesProvider.Kept + 1).ToList();
        foreach (var i in steamGames)
            files.Texts[$@"{Steam}\steamapps\appmanifest_{i}.acf"] = Manifest(i, $"Game {i}");
        files.Texts[Config] = LocalConfig([.. steamGames.Select(i => (i, (DateTimeOffset?)Now.AddHours(-i), 60))]);
        files.Texts[$@"{EpicData}\Manifests\F.item"] =
            """{ "AppName": "Fortnite", "DisplayName": "Fortnite", "InstallLocation": "C:\\Games\\Fortnite", "AppCategories": ["games"] }""";
        var played = TempFile();
        try
        {
            PlayedGames.Write(played, new Dictionary<string, PlayedGame> { ["epic:Fortnite"] = new(Now.AddDays(-1), 90) });
            using var provider = new RecentGamesProvider(played, files, new FakeTime(Now));
            var store = new MetricStore();
            provider.Initialize(store);
            await provider.SampleAsync(store, CancellationToken.None);

            // Eight of Steam's, newest first, then Fortnite, played before all of them.
            Assert.Equal([.. steamGames.Take(RecentGamesProvider.Kept).Select(i => $"Game {i}"), "Fortnite"], Recent(store).Select(r => r.Game.Name));
        }
        finally
        {
            File.Delete(played);
        }
    }

    [Fact]
    public async Task Steam_s_record_that_can_t_be_read_just_now_is_read_next_time()
    {
        var files = Library();
        files.Unreadable.Add(Config);
        var time = new FakeTime(Now);
        using var provider = new RecentGamesProvider(null, files, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(RecentGamesProvider.Recent));

        files.Unreadable.Clear();
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["Hollow Knight", "Hades"], Recent(store).Select(r => r.Game.Name));
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"ib-played-{Guid.NewGuid():N}.json");

    private static IReadOnlyList<RecentGame> Recent(MetricStore store) =>
        Assert.IsAssignableFrom<IReadOnlyList<RecentGame>>(store.GetAttachment(RecentGamesProvider.Recent));

    private static double? Number(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

using System.Runtime.InteropServices;
using System.Text;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Tests;

public sealed class GogGalaxyTests
{
    private const string User = "46987648922387683";

    /// <summary>
    /// The tables and columns of GOG Galaxy 2.0's database that the app reads, as Galaxy lays them out, with what it had
    /// on a PC with Galaxy: Cyberpunk 2077 installed, 64.5 hours, 25 of 57 achievements; Blast Brigade, played today,
    /// 52 achievements and none unlocked; and Satisfactory from the Epic Games launcher, 120 hours by Epic's count.
    /// </summary>
    private const string Galaxy = $"""
        create table GameTimes (userId integer not null, releaseKey text not null, minutesInGame integer not null, primary key (userId, releaseKey));
        create table LastPlayedDates (userId integer not null, gameReleaseKey text not null, lastPlayedDate text, primary key (userId, gameReleaseKey));
        create table UserAchievements (gameReleaseKey text not null, userId integer not null, apikey text not null, unlockTime text, isUnlocked integer not null);
        create table InstalledBaseProducts (productId integer primary key, installationPath text);
        create table WebCache (id integer primary key, releaseKey text not null, userId integer not null);
        create table WebCacheResourceTypes (id integer primary key, type text not null);
        create table WebCacheResources (webCacheId integer not null, webCacheResourceTypeId integer not null, filename text not null);
        insert into GameTimes values ({User}, 'gog_1423049311', 3873), ({User}, 'gog_1504894622', 1), ({User}, 'epic_CrabEA', 7222), ({User}, 'steam_640820', 12204);
        insert into LastPlayedDates values ({User}, 'gog_1423049311', '2025-12-03 18:36:02'), ({User}, 'gog_1504894622', '2026-11-06 16:24:31');
        with recursive n(i) as (select 1 union all select i + 1 from n where i < 57)
            insert into UserAchievements select 'gog_1423049311', {User}, 'a' || i, null, i <= 25 from n;
        with recursive n(i) as (select 1 union all select i + 1 from n where i < 52)
            insert into UserAchievements select 'gog_1504894622', {User}, 'b' || i, null, 0 from n;
        insert into InstalledBaseProducts values (1423049311, 'D:\Programs\GOG Galaxy\Games\Cyberpunk 2077'), (1504894622, 'D:\Programs\GOG Galaxy\Games\Blast Brigade');
        insert into WebCacheResourceTypes values (1, 'background'), (2, 'squareIcon'), (3, 'verticalCover');
        insert into WebCache values (2050, 'gog_1423049311', {User}), (7, 'epic_CrabEA', {User});
        insert into WebCacheResources values (2050, 1, 'b6cd_glx_bg_top_padding_7.webp'), (2050, 2, 'd874_glx_square_icon_v2.webp'), (2050, 3, '8b37_glx_vertical_cover.webp'),
            (7, 3, 'a710_glx_vertical_cover.webp'), (7, 1, '../escape.webp');
        """;

    [Fact]
    public void Galaxy_s_database_gives_play_time_last_played_achievements_and_pictures()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = Database(Galaxy);
        try
        {
            var games = GogGalaxy.Read(path, ["gog_1423049311", "gog_1504894622", "epic_CrabEA", "gog_404"]);

            Assert.Equal(["epic_CrabEA", "gog_1423049311", "gog_1504894622"], games.Keys.Order());
            var cyberpunk = games["gog_1423049311"];
            Assert.Equal(3873, cyberpunk.Minutes);
            Assert.Equal(new DateTimeOffset(2025, 12, 3, 18, 36, 2, TimeSpan.Zero), cyberpunk.LastPlayed);
            Assert.Equal(new AchievementCount(25, 57), cyberpunk.Achievements);
            Assert.True(cyberpunk.Installed);
            var pictures = Path.Combine(GogGalaxy.DataFolder, "webcache", User, "gog", "1423049311");
            Assert.Equal(Path.Combine(pictures, "8b37_glx_vertical_cover.webp"), cyberpunk.Cover);
            Assert.Equal(Path.Combine(pictures, "b6cd_glx_bg_top_padding_7.webp"), cyberpunk.Background);
            Assert.Equal(Path.Combine(pictures, "d874_glx_square_icon_v2.webp"), cyberpunk.Icon);

            // None of 52 unlocked is still achievements.
            Assert.Equal(new AchievementCount(0, 52), games["gog_1504894622"].Achievements);
            // Epic's: play time through Galaxy's integration, no date or achievements, and only its sound pictures.
            var satisfactory = games["epic_CrabEA"];
            Assert.Equal((7222, (DateTimeOffset?)null, (AchievementCount?)null, false), (satisfactory.Minutes, satisfactory.LastPlayed, satisfactory.Achievements, satisfactory.Installed));
            Assert.Equal(Path.Combine(GogGalaxy.DataFolder, "webcache", User, "epic", "CrabEA", "a710_glx_vertical_cover.webp"), satisfactory.Cover);
            Assert.Null(satisfactory.Background);

            Assert.Empty(GogGalaxy.Read(path, []));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_database_not_laid_out_as_galaxy_s_can_t_be_read()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = Database("create table Something (id integer);");
        try
        {
            Assert.Throws<IOException>(() => GogGalaxy.Read(path, ["gog_1"]));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Throws<IOException>(() => GogGalaxy.Read(Path.Combine(Path.GetTempPath(), $"ib-none-{Guid.NewGuid():N}", "galaxy-2.0.db"), ["gog_1"]));
    }

    [Fact]
    public void Galaxy_s_dates_are_utc_and_keys_join_the_launcher_and_its_id()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 16, 24, 31, TimeSpan.Zero), GogGalaxy.Utc("2026-11-06 16:24:31"));
        Assert.Null(GogGalaxy.Utc("yesterday"));
        Assert.Null(GogGalaxy.Utc(null));
        Assert.Equal("gog_1423049311", GogGalaxy.KeyOf(GameStore.Gog, "1423049311"));
        Assert.Equal("epic_CrabEA", GogGalaxy.KeyOf(GameStore.Epic, "CrabEA"));
    }

    [Fact]
    public void The_app_s_own_minutes_shrink_by_what_galaxy_adds_so_no_session_counts_twice()
    {
        var last = new DateTimeOffset(2026, 11, 6, 16, 22, 29, TimeSpan.Zero);

        // The first time the app sees Galaxy's count, its own minutes are taken to be in it, but for any more.
        var agentA = new PlayedGame(last, 3).WithGalaxy(88);
        Assert.Equal((0.0, (double?)88, 88.0), (agentA.Minutes, agentA.Galaxy, agentA.Total));
        Assert.Equal(5, new PlayedGame(last, 5).WithGalaxy(4).Total);

        // Ten minutes the app saw, four of which Galaxy has since added: the other six only the app saw, as from an exe.
        var counted = new PlayedGame(last, 10) { Galaxy = 100 }.WithGalaxy(104);
        Assert.Equal((6.0, 110.0), (counted.Minutes, counted.Total));
        // Galaxy hasn't added anything: as it was.
        Assert.Equal(new PlayedGame(last, 10) { Galaxy = 100 }, new PlayedGame(last, 10) { Galaxy = 100 }.WithGalaxy(100));
        // Galaxy added more than the app saw, from when the app wasn't running: Galaxy's count.
        Assert.Equal(120, new PlayedGame(last, 3) { Galaxy = 100 }.WithGalaxy(120).Total);
        // A count that went down, as after Galaxy was reinstalled, takes nothing off.
        Assert.Equal(95, new PlayedGame(last, 5) { Galaxy = 100 }.WithGalaxy(90).Total);
    }

    [Fact]
    public void A_gog_game_galaxy_has_installed_starts_through_galaxy_as_its_shortcuts_do()
    {
        const string Client = @"D:\Programs\GOG Galaxy\GalaxyClient.exe";
        var files = new FakeGameFiles { GalaxyExe = Client, Galaxy = new() { ["gog_1504894622"] = new GalaxyGame(1, null, null, Installed: true) } };
        files.Gog.Add(new RegistryEntry("1504894622", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gameID"] = "1504894622",
            ["gameName"] = "Blast Brigade vs. the Evil Legion of Dr. Cread",
            ["path"] = @"D:\Programs\GOG Galaxy\Games\Blast Brigade",
            ["exe"] = @"D:\Programs\GOG Galaxy\Games\Blast Brigade\Blast Brigade.exe",
        }));
        // Installed with GOG's own installer, not by Galaxy: its exe, as before.
        files.Gog.Add(new RegistryEntry("1207658924", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gameName"] = "Unreal Gold",
            ["path"] = @"C:\GOG Games\Unreal Gold",
            ["exe"] = @"C:\GOG Games\Unreal Gold\System\Unreal.exe",
            ["workingDir"] = @"C:\GOG Games\Unreal Gold",
        }));

        var games = new GogLibrary(files).Games();
        var blast = games.Single(g => g.Id == "1504894622");
        Assert.Equal((Client, @"/command=runGame /gameId=1504894622 /path=""D:\Programs\GOG Galaxy\Games\Blast Brigade""", @"D:\Programs\GOG Galaxy"),
            (blast.Launch, blast.Arguments, blast.WorkingDirectory));
        Assert.Equal(@"D:\Programs\GOG Galaxy\Games\Blast Brigade\Blast Brigade.exe", blast.Exe);
        var unreal = games.Single(g => g.Id == "1207658924");
        Assert.Equal((@"C:\GOG Games\Unreal Gold\System\Unreal.exe", @"C:\GOG Games\Unreal Gold"), (unreal.Launch, unreal.WorkingDirectory));

        // Without Galaxy, or when it can't be read just now, each starts by its exe.
        files.GalaxyBusy = true;
        Assert.Equal(@"D:\Programs\GOG Galaxy\Games\Blast Brigade\Blast Brigade.exe", new GogLibrary(files).Games().Single(g => g.Id == "1504894622").Launch);
        files.GalaxyBusy = false;
        files.GalaxyExe = null;
        Assert.Equal(@"D:\Programs\GOG Galaxy\Games\Blast Brigade\Blast Brigade.exe", new GogLibrary(files).Games().Single(g => g.Id == "1504894622").Launch);
    }

    [Fact]
    public async Task A_gog_game_s_achievements_come_from_galaxy_and_steam_s_are_left_to_steam()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 11, 6, 18, 0, 0, TimeSpan.Zero));
        var files = new FakeGameFiles { Galaxy = new() { ["gog_1423049311"] = new GalaxyGame(3873, null, new AchievementCount(25, 57), true) } };
        using var galaxy = new GalaxyAchievementsProvider(files, time);
        using var steam = new SteamFriendsProvider(() => null, time: time);
        var store = new MetricStore();
        galaxy.Initialize(store);
        steam.Initialize(store);
        var cyberpunk = new Game("Cyberpunk 2077", null, new GameArt(null, null, null, null), GameStore.Gog, "1423049311");

        store.SetAttachment(GameArtProvider.Current, cyberpunk);
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Equal("25 / 57", Text(store, GameAchievements.Text));
        Assert.Equal(25 * 100.0 / 57, Number(store, GameAchievements.Percent)!.Value, 6);
        // Steam, with no key and no Steam game in front, leaves them be.
        await steam.SampleAsync(store, CancellationToken.None);
        Assert.Equal("25 / 57", Text(store, GameAchievements.Text));

        // Read again only once Galaxy's record has changed, at most every minute.
        var reads = files.GalaxyReads;
        time.Advance(galaxy.Interval);
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Equal(reads, files.GalaxyReads);
        files.Galaxy["gog_1423049311"] = new GalaxyGame(3880, null, new AchievementCount(26, 57), true);
        files.GalaxyChanged = files.GalaxyChanged.AddMinutes(1);
        time.Advance(GalaxyAchievementsProvider.ReadEvery);
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Equal("26 / 57", Text(store, GameAchievements.Text));

        // A Steam game in front: cleared once, then Steam's to write.
        store.SetAttachment(GameArtProvider.Current, new Game("Hades", 1145360, new GameArt(null, null, null, null), GameStore.Steam, "1145360"));
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Null(Text(store, GameAchievements.Text));
        GameAchievements.Publish(store, new AchievementCount(3, 10));
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Equal("3 / 10", Text(store, GameAchievements.Text));

        // A GOG game Galaxy has no achievements for has none.
        store.SetAttachment(GameArtProvider.Current, new Game("Unreal Gold", null, new GameArt(null, null, null, null), GameStore.Gog, "1207658924"));
        await galaxy.SampleAsync(store, CancellationToken.None);
        Assert.Null(Text(store, GameAchievements.Text));
    }

    /// <summary>A new SQLite database in the temp folder, made by <paramref name="sql"/> with Windows' own SQLite.</summary>
    private static string Database(string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ib-galaxy-{Guid.NewGuid():N}.db");
        Assert.Equal(0, sqlite3_open16(path, out var db));
        try
        {
            Assert.Equal(0, sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, out _));
        }
        finally
        {
            sqlite3_close(db);
        }
        return path;
    }

    [DllImport("winsqlite3.dll", CharSet = CharSet.Unicode)]
    private static extern int sqlite3_open16(string filename, out IntPtr db);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr argument, out IntPtr error);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_close(IntPtr db);

    private static double? Number(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

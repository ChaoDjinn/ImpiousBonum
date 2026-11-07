using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class BattleNetTests
{
    private const string Config = @"C:\Users\Sam\AppData\Roaming\Battle.net\Battle.net.config";
    private const string Client = @"C:\Program Files (x86)\Battle.net\Battle.net.exe";

    private static RegistryEntry Entry(string key, string uid, string name, string folder, string icon) =>
        new(key, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DisplayName"] = name,
            ["InstallLocation"] = folder,
            ["DisplayIcon"] = icon,
            ["UninstallString"] = $"\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid={uid} --displayname=\"{name}\"",
        });

    /// <summary>Battle.net with Diablo IV, StarCraft II, an old beta whose folder has gone, a game it can't name a code for, and Notepad++.</summary>
    private static FakeGameFiles Files()
    {
        var files = new FakeGameFiles();
        files.Uninstall.AddRange(
        [
            Entry("Battle.net", "battle.net", "Battle.net", @"C:\Program Files (x86)\Battle.net", Client),
            Entry("Diablo IV", "fenris", "Diablo IV", @"E:\Diablo IV", @"E:\Diablo IV\Diablo IV.exe"),
            Entry("StarCraft II", "s2", "StarCraft II", @"D:\StarCraft II", @"""D:\StarCraft II\Support\SC2Switcher.exe"",0"),
            Entry("Diablo IV Beta", "fenris_beta", "Diablo IV Beta", @"E:\Diablo IV - Beta", @"E:\Diablo IV - Beta\Diablo IV.exe"),
            Entry("Warcraft Rumble", "gryphon", "Warcraft Rumble", @"D:\Warcraft Rumble", @"D:\Warcraft Rumble\Rumble.exe"),
            new RegistryEntry("Notepad++", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DisplayName"] = "Notepad++",
                ["InstallLocation"] = @"C:\Program Files\Notepad++",
                ["UninstallString"] = @"C:\Program Files\Notepad++\uninstall.exe",
            }),
        ]);
        foreach (var exe in new[] { Client, @"E:\Diablo IV\Diablo IV.exe", @"D:\StarCraft II\Support\SC2Switcher.exe", @"D:\Warcraft Rumble\Rumble.exe" })
            files.Texts[exe] = "";
        return files;
    }

    [Fact]
    public void Its_games_come_from_their_uninstall_entries_and_start_through_Battle_net()
    {
        var games = new BattleNetLibrary(Files(), Config).Games();

        Assert.Equal(["fenris", "s2", "gryphon"], games.Select(g => g.Id));
        var diablo = games[0];
        Assert.Equal((GameStore.BattleNet, "Diablo IV", @"E:\Diablo IV", @"E:\Diablo IV\Diablo IV.exe"), (diablo.Store, diablo.Name, diablo.Folder, diablo.Exe));
        Assert.Equal((Client, "--exec=\"launch Fen\""), (diablo.Launch, diablo.Arguments));
        Assert.Equal(@"D:\StarCraft II\Support\SC2Switcher.exe", games[1].Exe);
        Assert.Equal("--exec=\"launch S2\"", games[1].Arguments);
        // No launch code known: its own exe, which hands over to Battle.net.
        Assert.Equal((@"D:\Warcraft Rumble\Rumble.exe", ""), (games[2].Launch, games[2].Arguments));
        Assert.Equal("battlenet:fenris", diablo.Key);
        Assert.Equal("Battle.net", StoreGame.StoreName(GameStore.BattleNet));
    }

    [Theory]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=fenris --displayname=\"Diablo IV\"", "fenris")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enGB --uid=WoW", "wow")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS", null)]
    [InlineData(@"C:\Program Files\Other\uninstall.exe --uid=fenris", null)]
    [InlineData(null, null)]
    public void The_uid_comes_from_Battle_net_s_uninstaller_only(string? uninstall, string? uid) =>
        Assert.Equal(uid, BattleNetLibrary.Uid(uninstall));

    [Theory]
    [InlineData(@"D:\World of Warcraft\_retail_\WoW.exe", @"D:\World of Warcraft\_retail_\WoW.exe")]
    [InlineData(@"""D:\StarCraft II\Support\SC2Switcher.exe"",0", @"D:\StarCraft II\Support\SC2Switcher.exe")]
    [InlineData(@"D:\Game,1\game.exe,-101", @"D:\Game,1\game.exe")]
    [InlineData(@"C:\Games\icon.ico", null)]
    public void The_exe_comes_from_the_entry_s_icon(string icon, string? exe) =>
        Assert.Equal(exe, BattleNetLibrary.ExeOf(icon));

    [Fact]
    public void When_each_game_was_last_played_comes_from_Battle_net_s_settings()
    {
        var played = BattleNetLibrary.LastPlayed("""
            { "Client": { }, "Games": {
                "battle_net": { "ServerUid": "battle.net" },
                "fenris": { "ServerUid": "fenris", "LastPlayed": "1690651752" },
                "wow": { "LastPlayed": 1760032137 },
                "s2": { "LastPlayed": "soon" } } }
            """);

        Assert.Equal(new Dictionary<string, DateTimeOffset>
        {
            ["fenris"] = DateTimeOffset.FromUnixTimeSeconds(1690651752),
            ["wow"] = DateTimeOffset.FromUnixTimeSeconds(1760032137),
        }, played);
        Assert.Empty(BattleNetLibrary.LastPlayed("not json"));
    }

    [Fact]
    public void Battle_net_s_games_are_in_recent_games_by_when_it_says_they_were_last_played()
    {
        var files = Files();
        files.Texts[Config] = """{ "Games": { "fenris": { "LastPlayed": "1762000000" } } }""";
        files.Written[Config] = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var store = new MetricStore();
        using var provider = new RecentGamesProvider(null, files, new FakeTime(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)), Config);
        provider.Initialize(store);

        provider.SampleAsync(store, CancellationToken.None);

        var recent = Assert.IsAssignableFrom<IReadOnlyList<RecentGame>>(store.GetAttachment(RecentGamesProvider.Recent));
        var diablo = Assert.Single(recent);
        Assert.Equal(("Diablo IV", DateTimeOffset.FromUnixTimeSeconds(1762000000)), (diablo.Game.Name, diablo.LastPlayed));
        // Battle.net doesn't say for how long; the app counts that itself once it sees the game.
        Assert.Equal(0, diablo.Hours);
    }

    [Fact]
    public void An_exe_in_a_Battle_net_game_s_folder_is_that_game()
    {
        var finder = new GameFinder(Files());

        var game = finder.Find(@"D:\StarCraft II\Versions\Base93272\SC2_x64.exe", anyApp: false);

        Assert.NotNull(game);
        Assert.Equal(("StarCraft II", GameStore.BattleNet, "s2"), (game.Name, game.Store, game.StoreId));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

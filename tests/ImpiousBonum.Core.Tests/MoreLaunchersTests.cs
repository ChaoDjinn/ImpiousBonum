using System.Text;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class MoreLaunchersTests
{
    private static RegistryEntry Entry(string key, params (string Name, string Value)[] values) =>
        new(key, values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void EA_s_games_come_from_entries_its_installer_wrote_and_start_by_their_exe()
    {
        var files = new FakeGameFiles();
        const string Exe = @"E:\EA Games\Jedi Fallen Order\SwGame\Binaries\Win64\starwarsjedifallenorder.exe";
        files.Uninstall.AddRange(
        [
            Entry("{D00A89F1}", ("DisplayName", "STAR WARS Jedi - Fallen Order™"), ("InstallLocation", @"E:\EA Games\Jedi Fallen Order\"),
                ("DisplayIcon", $"\"{Exe}\""), ("UninstallString", "\"C:\\Program Files\\Common Files\\EAInstaller\\Jedi Fallen Order\\Cleanup.exe\" uninstall_game")),
            // Origin's older games: no EAInstaller uninstaller, but EA's notes in the folder.
            Entry("{11111111}", ("DisplayName", "Titanfall® 2"), ("InstallLocation", @"D:\Origin Games\Titanfall2"),
                ("DisplayIcon", @"D:\Origin Games\Titanfall2\Titanfall2.exe"), ("UninstallString", @"D:\Origin Games\Titanfall2\__Installer\Cleanup.exe")),
            // The EA app itself, and a game that's gone.
            Entry("{7aaf26af}", ("DisplayName", "EA app"), ("UninstallString", "\"C:\\Program Files\\Electronic Arts\\EA Desktop\\EA Desktop\\EAUninstall.exe\" /uninstall")),
            Entry("{22222222}", ("DisplayName", "Gone"), ("InstallLocation", @"F:\Gone"), ("DisplayIcon", @"F:\Gone\gone.exe"),
                ("UninstallString", @"C:\Program Files\Common Files\EAInstaller\Gone\Cleanup.exe")),
        ]);
        files.Texts[Exe] = "";
        files.Texts[@"D:\Origin Games\Titanfall2\Titanfall2.exe"] = "";
        files.Texts[@"D:\Origin Games\Titanfall2\__Installer\installerdata.xml"] = "<DiPManifest/>";

        var games = new EaLibrary(files).Games();

        Assert.Equal(["STAR WARS Jedi - Fallen Order", "Titanfall 2"], games.Select(g => g.Name));
        Assert.Equal((GameStore.Ea, @"E:\EA Games\Jedi Fallen Order", Exe, Exe), (games[0].Store, games[0].Folder, games[0].Exe, games[0].Launch));
        Assert.Equal("EA", StoreGame.StoreName(GameStore.Ea));
    }

    [Fact]
    public void Ubisoft_s_games_come_from_its_registry_named_by_their_uninstall_entries_and_start_through_it()
    {
        var files = new FakeGameFiles();
        files.Registry[UbisoftLibrary.RegistryPath] =
        [
            Entry("4311", ("InstallDir", "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Far Cry 5/")),
            Entry("5595", ("InstallDir", "D:/Games/Anno 1800/")),
            Entry("999"),
        ];
        files.Uninstall.Add(Entry("Uplay Install 4311", ("DisplayName", "Far Cry 5"), ("DisplayIcon", @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Far Cry 5\bin\FarCry5.exe")));

        var games = new UbisoftLibrary(files).Games();

        Assert.Equal(["Far Cry 5", "Anno 1800"], games.Select(g => g.Name));
        Assert.Equal(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Far Cry 5", games[0].Folder);
        Assert.Equal("uplay://launch/4311/0", games[0].Launch);
        Assert.EndsWith(@"\bin\FarCry5.exe", games[0].Exe);
        Assert.Null(games[1].Exe);
    }

    [Fact]
    public void Xbox_games_come_from_their_folders_own_description_and_start_as_the_Start_menu_does()
    {
        var files = new FakeGameFiles();
        files.XboxRoots.Add(@"D:\XboxGames");
        const string Folder = @"D:\XboxGames\Dune- Spice Wars";
        files.Texts[$@"{Folder}\Content\MicrosoftGame.config"] = """
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="FuncomOsloAS.DuneSpiceWars" Publisher="CN=Example" Version="1.0.0.0" />
              <ExecutableList><Executable Name="Launcher.exe" Id="Game" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Dune: Spice Wars" PublisherDisplayName="Funcom" />
            </Game>
            """;
        files.Texts[$@"{Folder}\Content\appxmanifest.xml"] = """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Applications><Application Id="Launcher" Executable="Launcher.exe" /></Applications>
            </Package>
            """;
        // A folder that isn't a game: the Xbox app's saves.
        files.Texts[@"D:\XboxGames\GameSave\wgs.dat"] = "";

        var game = Assert.Single(new XboxLibrary(files).Games());

        Assert.Equal((GameStore.Xbox, "FuncomOsloAS.DuneSpiceWars", "Dune: Spice Wars", Folder), (game.Store, game.Id, game.Name, game.Folder));
        Assert.Equal($@"{Folder}\Content\Launcher.exe", game.Exe);
        Assert.Equal($@"shell:AppsFolder\{XboxLibrary.FamilyName("FuncomOsloAS.DuneSpiceWars", "CN=Example")}!Launcher", game.Launch);
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator", "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft.WindowsCalculator_8wekyb3d8bbwe")]
    [InlineData("FuncomOsloAS.DuneSpiceWars", "CN=Example", "FuncomOsloAS.DuneSpiceWars_")]
    public void A_package_s_family_name_is_its_name_and_its_publisher_s_hash(string name, string publisher, string expected) =>
        Assert.StartsWith(expected, XboxLibrary.FamilyName(name, publisher));

    [Fact]
    public void A_drive_s_GamingRoot_names_its_Xbox_games_folders()
    {
        var marker = new byte[] { (byte)'R', (byte)'G', (byte)'B', (byte)'X', 1, 0, 0, 0 }.Concat(Encoding.Unicode.GetBytes("XboxGames\0")).ToArray();

        Assert.Equal([@"D:\XboxGames"], XboxLibrary.GamingRoot(@"D:\", marker));
        Assert.Empty(XboxLibrary.GamingRoot(@"D:\", [1, 2, 3]));
    }

    [Fact]
    public void Amazon_s_games_come_from_its_database_with_their_exe_from_fuel_json_and_start_through_it()
    {
        var files = new FakeGameFiles
        {
            AmazonGames = new AmazonData(
                [new AmazonInstall("amzn1.adg.product.1234", "Fallout 3: Game of the Year Edition", @"C:\Amazon Games\Library\Fallout 3\")],
                new Dictionary<string, DateTimeOffset> { ["amzn1.adg.product.1234"] = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero) }),
        };
        files.Texts[@"C:\Amazon Games\Library\Fallout 3\fuel.json"] = """{ "SchemaVersion": "2", "Main": { "Command": "Fallout3.exe", "Args": [] } }""";

        var library = new AmazonLibrary(files);
        var game = Assert.Single(library.Games());

        Assert.Equal((GameStore.Amazon, "Fallout 3: Game of the Year Edition", @"C:\Amazon Games\Library\Fallout 3"), (game.Store, game.Name, game.Folder));
        Assert.Equal("amazon-games://play/amzn1.adg.product.1234", game.Launch);
        Assert.Equal(@"C:\Amazon Games\Library\Fallout 3\Fallout3.exe", game.Exe);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero), library.LastPlayed()["amzn1.adg.product.1234"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 21, 14, 3, 123, TimeSpan.Zero), AmazonLibrary.When("2026-11-05 21:14:03.123"));
        Assert.Null(AmazonLibrary.When("soon"));
    }

    [Fact]
    public void Their_games_are_found_by_folder_and_in_recent_games_once_played()
    {
        var files = new FakeGameFiles
        {
            AmazonGames = new AmazonData(
                [new AmazonInstall("amzn1.adg.product.1234", "Fallout 3", @"C:\Amazon Games\Library\Fallout 3")],
                new Dictionary<string, DateTimeOffset> { ["amzn1.adg.product.1234"] = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero) }),
        };
        files.Registry[UbisoftLibrary.RegistryPath] = [Entry("4311", ("InstallDir", "D:/Games/Far Cry 5/"))];

        var finder = new GameFinder(files);
        Assert.Equal(GameStore.Ubisoft, finder.Find(@"D:\Games\Far Cry 5\bin\FarCry5.exe", anyApp: false)?.Store);
        Assert.Equal(GameStore.Amazon, finder.Find(@"C:\Amazon Games\Library\Fallout 3\Fallout3.exe", anyApp: false)?.Store);

        var store = new MetricStore();
        using var provider = new RecentGamesProvider(null, files, new FakeTime(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)));
        provider.Initialize(store);
        provider.SampleAsync(store, CancellationToken.None);

        // Amazon says when; Ubisoft doesn't, so Far Cry shows once the app has seen it played.
        var recent = Assert.IsAssignableFrom<IReadOnlyList<RecentGame>>(store.GetAttachment(RecentGamesProvider.Recent));
        Assert.Equal(["Fallout 3"], recent.Select(r => r.Game.Name));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

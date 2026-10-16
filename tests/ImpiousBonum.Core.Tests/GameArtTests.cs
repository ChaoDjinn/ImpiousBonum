using System.IO.Enumeration;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class GameArtTests
{
    // Steam writes its folder to the registry with forward slashes.
    private const string SteamPath = "D:/Programs/Steam";
    private const string Steam = @"D:\Programs\Steam";
    private const string Cache = @"D:\Programs\Steam\appcache\librarycache";

    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"D:\\Programs\\Steam"
        		"label"		""
        		"apps"
        		{
        			"1145360"		"7536274051"
        		}
        	}
        	"1"
        	{
        		"path"		"E:\\SteamLibrary"
        		"apps"
        		{
        			"367520"		"5231995691"
        		}
        	}
        }
        """;

    private static readonly byte[] CoverJpeg = [0xFF, 0xD8, 1];
    private static readonly byte[] HeaderJpeg = [0xFF, 0xD8, 2];
    private static readonly byte[] HeroJpeg = [0xFF, 0xD8, 3];
    private static readonly byte[] IconPng = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static string Manifest(int appId, string name, string installDir) => $$"""
        "AppState"
        {
        	"appid"		"{{appId}}"
        	"universe"		"1"
        	"name"		"{{name}}"
        	"StateFlags"		"4"
        	"installdir"		"{{installDir}}"
        }
        """;

    /// <summary>Steam with Hades in its own folder and Hollow Knight in a library on another drive.</summary>
    private static FakeFiles TwoLibraries()
    {
        var files = new FakeFiles { Steam = SteamPath };
        files.Texts[$@"{Steam}\steamapps\libraryfolders.vdf"] = LibraryFolders;
        files.Texts[$@"{Steam}\steamapps\appmanifest_1145360.acf"] = Manifest(1145360, "Hades", "Hades");
        files.Texts[@"E:\SteamLibrary\steamapps\appmanifest_367520.acf"] = Manifest(367520, "Hollow Knight", "Hollow Knight");
        return files;
    }

    [Fact]
    public void Vdf_reads_quoted_keys_values_and_blocks_ignoring_case_and_comments()
    {
        var root = Vdf.Parse("""
            // Written by Steam
            "AppState"
            {
            	"AppID"		"1145360"
            	"name"		"Say \"Hi\" \\ bye"
            	"UserConfig" { "language" "english" }
            	bare	word
            }
            """);

        var state = root.Block("appstate")!;
        Assert.Equal("1145360", state.Text("appid"));
        Assert.Equal("Say \"Hi\" \\ bye", state.Text("NAME"));
        Assert.Equal("english", state.Block("userconfig")!.Text("Language"));
        Assert.Equal("word", state.Text("bare"));
        Assert.Equal(["AppID", "name", "UserConfig", "bare"], state.Keys);
        Assert.Null(state.Text("UserConfig"));
        Assert.Null(state.Block("name"));
    }

    [Fact]
    public void Vdf_that_stops_short_keeps_what_it_read()
    {
        var root = Vdf.Parse("\"AppState\" { \"appid\" \"10\" \"name\" ");
        Assert.Equal("10", root.Block("AppState")!.Text("appid"));
        Assert.Empty(Vdf.Parse("").Keys);
    }

    [Fact]
    public void Library_folders_come_from_the_newer_and_the_older_file()
    {
        Assert.Equal([@"D:\Programs\Steam", @"E:\SteamLibrary"], SteamLibrary.LibraryFolders(LibraryFolders));

        // Before 2021 the file listed the other libraries' paths straight after their numbers.
        Assert.Equal([@"F:\Games\Steam"], SteamLibrary.LibraryFolders("""
            "LibraryFolders"
            {
            	"TimeNextStatsReport"		"1612345678"
            	"ContentStatsID"		"-4557178473925224712"
            	"1"		"F:\\Games\\Steam"
            }
            """));
        Assert.Empty(SteamLibrary.LibraryFolders("\"something\" { }"));
    }

    [Fact]
    public void A_manifest_gives_the_game_its_id_name_and_folder()
    {
        var app = SteamLibrary.Manifest(Manifest(367520, "Hollow Knight", "Hollow Knight"), @"E:\SteamLibrary")!;
        Assert.Equal(new SteamApp(367520, "Hollow Knight", @"E:\SteamLibrary\steamapps\common\Hollow Knight"), app);

        // No name falls back to the folder; no id or folder isn't a game.
        Assert.Equal("Hades", SteamLibrary.Manifest(Manifest(1, "", "Hades"), Steam)!.Name);
        Assert.Null(SteamLibrary.Manifest(Manifest(1, "Hades", ""), Steam));
        Assert.Null(SteamLibrary.Manifest("\"AppState\" { \"appid\" \"x\" \"installdir\" \"Hades\" }", Steam));
        Assert.Null(SteamLibrary.Manifest("not a manifest", Steam));
    }

    [Fact]
    public void Every_library_s_games_are_found_and_steam_s_folder_counts_without_being_listed()
    {
        var files = TwoLibraries();
        Assert.Equal(["Hades", "Hollow Knight"], new SteamLibrary(files).Apps().Select(a => a.Name).Order());

        // Steam often writes its folder in lower case, which is still the library the file lists.
        var lower = new FakeFiles { Steam = "d:/programs/steam" };
        foreach (var (path, text) in files.Texts)
            lower.Texts[path] = text;
        Assert.Equal(2, new SteamLibrary(lower).Apps().Count);

        // An older libraryfolders.vdf doesn't list Steam's own folder.
        files.Texts[$@"{Steam}\steamapps\libraryfolders.vdf"] = "\"LibraryFolders\" { \"1\" \"E:\\\\SteamLibrary\" }";
        Assert.Equal(2, new SteamLibrary(files).Apps().Count);

        Assert.Empty(new SteamLibrary(new FakeFiles()).Apps());
    }

    [Theory]
    [InlineData(@"D:\Programs\Steam\steamapps\common\Hades\x64\Hades.exe", 1145360)]
    [InlineData(@"d:\programs\steam\STEAMAPPS\common\hades\Hades.exe", 1145360)]
    [InlineData(@"D:/Programs/Steam/steamapps/common/Hades/Hades.exe", 1145360)]
    [InlineData(@"E:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe", 367520)]
    [InlineData(@"E:\SteamLibrary\steamapps\common\Hollow Knight 2\Silksong.exe", null)]
    [InlineData(@"D:\Programs\Steam\steamapps\common\Hades.exe", null)]
    [InlineData(@"D:\Programs\Steam\steam.exe", null)]
    [InlineData(@"C:\Windows\explorer.exe", null)]
    public void An_exe_belongs_to_the_game_whose_folder_it_is_in_at_any_depth(string exe, int? appId)
    {
        var apps = new SteamLibrary(TwoLibraries()).Apps();
        Assert.Equal(appId, SteamLibrary.Match(apps, exe)?.AppId);
    }

    [Fact]
    public void A_game_installed_inside_another_s_folder_wins_for_its_own_exes()
    {
        SteamApp[] apps = [new(1, "Outer", @"D:\Steam\steamapps\common\Outer"), new(2, "Inner", @"D:\Steam\steamapps\common\Outer\Inner")];
        Assert.Equal(2, SteamLibrary.Match(apps, @"D:\Steam\steamapps\common\Outer\Inner\game.exe")!.AppId);
        Assert.Equal(1, SteamLibrary.Match(apps, @"D:\Steam\steamapps\common\Outer\game.exe")!.AppId);
    }

    [Fact]
    public void Pictures_are_found_in_older_steam_s_flat_cache()
    {
        var files = TwoLibraries();
        files.Pictures[$@"{Cache}\1145360_library_600x900.jpg"] = CoverJpeg;
        files.Pictures[$@"{Cache}\1145360_header.jpg"] = HeaderJpeg;
        files.Pictures[$@"{Cache}\1145360_library_hero.jpg"] = HeroJpeg;
        files.Pictures[$@"{Cache}\1145360_library_hero_blur.jpg"] = HeroJpeg;

        Assert.Equal(new SteamArtFiles($@"{Cache}\1145360_library_600x900.jpg", $@"{Cache}\1145360_header.jpg", $@"{Cache}\1145360_library_hero.jpg"),
            new SteamLibrary(files).ArtFiles(1145360));

        // Its sharper cover, where it has one.
        files.Pictures[$@"{Cache}\1145360_library_600x900_2x.jpg"] = CoverJpeg;
        Assert.Equal($@"{Cache}\1145360_library_600x900_2x.jpg", new SteamLibrary(files).ArtFiles(1145360).Cover);
    }

    [Fact]
    public void Pictures_are_found_in_newer_steam_s_folder_per_game_and_the_folders_inside_it()
    {
        var files = TwoLibraries();
        files.Pictures[$@"{Cache}\367520\library_600x900.jpg"] = CoverJpeg;
        files.Pictures[$@"{Cache}\367520\dee4a8d8f03d972b4046006297488e9117df1b83\library_header.jpg"] = HeaderJpeg;
        files.Pictures[$@"{Cache}\367520\68b8a75a889a4a16503f7d8adc631add9f8009c6\library_hero.jpg"] = HeroJpeg;
        files.Pictures[$@"{Cache}\367520\68b8a75a889a4a16503f7d8adc631add9f8009c6\library_hero_blur.jpg"] = HeroJpeg;
        files.Pictures[$@"{Cache}\367520\a63b2ffb41a4b830cfd0b74fc23ed77d05c828a5\logo.png"] = IconPng;

        var art = new SteamLibrary(files).ArtFiles(367520);
        Assert.Equal($@"{Cache}\367520\library_600x900.jpg", art.Cover);
        Assert.Equal($@"{Cache}\367520\dee4a8d8f03d972b4046006297488e9117df1b83\library_header.jpg", art.Header);
        Assert.Equal($@"{Cache}\367520\68b8a75a889a4a16503f7d8adc631add9f8009c6\library_hero.jpg", art.Hero);

        // Some games' covers are library_capsule, in a folder of their own.
        files.Pictures.Remove($@"{Cache}\367520\library_600x900.jpg");
        files.Pictures[$@"{Cache}\367520\1aa20f983639643d7961f3ffcadf63bd3c49362f\library_capsule.jpg"] = CoverJpeg;
        Assert.Equal($@"{Cache}\367520\1aa20f983639643d7961f3ffcadf63bd3c49362f\library_capsule.jpg", new SteamLibrary(files).ArtFiles(367520).Cover);

        Assert.Equal(new SteamArtFiles(null, null, null), new SteamLibrary(files).ArtFiles(999));
        Assert.Equal(new SteamArtFiles(null, null, null), new SteamLibrary(new FakeFiles()).ArtFiles(367520));
    }

    [Fact]
    public void A_steam_game_has_steam_s_name_id_and_pictures()
    {
        var files = TwoLibraries();
        files.Pictures[$@"{Cache}\1145360\library_600x900.jpg"] = CoverJpeg;
        files.Pictures[$@"{Cache}\1145360\header.jpg"] = HeaderJpeg;
        files.Pictures[$@"{Cache}\1145360\library_hero.jpg"] = HeroJpeg;
        files.Descriptions[@"D:\Programs\Steam\steamapps\common\Hades\x64\Hades.exe"] = "Hades Game";

        var game = new GameFinder(files).Find(@"D:\Programs\Steam\steamapps\common\Hades\x64\Hades.exe", anyApp: false)!;
        Assert.Equal("Hades", game.Name);
        Assert.Equal(1145360, game.AppId);
        Assert.Same(CoverJpeg, game.Art.Cover);
        Assert.Same(HeaderJpeg, game.Art.Header);
        Assert.Same(HeroJpeg, game.Art.Hero);
        Assert.Null(game.Art.Icon);
        Assert.Equal(0, files.IconReads);
    }

    [Fact]
    public void Other_games_are_named_and_pictured_by_their_exe_but_only_when_they_count_as_games()
    {
        const string Exe = @"C:\Games\Celeste\Celeste.exe";
        var files = TwoLibraries();
        files.Descriptions[Exe] = "Celeste";
        files.Icons[Exe] = IconPng;
        var finder = new GameFinder(files);

        Assert.Null(finder.Find(Exe, anyApp: false));
        var game = finder.Find(Exe, anyApp: true)!;
        Assert.Equal(new Game("Celeste", null, new GameArt(null, null, null, IconPng)), game);

        // Without a description, the exe's name.
        Assert.Equal("Untitled", finder.Find(@"C:\Games\Untitled.exe", anyApp: true)!.Name);
    }

    [Fact]
    public void A_steam_game_steam_has_no_pictures_of_yet_shows_its_icon()
    {
        const string Exe = @"E:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe";
        var files = TwoLibraries();
        files.Icons[Exe] = IconPng;

        var game = new GameFinder(files).Find(Exe, anyApp: false)!;
        Assert.Equal("Hollow Knight", game.Name);
        Assert.Equal(367520, game.AppId);
        Assert.Same(IconPng, game.Art.Icon);
        Assert.False(game.Art.HasSteamArt);
    }

    [Fact]
    public void Lookups_are_kept_per_exe_and_steam_s_list_is_read_again_only_after_ten_minutes()
    {
        const string Hades = @"D:\Programs\Steam\steamapps\common\Hades\Hades.exe";
        const string Knight = @"E:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe";
        var files = TwoLibraries();
        files.Pictures[$@"{Cache}\1145360\library_600x900.jpg"] = CoverJpeg;
        var time = new FakeTime(new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero));
        var finder = new GameFinder(files, time);

        var first = finder.Find(Hades, anyApp: false);
        var (texts, pictures) = (files.TextReads, files.PictureReads);
        Assert.Equal(3, texts);

        // Switching between games reads nothing more.
        finder.Find(Knight, anyApp: false);
        finder.Find(@"C:\Windows\explorer.exe", anyApp: false);
        Assert.Same(first, finder.Find(Hades, anyApp: false));
        Assert.Equal(texts, files.TextReads);
        Assert.Equal(pictures, files.PictureReads);

        // A game installed since is found once Steam's list is old enough to read again.
        files.Texts[@"E:\SteamLibrary\steamapps\appmanifest_105600.acf"] = Manifest(105600, "Terraria", "Terraria");
        Assert.Null(finder.Find(@"E:\SteamLibrary\steamapps\common\Terraria\Terraria.exe", anyApp: false));
        time.Advance(GameFinder.LibraryLifetime);
        Assert.Equal("Terraria", finder.Find(@"E:\SteamLibrary\steamapps\common\Terraria\Terraria.exe", anyApp: false)!.Name);
        Assert.Equal(texts + 4, files.TextReads);
    }

    [Theory]
    [InlineData("cover", 1)]
    [InlineData("header", 2)]
    [InlineData("hero", 3)]
    [InlineData("HERO", 3)]
    [InlineData(null, 1)]
    public void A_picture_of_the_kind_asked_for(string? kind, byte which)
    {
        var art = new GameArt(CoverJpeg, HeaderJpeg, HeroJpeg, null);
        Assert.Equal(which, art.Picture(kind)![2]);
    }

    [Fact]
    public void Without_the_kind_asked_for_another_of_steam_s_then_the_icon()
    {
        Assert.Same(CoverJpeg, new GameArt(CoverJpeg, null, null, null).Picture("hero"));
        Assert.Same(HeaderJpeg, new GameArt(null, HeaderJpeg, HeroJpeg, null).Picture("cover"));
        var icon = new GameArt(null, null, null, IconPng);
        Assert.Same(IconPng, icon.Picture("header"));
        Assert.True(icon.IsIcon(icon.Picture("header")));
        Assert.False(new GameArt(CoverJpeg, null, null, null).IsIcon(CoverJpeg));
        Assert.Null(new GameArt(null, null, null, null).Picture("cover"));
    }

    [Fact]
    public async Task The_game_in_front_has_readings_and_pictures_until_it_has_been_gone_a_few_seconds()
    {
        const string Hades = @"D:\Programs\Steam\steamapps\common\Hades\Hades.exe";
        var files = TwoLibraries();
        files.Pictures[$@"{Cache}\1145360\library_600x900.jpg"] = CoverJpeg;
        var time = new FakeTime(new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero));
        (int, string?) inFront = (42, "Hades");
        var paths = new Dictionary<int, string> { [42] = Hades, [7] = @"C:\Program Files\Discord\Discord.exe" };
        var lookups = 0;
        using var provider = new GameArtProvider(() => inFront, null, files, id =>
        {
            lookups++;
            return paths.GetValueOrDefault(id);
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Hades", Text(store, GameArtProvider.Name));
        Assert.Equal("1145360", Text(store, GameArtProvider.AppId));
        var art = Assert.IsType<GameArt>(store.GetAttachment(GameArtProvider.Art));
        Assert.Same(CoverJpeg, art.Cover);

        // The same game a second later looks nothing up.
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, lookups);
        Assert.Same(art, store.GetAttachment(GameArtProvider.Art));

        // A quick alt-tab keeps the picture; staying away clears everything.
        inFront = (7, "Discord");
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Same(art, store.GetAttachment(GameArtProvider.Art));
        time.Advance(GameArtProvider.Linger);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Text(store, GameArtProvider.Name));
        Assert.Null(Text(store, GameArtProvider.AppId));
        Assert.Null(store.GetAttachment(GameArtProvider.Art));
        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task An_app_linked_on_the_game_layouts_page_counts_as_a_game()
    {
        const string Exe = @"C:\Games\Celeste\Celeste.exe";
        var files = TwoLibraries();
        files.Icons[Exe] = IconPng;
        string? linked = null;
        using var provider = new GameArtProvider(() => (9, "Celeste"), () => linked, files, _ => Exe);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(GameArtProvider.Art));

        linked = "celeste";
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Celeste", Text(store, GameArtProvider.Name));
        Assert.Null(Text(store, GameArtProvider.AppId));
        Assert.Same(IconPng, ((GameArt)store.GetAttachment(GameArtProvider.Art)!).Icon);
    }

    [Fact]
    public void The_biggest_icon_picture_is_picked_the_most_colourful_first()
    {
        // Three pictures: 32 pixels in 32 bits, 256 (stored as 0) in 8 bits, 256 in 32 bits.
        byte[] group =
        [
            0, 0, 1, 0, 3, 0,
            32, 32, 0, 0, 1, 0, 32, 0, 0x10, 0, 0, 0, 1, 0,
            0, 0, 0, 0, 1, 0, 8, 0, 0x20, 0, 0, 0, 2, 0,
            0, 0, 0, 0, 1, 0, 32, 0, 0x30, 0, 0, 0, 3, 0,
        ];
        var entry = ExeFile.Largest(group)!;
        Assert.Equal(3, entry[12]);
        Assert.Null(ExeFile.Largest([0, 0, 1, 0, 0, 0]));
        Assert.Null(ExeFile.Largest([0, 0]));
        // A count longer than the group stops at its end.
        Assert.Equal(1, ExeFile.Largest([0, 0, 1, 0, 9, 0, 16, 16, 0, 0, 1, 0, 32, 0, 1, 0, 0, 0, 1, 0])![12]);
    }

    [Fact]
    public void An_icon_picture_becomes_a_file_a_decoder_can_read()
    {
        byte[] entry = [48, 48, 0, 0, 1, 0, 32, 0, 4, 0, 0, 0, 7, 0];
        byte[] bitmap = [40, 0, 0, 0];

        var file = ExeFile.IconFile(entry, bitmap);
        Assert.Equal<byte>([0, 0, 1, 0, 1, 0, 48, 48, 0, 0, 1, 0, 32, 0, 4, 0, 0, 0, 22, 0, 0, 0, 40, 0, 0, 0], file);

        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];
        Assert.Equal(png, ExeFile.IconFile(entry, png));
    }

    [Fact]
    public void Windows_own_programs_have_a_path_a_description_and_an_icon()
    {
        Assert.Equal(Environment.ProcessPath, ExeFile.PathOf(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);
        Assert.Null(ExeFile.PathOf(0));

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.False(string.IsNullOrWhiteSpace(ExeFile.Description(explorer)));
        var icon = ExeFile.LargestIcon(explorer)!;
        Assert.True(icon[0] == 0x89 || (icon[2] == 1 && icon[4] == 1), "a PNG or a one-picture .ico");

        Assert.Null(ExeFile.LargestIcon(Path.Combine(Path.GetTempPath(), "not here.exe")));
        Assert.Null(ExeFile.Description(Path.Combine(Path.GetTempPath(), "not here.exe")));
    }

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    /// <summary>Steam's files and exes in memory, counting what's read.</summary>
    private sealed class FakeFiles : IGameFiles
    {
        public string? Steam { get; init; }

        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, byte[]> Pictures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Descriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, byte[]> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int TextReads { get; private set; }

        public int PictureReads { get; private set; }

        public int IconReads { get; private set; }

        private IEnumerable<string> All => Texts.Keys.Concat(Pictures.Keys);

        public string? SteamPath() => Steam;

        public string? ReadText(string path)
        {
            TextReads++;
            return Texts.GetValueOrDefault(path);
        }

        public byte[]? ReadBytes(string path)
        {
            PictureReads++;
            return Pictures.GetValueOrDefault(path);
        }

        public bool FileExists(string path) => Texts.ContainsKey(path) || Pictures.ContainsKey(path);

        public IEnumerable<string> Files(string folder, string pattern) =>
            All.Where(path => Same(Path.GetDirectoryName(path), folder) && FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path)));

        public IEnumerable<string> Folders(string folder) =>
            All.SelectMany(Parents).Where(dir => Same(Path.GetDirectoryName(dir), folder)).Distinct(StringComparer.OrdinalIgnoreCase);

        public string? Description(string exe) => Descriptions.GetValueOrDefault(exe);

        public byte[]? Icon(string exe)
        {
            IconReads++;
            return Icons.GetValueOrDefault(exe);
        }

        private static IEnumerable<string> Parents(string path)
        {
            for (var dir = Path.GetDirectoryName(path); dir is not null; dir = Path.GetDirectoryName(dir))
                yield return dir;
        }

        private static bool Same(string? a, string b) => string.Equals(a, Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

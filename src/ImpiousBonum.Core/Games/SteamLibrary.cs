using System.Globalization;

namespace ImpiousBonum.Core.Games;

/// <summary>A game Steam installed: its app id, its name in the Steam store, and the folder it's installed in.</summary>
public sealed record SteamApp(int AppId, string Name, string Folder);

/// <summary>Where Steam keeps its pictures of a game on this PC; null for any it hasn't got.</summary>
public sealed record SteamArtFiles(string? Cover, string? Header, string? Hero);

/// <summary>
/// The games Steam has installed on this PC and the pictures it keeps of them, all from Steam's own files: nothing is
/// fetched. Steam's folder comes from the registry, its libraries from <c>steamapps\libraryfolders.vdf</c>, and the games
/// in each library from its <c>appmanifest_&lt;id&gt;.acf</c> files, each of which names the game's folder under
/// <c>steamapps\common</c>.
/// </summary>
public sealed class SteamLibrary(IGameFiles files)
{
    // Steam's names for each picture, best first. Some games' covers are library_capsule, and older Steam had a sharper
    // _2x cover beside the usual one.
    private static readonly string[] CoverNames = ["library_600x900_2x.jpg", "library_600x900.jpg", "library_capsule.jpg"];
    private static readonly string[] HeaderNames = ["header.jpg", "library_header.jpg"];
    private static readonly string[] HeroNames = ["library_hero.jpg"];

    /// <summary>Steam's folder, or null if Steam isn't installed.</summary>
    public string? Folder => files.SteamPath() is { } path ? Normalize(path) : null;

    /// <summary>Every game installed in any of Steam's libraries; none without Steam.</summary>
    public IReadOnlyList<SteamApp> Apps()
    {
        if (Folder is not { } steam)
            return [];

        var apps = new List<SteamApp>();
        foreach (var library in Libraries(steam))
        {
            var steamapps = Path.Combine(library, "steamapps");
            foreach (var manifest in files.Files(steamapps, "appmanifest_*.acf"))
            {
                if (files.ReadText(manifest) is { } text && Manifest(text, library) is { } app)
                    apps.Add(app);
            }
        }
        return apps;
    }

    /// <summary>Steam's library folders: its own and the others it lists; none without Steam.</summary>
    public IReadOnlyList<string> LibraryPaths() => Folder is { } steam ? Libraries(steam) : [];

    /// <summary>
    /// The <c>localconfig.vdf</c> of whoever signs in to Steam on this PC (in <c>userdata\&lt;account id&gt;\config</c>),
    /// the most recently written if more than one person has, with when it was written; null without Steam or anyone
    /// who has signed in.
    /// </summary>
    public (string Path, DateTime Written)? LocalConfig()
    {
        if (Folder is not { } steam)
            return null;
        (string Path, DateTime Written)? newest = null;
        foreach (var user in files.Folders(Path.Combine(steam, "userdata")))
        {
            foreach (var config in files.FilesWritten(Path.Combine(user, "config"), "localconfig.vdf"))
            {
                if (newest is not { } best || config.Written > best.Written)
                    newest = config;
            }
        }
        return newest;
    }

    /// <summary>
    /// When each game was last played and how long for altogether, by app id, from a <c>localconfig.vdf</c>; games it
    /// has neither for are left out.
    /// </summary>
    internal static IReadOnlyDictionary<int, SteamPlay> PlayHistory(string localConfig)
    {
        var history = new Dictionary<int, SteamPlay>();
        var apps = Vdf.Parse(localConfig).Block("UserLocalConfigStore")?.Block("Software")?.Block("Valve")?.Block("Steam")?.Block("apps");
        if (apps is null)
            return history;
        foreach (var key in apps.Keys)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || apps.Block(key) is not { } app)
                continue;
            // Seconds since 1970; anything past the year 9999 is nonsense rather than a date.
            DateTimeOffset? last = long.TryParse(app.Text("LastPlayed"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is > 0 and < 253_402_300_800
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
            var minutes = int.TryParse(app.Text("Playtime"), NumberStyles.None, CultureInfo.InvariantCulture, out var played) ? played : 0;
            if (last is not null || minutes > 0)
                history[appId] = new SteamPlay(last, minutes);
        }
        return history;
    }

    /// <summary>The download an <c>appmanifest_&lt;id&gt;.acf</c> describes, or null if the game isn't being downloaded.</summary>
    internal static SteamDownload? Download(string acf) =>
        Vdf.Parse(acf).Block("AppState") is { } state ? SteamDownload.FromManifest(state) : null;

    /// <summary>Steam's own folder, which is always a library, and the others it lists.</summary>
    private List<string> Libraries(string steam)
    {
        var listed = files.ReadText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")) is { } vdf ? LibraryFolders(vdf) : [];
        return [.. new[] { steam }.Concat(listed).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The library folders in <c>libraryfolders.vdf</c>: each numbered entry's <c>path</c>, or in the older file, the
    /// numbered entry itself.
    /// </summary>
    internal static IReadOnlyList<string> LibraryFolders(string vdf)
    {
        var root = Vdf.Parse(vdf);
        var list = root.Block("libraryfolders");
        if (list is null)
            return [];
        return [.. list.Keys
            .Where(key => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(key => list.Block(key)?.Text("path") ?? list.Text(key))
            .OfType<string>()
            .Where(path => path.Length > 0)];
    }

    /// <summary>The game an <c>appmanifest_&lt;id&gt;.acf</c> in <paramref name="library"/> describes, or null if it's missing a part.</summary>
    internal static SteamApp? Manifest(string acf, string library)
    {
        var state = Vdf.Parse(acf).Block("AppState");
        if (state is null
            || !int.TryParse(state.Text("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            || state.Text("installdir") is not { Length: > 0 } installDir)
        {
            return null;
        }
        var name = state.Text("name") is { Length: > 0 } named ? named.Trim() : installDir;
        return new SteamApp(appId, name, Normalize(Path.Combine(library, "steamapps", "common", installDir)));
    }

    /// <summary>
    /// The game whose folder <paramref name="exe"/> is in, however deep (<c>…\common\Game\bin\x64\game.exe</c>), ignoring
    /// case; null if it's in none of them.
    /// </summary>
    public static SteamApp? Match(IEnumerable<SteamApp> apps, string exe) => InstallFolders.Match(apps, app => app.Folder, exe);

    /// <summary>The game as one of any launcher's, started through Steam.</summary>
    public static StoreGame ToStoreGame(SteamApp app)
    {
        var id = app.AppId.ToString(CultureInfo.InvariantCulture);
        return new StoreGame(GameStore.Steam, id, app.Name, app.Folder, $"steam://rungameid/{id}");
    }

    /// <summary>Where Steam keeps its pictures of the game, in its library cache; none without Steam.</summary>
    public SteamArtFiles ArtFiles(int appId)
    {
        if (Folder is not { } steam)
            return new SteamArtFiles(null, null, null);
        var cache = Path.Combine(steam, "appcache", "librarycache");
        return new SteamArtFiles(Find(cache, appId, CoverNames), Find(cache, appId, HeaderNames), Find(cache, appId, HeroNames));
    }

    /// <summary>
    /// A picture in the library cache: <c>&lt;id&gt;_header.jpg</c> in older Steam, or <c>header.jpg</c> in a folder named
    /// after the id, or in a folder inside that, in newer Steam.
    /// </summary>
    private string? Find(string cache, int appId, string[] names)
    {
        var id = appId.ToString(CultureInfo.InvariantCulture);
        var folder = Path.Combine(cache, id);
        string[]? inside = null;
        foreach (var name in names)
        {
            var older = Path.Combine(cache, $"{id}_{name}");
            if (files.FileExists(older))
                return older;
            var newer = Path.Combine(folder, name);
            if (files.FileExists(newer))
                return newer;
            inside ??= [.. files.Folders(folder)];
            foreach (var subfolder in inside)
            {
                var nested = Path.Combine(subfolder, name);
                if (files.FileExists(nested))
                    return nested;
            }
        }
        return null;
    }

    private static string Normalize(string path) => InstallFolders.Normalize(path);
}

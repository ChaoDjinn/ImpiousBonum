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
    public static SteamApp? Match(IEnumerable<SteamApp> apps, string exe)
    {
        string path;
        try
        {
            path = Normalize(exe);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        // The deepest folder wins, should one game be installed inside another's.
        return apps
            .Where(app => path.StartsWith(app.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .MaxBy(app => app.Folder.Length);
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

    /// <summary>A full path with Windows' separators and no separator at the end, so paths compare alike.</summary>
    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)));
}

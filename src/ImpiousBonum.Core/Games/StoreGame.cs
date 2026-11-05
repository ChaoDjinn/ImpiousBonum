namespace ImpiousBonum.Core.Games;

/// <summary>The launchers whose installed games are recognised without being linked on the Game layouts page.</summary>
public enum GameStore
{
    Steam,
    Epic,
    Gog,
}

/// <summary>
/// A game a launcher installed: which launcher, its id there (Steam's app id, Epic's app name, GOG's game id), its
/// name, the folder it's installed in, and how to start it.
/// </summary>
/// <param name="Launch">What to open to start it through its launcher (a <c>steam://</c> or <c>com.epicgames.launcher://</c> link), or its exe; null if there's no way known.</param>
/// <param name="Arguments">For an exe in <paramref name="Launch"/>: its command-line arguments.</param>
public sealed record StoreGame(GameStore Store, string Id, string Name, string Folder, string? Launch = null, string Arguments = "")
{
    /// <summary>The game's own exe, as its launcher records it, for its icon; null if the launcher doesn't say.</summary>
    public string? Exe { get; init; }

    /// <summary>
    /// For a game started by its exe rather than a launcher's link: the folder it starts in, since many look for their
    /// files there; null for the default.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The game's key among every launcher's, such as <c>steam:367520</c> or <c>epic:Fortnite</c>.</summary>
    public string Key => KeyOf(Store, Id);

    /// <summary>The key of the game with this id in this launcher (see <see cref="Key"/>).</summary>
    public static string KeyOf(GameStore store, string id) => $"{store.ToString().ToLowerInvariant()}:{id}";

    /// <summary>The launcher's name as people know it, for the <c>game.store</c> reading.</summary>
    public static string StoreName(GameStore store) => store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.Gog => "GOG",
        _ => store.ToString(),
    };
}

/// <summary>Finding which installed game an exe belongs to, by the folder it's in.</summary>
public static class InstallFolders
{
    /// <summary>
    /// The game whose folder <paramref name="exe"/> is in, however deep (<c>…\Game\bin\x64\game.exe</c>), ignoring case;
    /// the deepest folder wins, should one game be installed inside another's. Null if it's in none of them.
    /// </summary>
    public static T? Match<T>(IEnumerable<T> games, Func<T, string> folder, string exe) where T : class
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
        return games
            .Where(game => path.StartsWith(folder(game) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .MaxBy(game => folder(game).Length);
    }

    /// <summary>A full path with Windows' separators and no separator at the end, so paths compare alike.</summary>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// <paramref name="path"/> made ready to match exes against, or null if it's empty, can't be a path, or is a whole
    /// drive: a launcher's record that somehow names <c>C:\</c> would otherwise claim every program on it.
    /// </summary>
    public static string? GameFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var folder = Normalize(path.Trim());
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder) ?? ""), folder, StringComparison.OrdinalIgnoreCase)
                ? null
                : folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

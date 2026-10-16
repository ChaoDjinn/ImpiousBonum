namespace ImpiousBonum.Core.Games;

/// <summary>
/// Works out which game an exe belongs to, with its name and pictures: a Steam game by the folder it's installed in,
/// with Steam's name for it, its app id and Steam's pictures; any other game by the exe's own description and icon.
/// Steam's list of games is read when it's first needed and again at most every ten minutes, for games installed
/// since; each exe's answer is kept, so going back to a game reads nothing. Not thread-safe: use it from one loop.
/// </summary>
public sealed class GameFinder(IGameFiles files, TimeProvider? time = null)
{
    /// <summary>How long Steam's list of games is used before it's read again: reading it opens every game's manifest.</summary>
    public static readonly TimeSpan LibraryLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The games kept with their pictures, a few hundred KB each: enough to switch between a few without reading any again.</summary>
    private const int GamesKept = 4;

    /// <summary>The exes kept with whether Steam installed them: only paths, so many more.</summary>
    private const int ExesKept = 256;

    private readonly SteamLibrary _steam = new(files);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, SteamApp?> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Game> _games = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SteamApp> _apps = [];
    private DateTimeOffset _appsRead = DateTimeOffset.MinValue;

    /// <summary>The Steam game <paramref name="exe"/> belongs to, or null if Steam didn't install it.</summary>
    public SteamApp? SteamGame(string exe)
    {
        var now = _time.GetUtcNow();
        if (now - _appsRead >= LibraryLifetime)
        {
            _apps = _steam.Apps();
            _appsRead = now;
            // Steam may have fetched pictures since, too.
            _installed.Clear();
            _games.Clear();
        }

        if (!_installed.TryGetValue(exe, out var app))
        {
            app = SteamLibrary.Match(_apps, exe);
            if (_installed.Count >= ExesKept)
                _installed.Clear();
            _installed[exe] = app;
        }
        return app;
    }

    /// <summary>
    /// The game <paramref name="exe"/> belongs to: a Steam game; or, with <paramref name="anyApp"/>, whatever it is, named
    /// by its description and pictured by its icon. Null when Steam didn't install it and <paramref name="anyApp"/> is false.
    /// </summary>
    public Game? Find(string exe, bool anyApp)
    {
        var steam = SteamGame(exe);
        if (steam is null && !anyApp)
            return null;
        if (_games.TryGetValue(exe, out var game) && game.AppId == steam?.AppId)
            return game;

        var art = steam is null ? new GameArt(null, null, null, null) : Read(_steam.ArtFiles(steam.AppId));
        // Steam only has pictures of games it has shown in its library; until then, and for other games, the icon.
        if (!art.HasSteamArt)
            art = art with { Icon = files.Icon(exe) };
        var name = steam?.Name ?? files.Description(exe) ?? Path.GetFileNameWithoutExtension(exe);
        game = new Game(name, steam?.AppId, art);

        if (_games.Count >= GamesKept)
            _games.Clear();
        _games[exe] = game;
        return game;
    }

    private GameArt Read(SteamArtFiles art) => new(Bytes(art.Cover), Bytes(art.Header), Bytes(art.Hero), null);

    private byte[]? Bytes(string? path) => path is null ? null : files.ReadBytes(path);
}

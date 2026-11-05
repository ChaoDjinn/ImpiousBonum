using System.Globalization;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// Works out which game an exe belongs to, with its name and pictures: a Steam game by the folder it's installed in,
/// with Steam's name for it, its app id and Steam's pictures; an Epic Games or GOG game by its folder too, with that
/// launcher's name for it and the exe's icon; any other game by the exe's own description and icon. The launchers'
/// lists of games are read when first needed and again at most every ten minutes, for games installed since; each
/// exe's answer is kept, so going back to a game reads nothing. Not thread-safe: use it from one loop.
/// </summary>
public sealed class GameFinder(IGameFiles files, TimeProvider? time = null)
{
    /// <summary>How long the launchers' lists of games are used before they're read again: reading them opens every game's manifest.</summary>
    public static readonly TimeSpan LibraryLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The games kept with their pictures, a few hundred KB each: enough to switch between a few without reading any again.</summary>
    private const int GamesKept = 4;

    /// <summary>The exes kept with which launcher's game they belong to, if any: only paths, so many more.</summary>
    private const int ExesKept = 256;

    private readonly SteamLibrary _steam = new(files);
    private readonly EpicLibrary _epic = new(files);
    private readonly GogLibrary _gog = new(files);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, SteamApp?> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StoreGame?> _othersInstalled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Game> _games = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SteamApp> _apps = [];
    private IReadOnlyList<StoreGame> _others = [];
    private DateTimeOffset _appsRead = DateTimeOffset.MinValue;

    /// <summary>The Steam game <paramref name="exe"/> belongs to, or null if Steam didn't install it.</summary>
    public SteamApp? SteamGame(string exe)
    {
        Refresh();
        return Remember(_installed, exe, () => SteamLibrary.Match(_apps, exe));
    }

    /// <summary>The Epic Games or GOG game <paramref name="exe"/> belongs to, or null if neither installed it.</summary>
    public StoreGame? OtherGame(string exe)
    {
        Refresh();
        return Remember(_othersInstalled, exe, () => InstallFolders.Match(_others, game => game.Folder, exe));
    }

    /// <summary>Reads the launchers' lists of games again once they're old enough.</summary>
    private void Refresh()
    {
        var now = _time.GetUtcNow();
        if (now - _appsRead < LibraryLifetime)
            return;
        _apps = _steam.Apps();
        _others = [.. _epic.Games(), .. _gog.Games()];
        _appsRead = now;
        // Steam may have fetched pictures since, too.
        _installed.Clear();
        _othersInstalled.Clear();
        _games.Clear();
    }

    private static T? Remember<T>(Dictionary<string, T?> kept, string exe, Func<T?> match) where T : class
    {
        if (!kept.TryGetValue(exe, out var game))
        {
            game = match();
            if (kept.Count >= ExesKept)
                kept.Clear();
            kept[exe] = game;
        }
        return game;
    }

    /// <summary>
    /// The game <paramref name="exe"/> belongs to: a Steam, Epic Games or GOG game; or, with <paramref name="anyApp"/>,
    /// whatever it is, named by its description and pictured by its icon. Null when none of those launchers installed it
    /// and <paramref name="anyApp"/> is false.
    /// </summary>
    public Game? Find(string exe, bool anyApp)
    {
        var steam = SteamGame(exe);
        var other = steam is null ? OtherGame(exe) : null;
        if (steam is null && other is null && !anyApp)
            return null;
        if (_games.TryGetValue(exe, out var game) && game.AppId == steam?.AppId)
            return game;

        var art = steam is null ? new GameArt(null, null, null, null) : Read(_steam.ArtFiles(steam.AppId));
        // Steam only has pictures of games it has shown in its library; until then, and for other games, the icon.
        if (!art.HasSteamArt)
            art = art with { Icon = files.Icon(exe) };
        var name = steam?.Name ?? other?.Name ?? files.Description(exe) ?? Path.GetFileNameWithoutExtension(exe);
        GameStore? store = steam is not null ? GameStore.Steam : other?.Store;
        var storeId = steam?.AppId.ToString(CultureInfo.InvariantCulture) ?? other?.Id;
        game = new Game(name, steam?.AppId, art, store, storeId);

        if (_games.Count >= GamesKept)
            _games.Clear();
        _games[exe] = game;
        return game;
    }

    private GameArt Read(SteamArtFiles art) => new(Bytes(art.Cover), Bytes(art.Header), Bytes(art.Hero), null);

    private byte[]? Bytes(string? path) => path is null ? null : files.ReadBytes(path);
}

using System.Globalization;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>A game played lately: which game, when it was last played, how many hours altogether where that's known, and its pictures.</summary>
public sealed record RecentGame(StoreGame Game, DateTimeOffset LastPlayed, double? Hours, GameArt Art);

/// <summary>
/// The games played lately, newest first, as an attachment (a list of <see cref="RecentGame"/>) for the recent games
/// widget, and how many hours the game in front has been played. Steam's games come with Steam's own record of when each
/// was last played and for how long (its <c>localconfig.vdf</c>), read again only when Steam rewrites it. The Epic Games
/// launcher and GOG keep no such record on the PC, so their games come with the app's own, counted while they're in front
/// and kept in <c>played.json</c>. Only installed games are listed, as they're the ones a tap can start. Nothing is
/// fetched.
/// </summary>
public sealed class RecentGamesProvider : IMetricProvider
{
    /// <summary>The games, newest first (a list of <see cref="RecentGame"/>), as a <see cref="MetricStore"/> attachment; null while there are none.</summary>
    public const string Recent = "games.recent";

    public const string Last = "games.last";
    public const string Hours = "game.hours";

    /// <summary>How many games the list keeps, with their pictures.</summary>
    public const int Kept = 8;

    /// <summary>How often the app's own record is saved while a game it counts is in front, besides when the game leaves.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    private readonly IGameFiles _files;
    private readonly SteamLibrary _steam;
    private readonly EpicLibrary _epic;
    private readonly GogLibrary _gog;
    private readonly string? _playedPath;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, PlayedGame> _played;
    private readonly Dictionary<string, GameArt> _art = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<StoreGame> _installed = [];
    private DateTimeOffset _installedRead = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<int, SteamPlay> _steamPlayed = new Dictionary<int, SteamPlay>();
    private (string Path, DateTime Written)? _localConfig;
    private string? _counting;
    private DateTimeOffset _countedTo;
    private bool _unsaved;
    private bool _saveNow;
    private DateTimeOffset _saved;
    private string? _published;

    /// <param name="playedPath">Where the app's own record is kept between runs; null keeps it only while it runs.</param>
    /// <param name="files">The launchers' files; the real ones unless a test swaps them.</param>
    public RecentGamesProvider(string? playedPath = null, IGameFiles? files = null, TimeProvider? time = null)
    {
        _files = files ?? new GameFiles();
        _steam = new SteamLibrary(_files);
        _epic = new EpicLibrary(_files);
        _gog = new GogLibrary(_files);
        _playedPath = playedPath;
        _time = time ?? TimeProvider.System;
        _played = playedPath is null ? new Dictionary<string, PlayedGame>(StringComparer.OrdinalIgnoreCase) : PlayedGames.Read(playedPath);
        _saved = _time.GetUtcNow();
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(15);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Hours, "Game: hours played", "Game", MetricUnit.None, Format: "0.0"));
        store.Register(new MetricDefinition(Last, "Game: last played", "Game", MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var inFront = store.GetAttachment(GameArtProvider.Current) as Game;
        Count(inFront, now);
        Refresh(now);

        var recent = RecentGames();
        // The list is only replaced when it changes, so the widget redraws only then.
        var signature = string.Join("\n", recent.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Game.Key}|{r.Game.Name}|{r.LastPlayed.ToUnixTimeSeconds()}|{r.Hours:0.0}")));
        if (signature != _published)
        {
            _published = signature;
            store.SetAttachment(Recent, recent.Count == 0 ? null : recent);
            store.SetText(Last, recent.Count == 0 ? null : recent[0].Game.Name);
        }
        store.Set(Hours, HoursOf(inFront));

        if (_unsaved && (_saveNow || now - _saved >= SaveEvery))
            Save(now);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Counts the time an Epic Games or GOG game has been in front since the last look, and that it was played now. Time
    /// counts only when the same game was in front at the last look, and not across a long gap, such as the PC asleep.
    /// </summary>
    private void Count(Game? inFront, DateTimeOffset now)
    {
        string? key = null;
        if (inFront is { Store: { } launcher, StoreId: { } id } && launcher is GameStore.Epic or GameStore.Gog)
        {
            key = StoreGame.KeyOf(launcher, id);
            var since = now - _countedTo;
            var minutes = key == _counting && since > TimeSpan.Zero && since <= Interval * 4 ? since.TotalMinutes : 0;
            _played[key] = new PlayedGame(now, (_played.GetValueOrDefault(key)?.Minutes ?? 0) + minutes);
            _unsaved = true;
        }
        else if (_counting is not null)
        {
            // The game has gone: keep what was counted now rather than at the next save.
            _saveNow = true;
        }
        _counting = key;
        _countedTo = now;
    }

    /// <summary>Reads the launchers' lists of games again once they're old enough, and Steam's record whenever it's rewritten.</summary>
    private void Refresh(DateTimeOffset now)
    {
        if (now - _installedRead >= GameFinder.LibraryLifetime)
        {
            _installed = [.. _steam.Apps().Select(SteamLibrary.ToStoreGame), .. _epic.Games(), .. _gog.Games()];
            _installedRead = now;
            // Steam may have pictures of more games since.
            _art.Clear();
            _published = null;
        }

        var config = _steam.LocalConfig();
        if (!config.Equals(_localConfig))
        {
            _localConfig = config;
            _steamPlayed = config is { } found && _files.ReadText(found.Path) is { } text
                ? SteamLibrary.PlayHistory(text)
                : new Dictionary<int, SteamPlay>();
        }
    }

    /// <summary>The installed games played lately, newest first, with their pictures; pictures of games no longer on the list are let go.</summary>
    private List<RecentGame> RecentGames()
    {
        var played = new List<(StoreGame Game, DateTimeOffset Last, double? Hours)>();
        foreach (var game in _installed)
        {
            if (game.Store == GameStore.Steam)
            {
                if (int.TryParse(game.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
                    && _steamPlayed.TryGetValue(appId, out var play) && play.LastPlayed is { } last)
                {
                    played.Add((game, last, play.Minutes / 60.0));
                }
            }
            else if (_played.TryGetValue(game.Key, out var own))
            {
                played.Add((game, own.Last, own.Minutes / 60.0));
            }
        }

        var recent = played
            .OrderByDescending(p => p.Last)
            .Take(Kept)
            .Select(p => new RecentGame(p.Game, p.Last, p.Hours, ArtOf(p.Game)))
            .ToList();
        foreach (var key in _art.Keys.Except(recent.Select(r => r.Game.Key), StringComparer.OrdinalIgnoreCase).ToList())
            _art.Remove(key);
        return recent;
    }

    /// <summary>A game's pictures: Steam's cover and banner from its cache, or the exe's icon. Kept while the game is on the list.</summary>
    private GameArt ArtOf(StoreGame game)
    {
        if (_art.TryGetValue(game.Key, out var art))
            return art;
        art = new GameArt(null, null, null, null);
        if (game.Store == GameStore.Steam && int.TryParse(game.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            // Not the hero picture: it's the biggest, and the widget doesn't show it.
            var files = _steam.ArtFiles(appId);
            art = new GameArt(Bytes(files.Cover), Bytes(files.Header), null, null);
        }
        if (!art.HasSteamArt && game.Exe is { } exe)
            art = art with { Icon = _files.Icon(exe) };
        _art[game.Key] = art;
        return art;
    }

    private byte[]? Bytes(string? path) => path is null ? null : _files.ReadBytes(path);

    /// <summary>The hours the game has been played: Steam's count for a Steam game, the app's own for an Epic Games or GOG game.</summary>
    private double? HoursOf(Game? game)
    {
        if (game is { Store: GameStore.Steam, AppId: { } appId })
            return _steamPlayed.TryGetValue(appId, out var play) && play.Minutes > 0 ? play.Minutes / 60.0 : null;
        if (game is { Store: { } launcher, StoreId: { } id } && launcher is GameStore.Epic or GameStore.Gog)
            return _played.TryGetValue(StoreGame.KeyOf(launcher, id), out var own) ? own.Minutes / 60.0 : null;
        return null;
    }

    private void Save(DateTimeOffset now)
    {
        if (_playedPath is not null)
            PlayedGames.Write(_playedPath, _played);
        _saved = now;
        _unsaved = false;
        _saveNow = false;
    }

    public void Dispose()
    {
        if (_unsaved)
            Save(_time.GetUtcNow());
    }
}

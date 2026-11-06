using System.Globalization;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>A game played lately: which game, when it was last played, how many hours altogether where that's known, and its pictures.</summary>
public sealed record RecentGame(StoreGame Game, DateTimeOffset LastPlayed, double? Hours, GameArt Art);

/// <summary>
/// The games played lately, newest first, as an attachment (a list of <see cref="RecentGame"/>) for the recent games
/// widget, and how many hours the game in front has been played. Steam's games come with Steam's own record of when each
/// was last played and for how long (its <c>localconfig.vdf</c>), read again only when Steam rewrites it, which it adds
/// a session to only as the game closes; until then the app counts the time it sees a Steam game in front on top. For
/// Epic Games and GOG games the app keeps its own record, counted while they're in front and kept in <c>played.json</c>,
/// and where GOG Galaxy is installed, Galaxy's record comes first: its minutes for GOG games and, through its
/// integration, Epic's, when GOG's were last played, and its cached covers. Only installed games are listed, as they're
/// the ones a tap can start. Nothing is fetched.
/// </summary>
public sealed class RecentGamesProvider : IMetricProvider
{
    /// <summary>The games, newest first (a list of <see cref="RecentGame"/>), as a <see cref="MetricStore"/> attachment; null while there are none.</summary>
    public const string Recent = "games.recent";

    public const string Last = "games.last";
    public const string Hours = "game.hours";

    /// <summary>
    /// How many games the list keeps from each launcher, with their pictures: as many as a widget shows, so one showing
    /// only Epic's games has them even when the last few played were Steam's.
    /// </summary>
    public const int Kept = 8;

    /// <summary>How often the app's own record is saved while a game it counts is in front, besides when the game leaves.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    /// <summary>How often GOG Galaxy's record is read again at most, as Galaxy writes to its database often while it runs.</summary>
    public static readonly TimeSpan GalaxyEvery = TimeSpan.FromMinutes(1);

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

    /// <summary>What GOG Galaxy said about the installed Epic Games and GOG games when last read, by Galaxy's key.</summary>
    private IReadOnlyDictionary<string, GalaxyGame> _galaxy = new Dictionary<string, GalaxyGame>();
    private DateTime? _galaxyWritten;
    private DateTimeOffset _galaxyRead = DateTimeOffset.MinValue;
    private string? _galaxyPictures;

    /// <summary>
    /// For Steam games: the minutes the app has seen each in front that Steam hasn't yet added to its record, and when it
    /// was last in front. Kept only while the app runs, as Steam soon has its own count.
    /// </summary>
    private readonly Dictionary<int, PlayedGame> _steamSession = [];
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
    /// Counts the time the game in front has been in front since the last look, and that it was played now: an Epic
    /// Games or GOG game's in the app's own record, a Steam game's until Steam counts it. Time counts only when the same
    /// game was in front at the last look, and not across a long gap, such as the PC asleep.
    /// </summary>
    private void Count(Game? inFront, DateTimeOffset now)
    {
        string? key = null;
        double Minutes()
        {
            var since = now - _countedTo;
            return key == _counting && since > TimeSpan.Zero && since <= Interval * 4 ? since.TotalMinutes : 0;
        }

        if (inFront is { Store: { } launcher, StoreId: { } id } && launcher is GameStore.Epic or GameStore.Gog)
        {
            key = StoreGame.KeyOf(launcher, id);
            var minutes = Minutes();
            _played[key] = _played.TryGetValue(key, out var own) ? own with { Last = now, Minutes = own.Minutes + minutes } : new PlayedGame(now, minutes);
            _unsaved = true;
        }
        else if (inFront is { Store: GameStore.Steam, AppId: { } appId })
        {
            key = StoreGame.KeyOf(GameStore.Steam, appId.ToString(CultureInfo.InvariantCulture));
            _steamSession[appId] = new PlayedGame(now, (_steamSession.GetValueOrDefault(appId)?.Minutes ?? 0) + Minutes());
        }
        // A game has gone: keep what was counted now rather than at the next save.
        if (_counting is not null && _counting != key)
            _saveNow = true;
        _counting = key;
        _countedTo = now;
    }

    /// <summary>
    /// Reads the launchers' lists of games again once they're old enough, Steam's record whenever it's rewritten, and GOG
    /// Galaxy's when it changes, at most every <see cref="GalaxyEvery"/>.
    /// </summary>
    private void Refresh(DateTimeOffset now)
    {
        if (now - _installedRead >= GameFinder.LibraryLifetime)
        {
            _installed = [.. _steam.Apps().Select(SteamLibrary.ToStoreGame), .. _epic.Games(), .. _gog.Games()];
            _installedRead = now;
            // Steam may have pictures of more games since, and Galaxy may know more of them.
            _art.Clear();
            _published = null;
            _galaxyWritten = null;
            _galaxyRead = DateTimeOffset.MinValue;
        }
        RefreshGalaxy(now);

        var config = _steam.LocalConfig();
        if (!config.Equals(_localConfig))
        {
            if (config is not { } found)
            {
                _localConfig = null;
                _steamPlayed = new Dictionary<int, SteamPlay>();
            }
            else if (_files.ReadText(found.Path) is { } text)
            {
                var rewritten = _localConfig is not null;
                _localConfig = config;
                var before = _steamPlayed;
                _steamPlayed = SteamLibrary.PlayHistory(text);
                // Steam has counted the minutes it added since it last wrote: the app's own count stops at what Steam
                // hasn't. The first read adds nothing, as it's all from before.
                foreach (var (appId, session) in rewritten ? _steamSession.ToList() : [])
                {
                    var added = (_steamPlayed.GetValueOrDefault(appId)?.Minutes ?? 0) - (before.GetValueOrDefault(appId)?.Minutes ?? 0);
                    if (added <= 0)
                        continue;
                    if (session.Minutes > added)
                        _steamSession[appId] = session with { Minutes = session.Minutes - added };
                    else
                        _steamSession.Remove(appId);
                }
            }
            // Else Steam's writing it: it's read next time, and what was read before stands till then.
        }
    }

    /// <summary>
    /// Reads GOG Galaxy's record of the installed Epic Games and GOG games when it has changed: its minutes, which the
    /// app's own record is brought into line with (see <see cref="PlayedGame.WithGalaxy"/>), when GOG's were last played,
    /// and its pictures. One that can't be read just now is read next time, and what was read before stands.
    /// </summary>
    private void RefreshGalaxy(DateTimeOffset now)
    {
        var written = _files.GalaxyWritten();
        if (written == _galaxyWritten || now - _galaxyRead < GalaxyEvery)
            return;
        var games = _installed.Where(g => g.Store is GameStore.Epic or GameStore.Gog).ToList();
        if (_files.GalaxyGames([.. games.Select(g => GogGalaxy.KeyOf(g.Store, g.Id))]) is not { } galaxy)
            return;
        _galaxyWritten = written;
        _galaxyRead = now;

        var pictures = string.Join("\n", galaxy.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Key}|{g.Value.Cover}|{g.Value.Background}|{g.Value.Icon}"));
        if (pictures != _galaxyPictures)
        {
            _galaxyPictures = pictures;
            _art.Clear();
            _published = null;
        }
        _galaxy = galaxy;
        foreach (var game in games)
        {
            if (_played.TryGetValue(game.Key, out var own) && galaxy.GetValueOrDefault(GogGalaxy.KeyOf(game.Store, game.Id))?.Minutes is { } minutes
                && own.WithGalaxy(minutes) is var counted && counted != own)
            {
                _played[game.Key] = counted;
                _unsaved = true;
            }
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
                if (int.TryParse(game.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && SteamPlayed(appId) is { } play)
                    played.Add((game, play.Last, play.Minutes / 60.0));
            }
            else if (StorePlayed(game.Store, game.Id) is { Last: { } last } play)
            {
                played.Add((game, last, play.Minutes / 60.0));
            }
        }

        var recent = played
            .GroupBy(p => p.Game.Store)
            .SelectMany(launcher => launcher.OrderByDescending(p => p.Last).Take(Kept))
            .OrderByDescending(p => p.Last)
            .Select(p => new RecentGame(p.Game, p.Last, p.Hours, ArtOf(p.Game)))
            .ToList();
        foreach (var key in _art.Keys.Except(recent.Select(r => r.Game.Key), StringComparer.OrdinalIgnoreCase).ToList())
            _art.Remove(key);
        return recent;
    }

    /// <summary>
    /// A game's pictures: Steam's cover and banner from its cache; for an Epic Games or GOG game, the cover and background
    /// GOG Galaxy has cached; else the exe's icon, or Galaxy's. Kept while the game is on the list.
    /// </summary>
    private GameArt ArtOf(StoreGame game)
    {
        if (_art.TryGetValue(game.Key, out var art))
            return art;
        art = new GameArt(null, null, null, null);
        var galaxy = game.Store is GameStore.Epic or GameStore.Gog ? _galaxy.GetValueOrDefault(GogGalaxy.KeyOf(game.Store, game.Id)) : null;
        if (game.Store == GameStore.Steam && int.TryParse(game.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            // Not the hero picture: it's the biggest, and the widget doesn't show it.
            var files = _steam.ArtFiles(appId);
            art = new GameArt(Bytes(files.Cover), Bytes(files.Header), null, null);
        }
        else if (galaxy is not null)
        {
            art = new GameArt(Bytes(galaxy.Cover), Bytes(galaxy.Background), null, null);
        }
        if (!art.HasSteamArt)
            art = art with { Icon = (game.Exe is { } exe ? _files.Icon(exe) : null) ?? Bytes(galaxy?.Icon) };
        _art[game.Key] = art;
        return art;
    }

    private byte[]? Bytes(string? path) => path is null ? null : _files.ReadBytes(path);

    /// <summary>
    /// When a Steam game was last played and its minutes: Steam's record, with the time the app has seen it in front that
    /// Steam hasn't counted yet. Null if it has neither.
    /// </summary>
    private PlayedGame? SteamPlayed(int appId)
    {
        var last = Later(_steamPlayed.GetValueOrDefault(appId)?.LastPlayed, _steamSession.GetValueOrDefault(appId)?.Last);
        return last is { } when ? new PlayedGame(when, SteamMinutes(appId)) : null;
    }

    /// <summary>
    /// When an Epic Games or GOG game was last played (null if nothing says) and its minutes: GOG Galaxy's count where it
    /// has one, with the time the app has seen that Galaxy hasn't counted, else the app's own. Null if neither knows it.
    /// </summary>
    private (DateTimeOffset? Last, double Minutes)? StorePlayed(GameStore store, string id)
    {
        var own = _played.GetValueOrDefault(StoreGame.KeyOf(store, id));
        var galaxy = _galaxy.GetValueOrDefault(GogGalaxy.KeyOf(store, id));
        if (own is null && galaxy is null)
            return null;
        var minutes = galaxy?.Minutes is { } counted ? own?.WithGalaxy(counted).Total ?? counted : own?.Total ?? 0;
        return (Later(own?.Last, galaxy?.LastPlayed), minutes);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null || a > b ? a : b;

    /// <summary>A Steam game's minutes: Steam's count, and the time the app has seen it in front that Steam hasn't counted yet.</summary>
    private double SteamMinutes(int appId) =>
        (_steamPlayed.GetValueOrDefault(appId)?.Minutes ?? 0) + (_steamSession.GetValueOrDefault(appId)?.Minutes ?? 0);

    /// <summary>
    /// The hours the game has been played: Steam's count for a Steam game, GOG Galaxy's or the app's own for an Epic Games
    /// or GOG game, each with the time the app has seen that they haven't counted yet.
    /// </summary>
    private double? HoursOf(Game? game)
    {
        if (game is { Store: GameStore.Steam, AppId: { } appId })
            return SteamMinutes(appId) is > 0 and var minutes ? minutes / 60.0 : null;
        if (game is { Store: { } launcher, StoreId: { } id } && launcher is GameStore.Epic or GameStore.Gog)
            return StorePlayed(launcher, id) is { } play ? play.Minutes / 60.0 : null;
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

using System.Globalization;
using System.Net;
using System.Text.Json;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Your Steam friends who are online and what they're playing, as an attachment (a list of <see cref="SteamFriend"/>)
/// for the friends widget and as counts, and your achievements in the Steam game in front, from Steam's Web API with a
/// key of your own (<see cref="SteamWeb"/>). Off without a key and a profile, and nothing is fetched then. Who's online
/// is checked every minute and the friends list itself every 10 minutes; achievements when a Steam game comes to the
/// front and every 2 minutes while it's there. <c>steam.status</c> says how it's going, in words.
/// </summary>
/// <param name="account">Whose friends and achievements, or null when it's off. Read on every sample, so changes apply quickly.</param>
/// <param name="get">Gets a Web API reply; Steam's unless a test swaps it.</param>
public sealed class SteamFriendsProvider(
    Func<SteamAccount?> account,
    Func<string, CancellationToken, Task<(HttpStatusCode Status, string Body)>>? get = null,
    TimeProvider? time = null) : IMetricProvider
{
    /// <summary>The friends online, those playing first (a list of <see cref="SteamFriend"/>), as a <see cref="MetricStore"/> attachment; null while there are none.</summary>
    public const string Friends = "steam.friends.list";

    public const string Online = "steam.friends.online";
    public const string Playing = "steam.friends.playing";
    public const string Status = "steam.status";
    public const string Achievements = "game.achievements";
    public const string AchievementsPercent = "game.achievements.percent";

    public static readonly TimeSpan FriendsEvery = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan FriendListEvery = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AchievementsEvery = TimeSpan.FromMinutes(2);

    /// <summary>After Steam can't be reached, or says something needs doing, how long until trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly Func<string, CancellationToken, Task<(HttpStatusCode Status, string Body)>> _get = get ?? SteamWeb.GetAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private SteamAccount? _account;
    private string? _steamId;
    private IReadOnlyList<string>? _friendIds;
    private string? _friendsProblem;
    private DateTimeOffset _friendIdsAt;
    private IReadOnlyList<SteamFriend>? _friends;
    private DateTimeOffset _friendsAt;
    private int? _app;
    private AchievementCount? _achievements;
    private string? _achievementsProblem;
    private DateTimeOffset _achievementsAt;
    private string? _problem;
    private DateTimeOffset _nextTry;

    public TimeSpan Interval => TimeSpan.FromSeconds(15);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Online, "Steam: friends online", "Steam", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Playing, "Steam: friends playing a game", "Steam", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Status, "Steam: how fetching your friends and achievements is going", "Steam", MetricUnit.Text));
        store.Register(new MetricDefinition(Achievements, "Game: achievements unlocked, e.g. 34 / 50", "Game", MetricUnit.Text));
        store.Register(new MetricDefinition(AchievementsPercent, "Game: achievements unlocked", "Game", MetricUnit.Percent, 100, "0"));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var wanted = account();
        if (wanted != _account)
        {
            // Another key or profile, or turned on or off: start afresh.
            _account = wanted;
            _steamId = null;
            _friendIds = null;
            _friendsProblem = null;
            _friends = null;
            _problem = null;
            _friendIdsAt = DateTimeOffset.MinValue;
            _friendsAt = DateTimeOffset.MinValue;
            _nextTry = DateTimeOffset.MinValue;
            ForgetAchievements(_app);
        }
        if (wanted is null)
        {
            Publish(store, null, null, null);
            return;
        }

        var now = _time.GetUtcNow();
        // Another game in front, or none: its achievements are wanted now, and the last game's are gone.
        var app = (store.GetAttachment(GameArtProvider.Current) as Game)?.AppId;
        if (app != _app)
            ForgetAchievements(app);

        if (now >= _nextTry)
        {
            try
            {
                var steamId = _steamId ??= wanted.IsId ? wanted.Profile : await Resolve(wanted, cancellationToken).ConfigureAwait(false);
                if (now - _friendIdsAt >= FriendListEvery)
                {
                    (_friendIds, _friendsProblem) = await FriendIds(wanted.Key, steamId, cancellationToken).ConfigureAwait(false);
                    _friendIdsAt = now;
                    _friendsAt = DateTimeOffset.MinValue;
                }
                if (now - _friendsAt >= FriendsEvery)
                {
                    _friends = await OnlineFriends(wanted.Key, _friendIds ?? [], cancellationToken).ConfigureAwait(false);
                    _friendsAt = now;
                }
                if (app is { } appId && now - _achievementsAt >= AchievementsEvery)
                {
                    (_achievements, _achievementsProblem) = await AchievementsIn(wanted.Key, steamId, appId, cancellationToken).ConfigureAwait(false);
                    _achievementsAt = now;
                }
                _problem = null;
            }
            catch (SteamWebException ex)
            {
                _problem = ex.Message;
                _friends = null;
                _nextTry = now + RetryAfter;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                _problem = "Couldn't reach Steam. It'll try again in a few minutes.";
                _nextTry = now + RetryAfter;
            }
        }
        Publish(store, _friends, _achievements, StatusText());
    }

    /// <summary>Lets the achievements go, so <paramref name="app"/>'s are fetched afresh.</summary>
    private void ForgetAchievements(int? app)
    {
        _app = app;
        _achievements = null;
        _achievementsProblem = null;
        _achievementsAt = DateTimeOffset.MinValue;
    }

    /// <summary>Writes the friends, the counts, the achievements and the status, or clears them all with nulls.</summary>
    public static void Publish(MetricStore store, IReadOnlyList<SteamFriend>? friends, AchievementCount? achievements, string? status)
    {
        store.SetAttachment(Friends, friends is { Count: > 0 } ? friends : null);
        store.Set(Online, friends?.Count);
        store.Set(Playing, friends?.Count(f => f.Game is not null));
        store.SetText(Status, status);
        store.SetText(Achievements, achievements is null ? null : string.Create(CultureInfo.CurrentCulture, $"{achievements.Unlocked} / {achievements.Total}"));
        store.Set(AchievementsPercent, achievements?.Percent);
    }

    /// <summary>"3 friends online, 1 playing", or what's wrong.</summary>
    private string? StatusText()
    {
        if (_problem is not null)
            return _problem;
        if (_friends is null)
            return "Connecting to Steam";
        var status = _friendsProblem ?? FriendsOnline(_friends);
        return _achievementsProblem is null ? status : $"{status} {_achievementsProblem}";
    }

    internal static string FriendsOnline(IReadOnlyList<SteamFriend> friends)
    {
        if (friends.Count == 0)
            return "No friends online.";
        var playing = friends.Count(f => f.Game is not null);
        var online = friends.Count == 1 ? "1 friend online" : $"{friends.Count} friends online";
        return playing == 0 ? $"{online}." : $"{online}, {playing} playing.";
    }

    private async Task<string> Resolve(SteamAccount wanted, CancellationToken cancellationToken)
    {
        var (status, body) = await _get(SteamWeb.ResolveUrl(wanted), cancellationToken).ConfigureAwait(false);
        Succeeded(status, body);
        return SteamWeb.ResolvedId(body, wanted.Profile);
    }

    private async Task<(IReadOnlyList<string> Ids, string? Problem)> FriendIds(string key, string steamId, CancellationToken cancellationToken)
    {
        var (status, body) = await _get(SteamWeb.FriendsUrl(key, steamId), cancellationToken).ConfigureAwait(false);
        SteamWeb.ThrowIfKeyRefused(status, body);
        // Steam refuses a private friends list without saying more.
        if (status == HttpStatusCode.Unauthorized)
            return ([], "Your friends list is private, so Steam doesn't share it. Make it public in your profile's privacy settings to see who's online.");
        Succeeded(status, body);
        return (SteamWeb.FriendIds(body), null);
    }

    /// <summary>Friends' summaries, a hundred at a time, down to those online: playing first, then by name.</summary>
    private async Task<IReadOnlyList<SteamFriend>> OnlineFriends(string key, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var online = new List<SteamFriend>();
        foreach (var some in ids.Chunk(SteamWeb.SummariesPerRequest))
        {
            var (status, body) = await _get(SteamWeb.SummariesUrl(key, some), cancellationToken).ConfigureAwait(false);
            Succeeded(status, body);
            online.AddRange(SteamWeb.OnlineFriends(body));
        }
        return [.. online
            .OrderBy(f => f.Game is null)
            .ThenBy(f => f.Presence)
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private async Task<(AchievementCount? Count, string? Problem)> AchievementsIn(string key, string steamId, int appId, CancellationToken cancellationToken)
    {
        var (status, body) = await _get(SteamWeb.AchievementsUrl(key, steamId, appId), cancellationToken).ConfigureAwait(false);
        SteamWeb.ThrowIfKeyRefused(status, body);
        try
        {
            return (SteamWeb.Achievements(status, body), null);
        }
        catch (SteamWebException ex)
        {
            // Private game details: the friends still work.
            return (null, ex.Message);
        }
    }

    /// <summary>Throws for a reply that isn't a success: <see cref="SteamWebException"/> for a refused key, else <see cref="HttpRequestException"/>.</summary>
    private static void Succeeded(HttpStatusCode status, string body)
    {
        SteamWeb.ThrowIfKeyRefused(status, body);
        if (status != HttpStatusCode.OK)
            throw new HttpRequestException($"Steam answered {(int)status}.", null, status);
    }

    public void Dispose()
    {
    }
}

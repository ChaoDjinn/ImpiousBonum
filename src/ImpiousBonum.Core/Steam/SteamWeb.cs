using System.Net;
using System.Text.Json;

namespace ImpiousBonum.Core.Steam;

/// <summary>Whose Steam friends and achievements to fetch, and the Web API key to fetch them with.</summary>
/// <param name="Profile">A 17-digit SteamID64, or the custom name at the end of a profile's address, to look the id up by.</param>
public sealed record SteamAccount(string Key, string Profile)
{
    /// <summary>Whether <see cref="Profile"/> is the id itself rather than a name to look it up by.</summary>
    public bool IsId => SteamWeb.IsSteamId(Profile);

    /// <summary>Never the key: records print their values, and this one could end up in a log.</summary>
    public override string ToString() => $"SteamAccount {{ Profile = {Profile} }}";
}

/// <summary>Whether an online friend is around: offline friends aren't listed at all.</summary>
public enum SteamPresence
{
    Online,
    Away,
    Busy,
}

/// <summary>A friend who's online: their name, whether they're around, and the game they're playing, if any.</summary>
public sealed record SteamFriend(string Name, SteamPresence Presence, string? Game);

/// <summary>How many of a game's achievements have been unlocked, of how many it has.</summary>
public sealed record AchievementCount(int Unlocked, int Total)
{
    public double Percent => Total > 0 ? 100.0 * Unlocked / Total : 0;
}

/// <summary>A reply from Steam that means something needs doing, in words for the person using the app.</summary>
public sealed class SteamWebException(string message) : Exception(message);

/// <summary>
/// Steam's Web API, with the key of whoever uses the app: their profile's id from its custom name, their friends, which
/// of them are online and playing what, and their achievements in a game. Steam only gives what a profile's privacy
/// settings make public, whoever's key asks. The key goes only to Steam, in each request's address, so addresses are
/// never logged.
/// </summary>
public static class SteamWeb
{
    /// <summary>Where Steam gives you a key of your own.</summary>
    public const string KeyPage = "https://steamcommunity.com/dev/apikey";

    /// <summary>How many friends' summaries one request can ask for.</summary>
    public const int SummariesPerRequest = 100;

    private const string Api = "https://api.steampowered.com";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The account to fetch for, from what was typed on the Steam page; null without both a key and a profile.</summary>
    public static SteamAccount? Account(string? key, string? profile) =>
        key?.Trim() is { Length: > 0 } trimmed && Profile(profile) is { } found ? new SteamAccount(trimmed, found) : null;

    /// <summary>
    /// A profile as typed, down to what identifies it: the id from <c>steamcommunity.com/profiles/7656…</c> or on its own,
    /// or the name from <c>steamcommunity.com/id/name</c> or on its own. Null if it's empty or can't be either.
    /// </summary>
    public static string? Profile(string? typed)
    {
        var text = typed?.Trim() ?? "";
        foreach (var marker in (string[])["/profiles/", "/id/"])
        {
            var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                text = text[(at + marker.Length)..];
                break;
            }
        }
        var end = text.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
            text = text[..end];
        return text.Length is > 0 and <= 64 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? text : null;
    }

    /// <summary>Whether <paramref name="profile"/> is a SteamID64: 17 digits, starting 7656.</summary>
    public static bool IsSteamId(string profile) =>
        profile.Length == 17 && profile.StartsWith("7656", StringComparison.Ordinal) && profile.All(char.IsAsciiDigit);

    internal static string ResolveUrl(SteamAccount account) =>
        $"{Api}/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(account.Key)}&vanityurl={Uri.EscapeDataString(account.Profile)}";

    internal static string FriendsUrl(string key, string steamId) =>
        $"{Api}/ISteamUser/GetFriendList/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&relationship=friend";

    internal static string SummariesUrl(string key, IEnumerable<string> steamIds) =>
        $"{Api}/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(key)}&steamids={string.Join(',', steamIds)}";

    internal static string AchievementsUrl(string key, string steamId, int appId) =>
        $"{Api}/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(key)}&steamid={steamId}&appid={appId}";

    /// <summary>A reply's status and body, whatever the status. Throws <see cref="HttpRequestException"/> and the like when Steam can't be reached.</summary>
    public static async Task<(HttpStatusCode Status, string Body)> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Throws <see cref="SteamWebException"/> for a reply that says the key isn't accepted: 403 with a page saying to
    /// check the <c>key=</c> parameter. Steam sends the same page with 401 when the key is fine but what it asked for is
    /// private, such as a friends list, so a 401 is left for the caller.
    /// </summary>
    internal static void ThrowIfKeyRefused(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.Forbidden && body.Contains("key=", StringComparison.OrdinalIgnoreCase))
            throw new SteamWebException("Steam didn't accept the key. Check it on Steam's Web API key page, then paste it again.");
    }

    /// <summary>The id a custom profile name belongs to. Throws <see cref="SteamWebException"/> when there's no such profile.</summary>
    internal static string ResolvedId(string json, string name)
    {
        var response = Root(json).GetProperty("response");
        if (response.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.Number && success.GetInt32() == 1
            && Text(response, "steamid") is { } id && IsSteamId(id))
        {
            return id;
        }
        throw new SteamWebException($"Steam has no profile called {name}. Check the address of your profile, or use its 17-digit ID.");
    }

    /// <summary>Your friends' ids.</summary>
    internal static IReadOnlyList<string> FriendIds(string json) =>
        Root(json).TryGetProperty("friendslist", out var list) && list.TryGetProperty("friends", out var friends) && friends.ValueKind == JsonValueKind.Array
            ? [.. friends.EnumerateArray().Select(f => Text(f, "steamid")).OfType<string>().Where(IsSteamId)]
            : [];

    /// <summary>
    /// The friends among some players' summaries who are online, with what they're playing. A friend whose profile is
    /// private shows as offline unless they've said they're looking to play or trade, so isn't listed.
    /// </summary>
    internal static IReadOnlyList<SteamFriend> OnlineFriends(string json)
    {
        if (!Root(json).TryGetProperty("response", out var response) || !response.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return [];
        var online = new List<SteamFriend>();
        foreach (var player in players.EnumerateArray())
        {
            var state = player.TryGetProperty("personastate", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
            SteamPresence? presence = state switch
            {
                2 => SteamPresence.Busy,
                3 or 4 => SteamPresence.Away,
                1 or 5 or 6 => SteamPresence.Online,
                _ => null,
            };
            if (presence is { } around && Text(player, "personaname") is { } name)
                online.Add(new SteamFriend(name.Trim(), around, Text(player, "gameextrainfo")?.Trim()));
        }
        return online;
    }

    /// <summary>
    /// How many of a game's achievements are unlocked, from a reply to <see cref="AchievementsUrl"/>; null for a game
    /// without any. Throws <see cref="SteamWebException"/> when the profile's game details are private.
    /// </summary>
    internal static AchievementCount? Achievements(HttpStatusCode status, string body)
    {
        JsonElement stats;
        try
        {
            stats = Root(body).GetProperty("playerstats");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A game without stats can get a bare error page; only a reply that says it worked has to make sense.
            if (status == HttpStatusCode.OK)
                throw new JsonException("Not a reply about achievements.", ex);
            return null;
        }
        if (Text(stats, "error") is { } error && error.Contains("not public", StringComparison.OrdinalIgnoreCase))
            throw new SteamWebException("Your profile's game details are private, so Steam doesn't share your achievements. Make them public in your profile's privacy settings.");
        if (!stats.TryGetProperty("achievements", out var achievements) || achievements.ValueKind != JsonValueKind.Array)
            return null;
        var total = 0;
        var unlocked = 0;
        foreach (var achievement in achievements.EnumerateArray())
        {
            total++;
            if (achievement.TryGetProperty("achieved", out var achieved) && achieved.ValueKind == JsonValueKind.Number && achieved.GetInt32() == 1)
                unlocked++;
        }
        return total == 0 ? null : new AchievementCount(unlocked, total);
    }

    private static JsonElement Root(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

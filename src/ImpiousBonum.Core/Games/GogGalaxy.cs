using System.Globalization;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// What GOG Galaxy keeps on the PC about a game, by its release key (<c>gog_1207658924</c>, <c>epic_Fortnite</c>):
/// minutes played, when it was last played, achievements unlocked, and the pictures it has cached. Galaxy keeps these
/// for its own games and, through its integrations, for other launchers' (play time from the Epic Games launcher, say).
/// </summary>
/// <param name="Minutes">Minutes played altogether, as Galaxy counts them; null if it has no count.</param>
/// <param name="LastPlayed">When it was last played through Galaxy; null if Galaxy doesn't say (it doesn't for Epic's games).</param>
/// <param name="Achievements">Achievements unlocked of how many there are; null for a game without any, or none known.</param>
/// <param name="Installed">Whether Galaxy itself has the game installed, so it can start it.</param>
public sealed record GalaxyGame(int? Minutes, DateTimeOffset? LastPlayed, AchievementCount? Achievements, bool Installed)
{
    /// <summary>The tall cover Galaxy has cached (a WebP file), or null.</summary>
    public string? Cover { get; init; }

    /// <summary>The wide background picture Galaxy has cached (a WebP file), or null.</summary>
    public string? Background { get; init; }

    /// <summary>The square icon Galaxy has cached (a WebP file), or null.</summary>
    public string? Icon { get; init; }
}

/// <summary>
/// Reads GOG Galaxy 2.0's own database (<c>galaxy-2.0.db</c>, SQLite) and its picture cache, read-only, with the copy of
/// SQLite that comes with Windows. Galaxy's layout isn't documented, so anything unexpected counts as nothing known.
/// </summary>
public static class GogGalaxy
{
    /// <summary>Galaxy's folder of shared data: <c>C:\ProgramData\GOG.com\Galaxy</c>.</summary>
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GOG.com", "Galaxy");

    /// <summary>Galaxy's database, whether or not it's there.</summary>
    public static string DatabasePath => Path.Combine(DataFolder, "storage", "galaxy-2.0.db");

    /// <summary>A game's key in Galaxy: <c>gog_</c> and its GOG id, <c>epic_</c> and its Epic app name, <c>steam_</c> and its app id.</summary>
    public static string KeyOf(GameStore store, string id) => $"{store.ToString().ToLowerInvariant()}_{id}";

    /// <summary>What the database at <paramref name="database"/> says about the games with these keys; those it doesn't know are left out.</summary>
    /// <exception cref="IOException">The database can't be read just now, or isn't laid out as expected.</exception>
    internal static Dictionary<string, GalaxyGame> Read(string database, IReadOnlyCollection<string> keys)
    {
        var games = new Dictionary<string, GalaxyGame>(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
            return games;
        using var db = WinSqlite.Open(database);
        var list = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var marks = string.Join(",", list.Select(_ => "?"));

        var minutes = db.Query($"select releaseKey, minutesInGame from GameTimes where releaseKey in ({marks})", list,
            row => (Key: row.Text(0) ?? "", Minutes: row.Long(1)));
        var last = db.Query($"select gameReleaseKey, lastPlayedDate from LastPlayedDates where gameReleaseKey in ({marks})", list,
            row => (Key: row.Text(0) ?? "", When: Utc(row.Text(1))));
        var achievements = db.Query(
            $"select gameReleaseKey, count(*), sum(isUnlocked) from UserAchievements where gameReleaseKey in ({marks}) group by gameReleaseKey", list,
            row => (Key: row.Text(0) ?? "", Total: row.Long(1) ?? 0, Unlocked: row.Long(2) ?? 0));
        var installed = db.Query("select productId from InstalledBaseProducts", [], row => row.Long(0)?.ToString(CultureInfo.InvariantCulture))
            .OfType<string>().Select(id => KeyOf(GameStore.Gog, id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pictures = db.Query(
            "select w.releaseKey, w.userId, t.type, r.filename from WebCache w join WebCacheResources r on r.webCacheId = w.id " +
            $"join WebCacheResourceTypes t on t.id = r.webCacheResourceTypeId where w.releaseKey in ({marks})", list,
            row => (Key: row.Text(0) ?? "", User: row.Text(1), Type: row.Text(2), File: row.Text(3)));

        foreach (var key in list)
        {
            var played = minutes.FirstOrDefault(m => Same(m.Key, key)).Minutes;
            var when = last.FirstOrDefault(l => Same(l.Key, key)).When;
            var unlocked = achievements.FirstOrDefault(a => Same(a.Key, key));
            var game = new GalaxyGame(
                played is { } m and >= 0 and <= int.MaxValue ? (int)m : null,
                when,
                unlocked.Total > 0 ? new AchievementCount((int)Math.Min(unlocked.Unlocked, unlocked.Total), (int)unlocked.Total) : null,
                installed.Contains(key))
            {
                Cover = Picture(pictures, key, "verticalCover"),
                Background = Picture(pictures, key, "background"),
                Icon = Picture(pictures, key, "squareIcon"),
            };
            if (game.Minutes is not null || game.LastPlayed is not null || game.Achievements is not null || game.Installed
                || game.Cover is not null || game.Background is not null || game.Icon is not null)
            {
                games[key] = game;
            }
        }
        return games;
    }

    /// <summary>
    /// Where Galaxy keeps a cached picture: <c>webcache\&lt;user&gt;\&lt;platform&gt;\&lt;id&gt;\&lt;file&gt;</c> in its data
    /// folder, where the platform and id are the two halves of the release key. Null if Galaxy has none of that type.
    /// </summary>
    private static string? Picture(IEnumerable<(string Key, string? User, string? Type, string? File)> pictures, string key, string type)
    {
        var split = key.IndexOf('_', StringComparison.Ordinal);
        foreach (var picture in pictures)
        {
            if (split <= 0 || !Same(picture.Key, key) || picture.Type != type || picture.User is not { Length: > 0 } user || picture.File is not { Length: > 0 } file
                || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || user.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                continue;
            }
            return Path.Combine(DataFolder, "webcache", user, key[..split], key[(split + 1)..], file);
        }
        return null;
    }

    /// <summary>Galaxy's dates, written as <c>2026-11-06 16:24:31</c> in UTC.</summary>
    internal static DateTimeOffset? Utc(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
            ? new DateTimeOffset(when, TimeSpan.Zero)
            : null;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

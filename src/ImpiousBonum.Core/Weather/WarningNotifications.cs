namespace ImpiousBonum.Core.Weather;

/// <summary>A notification about warnings: its title, its text, and the page clicking it opens.</summary>
public sealed record WarningNotice(string Title, string Message, string? Link);

/// <summary>
/// Decides when to notify about warnings: each one once a run, when it's new or gets more serious. An update the NWS
/// gives an id of its own is the same warning. Several at once make one notification listing the most serious few and
/// how many more, since Windows shows a tray app's notifications one at a time.
/// </summary>
public sealed class WarningNotifications
{
    /// <summary>Most warnings one notification lists; the rest are counted.</summary>
    public const int MostListed = 3;

    /// <summary>Windows cuts a notification's text off at 255 characters and its title at 63.</summary>
    private const int MostText = 255;

    /// <summary>Each warning told about, with the level it was at then.</summary>
    private readonly Dictionary<string, WarningLevel> _told = new(StringComparer.Ordinal);

    /// <summary>
    /// What to notify about, given the warnings now (most serious first), or null for nothing new. Every warning given is
    /// remembered as told, the counted ones too, so none comes up again unless it gets more serious.
    /// </summary>
    public WarningNotice? Next(IReadOnlyList<WeatherWarning>? warnings)
    {
        if (warnings is null)
            return null;
        var news = warnings.Where(IsNews).ToList();
        foreach (var warning in warnings)
        {
            foreach (var id in warning.Replaces.Prepend(warning.Id))
                _told[id] = _told.TryGetValue(id, out var level) && level > warning.Level ? level : warning.Level;
        }
        return news.Count == 0 ? null : Describe(news);
    }

    /// <summary>A warning not told about yet, as it is or as an earlier version, or one more serious than when it was.</summary>
    private bool IsNews(WeatherWarning warning) =>
        warning.Replaces.Prepend(warning.Id).All(id => !_told.TryGetValue(id, out var level) || level < warning.Level);

    /// <summary>One warning's headline, or the first few with "and 2 more"; clicking opens the most serious one's page.</summary>
    internal static WarningNotice Describe(IReadOnlyList<WeatherWarning> news)
    {
        if (news.Count == 1)
            return new WarningNotice("Weather warning", Shorten(news[0].Headline, MostText), news[0].Link);

        var listed = news.Take(MostListed).ToList();
        var more = news.Count - listed.Count;
        var tail = more > 0 ? $"\nand {more} more" : "";
        // Each listed headline gets an even share of what's left, newlines included.
        var each = (MostText - tail.Length) / listed.Count - 1;
        var message = string.Join("\n", listed.Select(w => Shorten(w.Headline, each))) + tail;
        return new WarningNotice($"{news.Count} weather warnings", message, news[0].Link);
    }

    private static string Shorten(string text, int most) => text.Length <= most ? text : text[..(most - 1)].TrimEnd() + "…";
}

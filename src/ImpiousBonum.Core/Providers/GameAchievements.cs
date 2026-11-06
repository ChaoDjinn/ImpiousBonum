using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The game in front's achievements, <c>game.achievements</c> (<em>34 / 50</em>) and <c>game.achievements.percent</c>,
/// which come from whichever launcher's game it is: Steam's Web API for a Steam game (<see cref="SteamFriendsProvider"/>),
/// GOG Galaxy's own record for a GOG game (<see cref="GalaxyAchievementsProvider"/>). Each writes them only while its
/// launcher's game is in front, and clears them once when it goes.
/// </summary>
public static class GameAchievements
{
    public const string Text = "game.achievements";
    public const string Percent = "game.achievements.percent";

    public static void Register(MetricStore store)
    {
        store.Register(new MetricDefinition(Text, "Game: achievements unlocked, e.g. 34 / 50", "Game", MetricUnit.Text));
        store.Register(new MetricDefinition(Percent, "Game: achievements unlocked", "Game", MetricUnit.Percent, 100, "0"));
    }

    /// <summary>Writes how many are unlocked of how many, or clears both readings with null.</summary>
    public static void Publish(MetricStore store, AchievementCount? achievements)
    {
        store.SetText(Text, achievements is null ? null : string.Create(CultureInfo.CurrentCulture, $"{achievements.Unlocked} / {achievements.Total}"));
        store.Set(Percent, achievements?.Percent);
    }
}

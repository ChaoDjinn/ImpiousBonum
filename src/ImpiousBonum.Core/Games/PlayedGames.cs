using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// When a game was last played, and for how many minutes, as the app has seen it in front. Where GOG Galaxy has a count
/// of its minutes too, <see cref="Galaxy"/> is that count as the app last saw it, and <see cref="Minutes"/> only the time
/// the app has seen that Galaxy hadn't counted then (see <see cref="WithGalaxy"/>).
/// </summary>
public sealed record PlayedGame(DateTimeOffset Last, double Minutes)
{
    /// <summary>GOG Galaxy's count of the game's minutes when the app last looked; null if it has never seen one.</summary>
    public double? Galaxy { get; init; }

    /// <summary>The minutes altogether: Galaxy's count, where there is one, and the time the app has seen on top.</summary>
    [JsonIgnore]
    public double Total => (Galaxy ?? 0) + Minutes;

    /// <summary>
    /// The record after seeing Galaxy's count of the game's minutes. Galaxy adds a session as the game closes when it
    /// started the game (and the Epic Games launcher's sessions when it syncs), which the app has seen too, so what Galaxy
    /// adds comes off the app's own minutes: what's left is the time only the app saw, such as a game started from its
    /// exe. The first time, all the app's minutes are taken to be in Galaxy's count already, but for any more.
    /// </summary>
    public PlayedGame WithGalaxy(double galaxy) => this with
    {
        Minutes = Math.Max(0, Minutes - (Galaxy is { } before ? Math.Max(0, galaxy - before) : galaxy)),
        Galaxy = galaxy,
    };
}

/// <summary>
/// The app's own record of when games from launchers that keep none on the PC (the Epic Games launcher and GOG) were last
/// played and for how long: what <c>played.json</c> in the settings folder keeps between runs, by
/// <see cref="StoreGame.Key"/>. It counts only the time the app has seen each game in front.
/// </summary>
public static class PlayedGames
{
    public const string FileName = "played.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Games Galaxy has no count for go without one.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The record kept at <paramref name="path"/>; empty when there's none or it can't be read. Never throws.</summary>
    public static Dictionary<string, PlayedGame> Read(string path)
    {
        var played = new Dictionary<string, PlayedGame>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path) || JsonSerializer.Deserialize<Dictionary<string, PlayedGame?>>(File.ReadAllText(path), Options) is not { } read)
                return played;
            // A hand edit gone wrong counts as that game not played, rather than a negative time.
            foreach (var (key, game) in read)
            {
                if (game is { Minutes: >= 0 } && double.IsFinite(game.Minutes))
                    played[key] = game.Galaxy is { } galaxy && !(galaxy >= 0 && double.IsFinite(galaxy)) ? game with { Galaxy = null } : game;
            }
            return played;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return played;
        }
    }

    /// <summary>
    /// Writes to a temporary file and moves it into place, so a run that stops halfway never leaves half a file. A file
    /// that can't be written is left for the next save.
    /// </summary>
    public static void Write(string path, IReadOnlyDictionary<string, PlayedGame> played)
    {
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(played, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

using System.Text.Json;

namespace ImpiousBonum.Core.Games;

/// <summary>When a game was last played, and for how many minutes altogether, as the app has seen it in front.</summary>
public sealed record PlayedGame(DateTimeOffset Last, double Minutes);

/// <summary>
/// The app's own record of when games from launchers that keep none on the PC (the Epic Games launcher and GOG) were last
/// played and for how long: what <c>played.json</c> in the settings folder keeps between runs, by
/// <see cref="StoreGame.Key"/>. It counts only the time the app has seen each game in front.
/// </summary>
public static class PlayedGames
{
    public const string FileName = "played.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

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
                    played[key] = game;
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

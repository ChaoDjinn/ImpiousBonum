using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// The games Battle.net has installed, from the uninstall entry Windows keeps for each (its uninstaller is Battle.net's,
/// <c>Blizzard Uninstaller.exe --uid=…</c>): the game's name, the folder it's in and its exe. A game is started through
/// Battle.net (<c>Battle.net.exe --exec="launch Fen"</c>), as its shortcuts do, so Battle.net signs in and updates it
/// first; one whose launch code isn't known is started by its exe, which hands over to Battle.net. When each was last
/// played comes from Battle.net's settings (<c>%AppData%\Battle.net\Battle.net.config</c>). Nothing is fetched.
/// </summary>
/// <param name="configPath">Battle.net's settings file; the usual place unless a test gives another.</param>
public sealed class BattleNetLibrary(IGameFiles files, string? configPath = null)
{
    /// <summary>Where Windows lists installed programs, in the registry's 32-bit view, as Battle.net's installer is 32-bit.</summary>
    public const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Battle.net's own uid in its uninstall entry: the launcher, not a game.</summary>
    private const string Launcher = "battle.net";

    /// <summary>
    /// The launch codes Battle.net's <c>--exec="launch …"</c> takes, by the uid its uninstaller and settings know a game
    /// by. They're Blizzard's own product codes, as shortcuts and other game libraries use them.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> LaunchCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["wow"] = "WoW",
        ["wow_classic"] = "WoWC",
        ["d3"] = "D3",
        ["diablo3"] = "D3",
        ["fenris"] = "Fen",
        ["osi"] = "OSI",
        ["s1"] = "S1",
        ["s2"] = "S2",
        ["w3"] = "W3",
        ["hs_beta"] = "WTCG",
        ["heroes"] = "Hero",
        ["prometheus"] = "Pro",
        ["odin"] = "ODIN",
        ["viper"] = "VIPR",
        ["zeus"] = "ZEUS",
        ["fore"] = "FORE",
        ["auks"] = "AUKS",
        ["lazarus"] = "LAZR",
        ["anbs"] = "ANBS",
        ["rtro"] = "RTRO",
        ["aqua"] = "AQUA",
        ["lbra"] = "LBRA",
    };

    private string ConfigPath => configPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", "Battle.net.config");

    /// <summary>Every game Battle.net has installed whose exe is still there; none without Battle.net.</summary>
    public IReadOnlyList<StoreGame> Games()
    {
        var entries = files.LocalMachineEntries(RegistryPath).Where(e => Uid(e.Text("UninstallString")) is not null).ToList();
        if (entries.Count == 0)
            return [];
        // Battle.net's own entry says where it is, to start the games through it.
        var client = entries.FirstOrDefault(e => Uid(e.Text("UninstallString")) == Launcher)?.Text("InstallLocation") is { } location
            ? Path.Combine(location, "Battle.net.exe")
            : null;
        if (client is not null && !files.FileExists(client))
            client = null;
        return [.. entries
            .Select(entry => Entry(entry, client))
            .OfType<StoreGame>()
            // An old beta can leave an entry behind for a folder that's gone, or share another's.
            .Where(game => game.Exe is not null && files.FileExists(game.Exe))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>When each game was last played, by its uid, from Battle.net's settings; none without them.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastPlayed() =>
        files.ReadText(ConfigPath) is { } json ? LastPlayed(json) : new Dictionary<string, DateTimeOffset>();

    /// <summary>When Battle.net's settings were last written, to read them again only then; null without them.</summary>
    public DateTime? Written() =>
        Path.GetDirectoryName(ConfigPath) is { } folder && files.FilesWritten(folder, Path.GetFileName(ConfigPath)) is [var config, ..] ? config.Written : null;

    /// <summary>The game an uninstall entry describes, or null if it isn't Battle.net's, is the launcher itself, or has no folder.</summary>
    internal static StoreGame? Entry(RegistryEntry entry, string? client)
    {
        if (Uid(entry.Text("UninstallString")) is not { } uid || uid == Launcher || InstallFolders.GameFolder(entry.Text("InstallLocation")) is not { } folder)
            return null;
        var name = (entry.Text("DisplayName") ?? Path.GetFileName(folder)).Trim();
        var exe = ExeOf(entry.Text("DisplayIcon"));
        var game = new StoreGame(GameStore.BattleNet, uid, name, folder, exe) { Exe = exe, WorkingDirectory = exe is null ? null : Path.GetDirectoryName(exe) };
        return client is not null && LaunchCodes.TryGetValue(uid, out var code)
            ? game with { Launch = client, Arguments = $"--exec=\"launch {code}\"", WorkingDirectory = Path.GetDirectoryName(client) }
            : game;
    }

    /// <summary>
    /// The uid in Battle.net's uninstall command (<c>"…\Blizzard Uninstaller.exe" --lang=enUS --uid=fenris
    /// --displayname="Diablo IV"</c>), in lower case; null for any other program's.
    /// </summary>
    internal static string? Uid(string? uninstall)
    {
        if (uninstall is null || !uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase))
            return null;
        var start = uninstall.IndexOf("--uid=", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        start += "--uid=".Length;
        var end = uninstall.IndexOf(' ', start);
        var uid = (end < 0 ? uninstall[start..] : uninstall[start..end]).Trim('"').Trim();
        return uid.Length > 0 ? uid.ToLowerInvariant() : null;
    }

    /// <summary>The exe in a <c>DisplayIcon</c>, which can be quoted or end with an icon's number (<c>"…\WoW.exe",0</c>).</summary>
    internal static string? ExeOf(string? icon)
    {
        if (icon is null)
            return null;
        var path = icon.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > path.LastIndexOf('\\') && int.TryParse(path[(comma + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            path = path[..comma];
        path = path.Trim().Trim('"');
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// When each game was last played, from Battle.net's settings: <c>"Games": { "fenris": { "LastPlayed": "1690651752" } }</c>,
    /// in seconds since 1970.
    /// </summary>
    internal static IReadOnlyDictionary<string, DateTimeOffset> LastPlayed(string json)
    {
        var played = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("Games", out var games) || games.ValueKind != JsonValueKind.Object)
                return played;
            foreach (var game in games.EnumerateObject())
            {
                if (game.Value.ValueKind == JsonValueKind.Object && game.Value.TryGetProperty("LastPlayed", out var last)
                    && (last.ValueKind == JsonValueKind.String && long.TryParse(last.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                        || last.ValueKind == JsonValueKind.Number && last.TryGetInt64(out seconds))
                    && seconds > 0)
                {
                    played[game.Name] = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
            }
        }
        catch (JsonException)
        {
        }
        return played;
    }
}

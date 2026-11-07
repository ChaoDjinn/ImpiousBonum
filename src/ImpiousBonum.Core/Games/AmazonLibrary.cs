using System.Globalization;
using System.Text.Json;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Games;

/// <summary>A game the Amazon Games app has installed: its id there, its name and its folder.</summary>
public sealed record AmazonInstall(string Id, string Title, string Folder);

/// <summary>The Amazon Games app's installed games, and when each was last played, by its id.</summary>
public sealed record AmazonData(IReadOnlyList<AmazonInstall> Installed, IReadOnlyDictionary<string, DateTimeOffset> LastPlayed);

/// <summary>
/// The games the Amazon Games app has installed, from its own databases on this PC (in
/// <c>%LocalAppData%\Amazon Games\Data\Games\Sql</c>), read-only: each game's name and folder, and when it was last
/// played. A game is started through the app (<c>amazon-games://play/…</c>), as its shortcuts do. Nothing is fetched.
/// </summary>
public sealed class AmazonLibrary(IGameFiles files)
{
    /// <summary>Where the app keeps its databases.</summary>
    public static string DataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Amazon Games", "Data", "Games", "Sql");

    /// <summary>Every game the app has installed; none without it, or while its databases can't be read.</summary>
    public IReadOnlyList<StoreGame> Games() =>
        files.Amazon()?.Installed.Select(ToGame).OfType<StoreGame>().ToList() ?? [];

    /// <summary>When each game was last played, by its id; none without the app.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastPlayed() =>
        files.Amazon()?.LastPlayed ?? new Dictionary<string, DateTimeOffset>();

    /// <summary>A game started through the app, with its exe (for its icon) from the <c>fuel.json</c> Amazon puts in its folder.</summary>
    internal StoreGame? ToGame(AmazonInstall install)
    {
        if (InstallFolders.GameFolder(install.Folder) is not { } folder)
            return null;
        return new StoreGame(GameStore.Amazon, install.Id, install.Title.Trim(), folder, $"amazon-games://play/{Uri.EscapeDataString(install.Id)}")
        {
            Exe = files.ReadText(Path.Combine(folder, "fuel.json")) is { } fuel ? Exe(folder, fuel) : null,
        };
    }

    /// <summary>The exe <c>fuel.json</c> starts (<c>{ "Main": { "Command": "Game.exe" } }</c>), in the game's folder; null if it names none.</summary>
    internal static string? Exe(string folder, string fuel)
    {
        try
        {
            using var document = JsonDocument.Parse(fuel, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Main", out var main)
                && main.ValueKind == JsonValueKind.Object && main.TryGetProperty("Command", out var command)
                && command.ValueKind == JsonValueKind.String && command.GetString() is { Length: > 0 } exe)
            {
                return InstallFolders.Normalize(Path.Combine(folder, exe));
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
        return null;
    }

    /// <summary>
    /// Reads the app's databases: its installed games (<c>GameInstallInfo.sqlite</c>) and when they were last played
    /// (<c>GameUserInteractionsInfo.sqlite</c>). Throws <see cref="IOException"/> if they can't be read.
    /// </summary>
    internal static AmazonData Read(string folder)
    {
        var installed = new List<AmazonInstall>();
        var installs = Path.Combine(folder, "GameInstallInfo.sqlite");
        if (File.Exists(installs))
        {
            using var db = WinSqlite.Open(installs);
            installed = db.Query("SELECT Id, ProductTitle, InstallDirectory FROM DbSet WHERE Installed = 1", [],
                row => row.Text(0) is { } id && row.Text(2) is { } path ? new AmazonInstall(id, row.Text(1) ?? Path.GetFileName(path.TrimEnd('\\', '/')), path) : null)
                .OfType<AmazonInstall>().ToList();
        }
        var played = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var interactions = Path.Combine(folder, "GameUserInteractionsInfo.sqlite");
        if (File.Exists(interactions))
        {
            using var db = WinSqlite.Open(interactions);
            foreach (var (id, last) in db.Query("SELECT Id, LastPlayed FROM DbSet WHERE LastPlayed IS NOT NULL", [], row => (row.Text(0), row.Text(1))))
            {
                if (id is not null && When(last) is { } when)
                    played[id] = when;
            }
        }
        return new AmazonData(installed, played);
    }

    /// <summary>A time as the app writes it (<c>2026-11-05 21:14:03.123</c>, UTC), or null.</summary>
    internal static DateTimeOffset? When(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when) ? when : null;
}

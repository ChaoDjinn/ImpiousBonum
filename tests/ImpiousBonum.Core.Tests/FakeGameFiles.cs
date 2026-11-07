using System.IO.Enumeration;
using ImpiousBonum.Core.Games;

namespace ImpiousBonum.Core.Tests;

/// <summary>The launchers' files, the registry and exes in memory, counting what's read, for the game tests.</summary>
internal sealed class FakeGameFiles : IGameFiles
{
    public string? Steam { get; init; }

    public string? Epic { get; set; }

    public List<RegistryEntry> Gog { get; } = [];

    public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Pictures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Descriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files that are listed but can't be read just now, as when Steam is writing one.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What Steam's registry says about whether it's working on each game; a game not in here, nothing.</summary>
    public Dictionary<int, bool> Updating { get; } = [];

    /// <summary>When each file was last written; a file not in here was written at <see cref="DateTime.MinValue"/>.</summary>
    public Dictionary<string, DateTime> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What GOG Galaxy's database says, by its key (gog_…, epic_…); null for no Galaxy.</summary>
    public Dictionary<string, GalaxyGame>? Galaxy { get; set; }

    /// <summary>When Galaxy's database last changed.</summary>
    public DateTime GalaxyChanged { get; set; } = new(2026, 11, 5, 21, 0, 0, DateTimeKind.Utc);

    /// <summary>Galaxy's database can't be read just now, as while Galaxy writes it.</summary>
    public bool GalaxyBusy { get; set; }

    /// <summary>Where GalaxyClient.exe is; null for no Galaxy.</summary>
    public string? GalaxyExe { get; set; }

    public int GalaxyReads { get; private set; }

    public int TextReads { get; private set; }

    public int PictureReads { get; private set; }

    public int IconReads { get; private set; }

    private IEnumerable<string> All => Texts.Keys.Concat(Pictures.Keys);

    public string? SteamPath() => Steam;

    public string? EpicDataPath() => Epic;

    /// <summary>Windows' uninstall entries, where Battle.net's games are.</summary>
    public List<RegistryEntry> Uninstall { get; } = [];

    public IReadOnlyList<RegistryEntry> LocalMachineEntries(string path) =>
        path == GogLibrary.RegistryPath ? Gog : path == BattleNetLibrary.RegistryPath ? Uninstall : [];

    public string? ReadText(string path)
    {
        TextReads++;
        return Unreadable.Contains(path) ? null : Texts.GetValueOrDefault(path);
    }

    public byte[]? ReadBytes(string path)
    {
        PictureReads++;
        return Pictures.GetValueOrDefault(path);
    }

    public bool FileExists(string path) => Texts.ContainsKey(path) || Pictures.ContainsKey(path);

    public IEnumerable<string> Files(string folder, string pattern) =>
        All.Where(path => Same(Path.GetDirectoryName(path), folder) && FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path)));

    public IReadOnlyList<(string Path, DateTime Written)> FilesWritten(string folder, string pattern) =>
        [.. Files(folder, pattern).Select(path => (Path: path, Written: Written.GetValueOrDefault(path)))];

    public IEnumerable<string> Folders(string folder) =>
        All.SelectMany(Parents).Where(dir => Same(Path.GetDirectoryName(dir), folder)).Distinct(StringComparer.OrdinalIgnoreCase);

    public bool? SteamUpdating(int appId) => Updating.TryGetValue(appId, out var updating) ? updating : null;

    public string? Description(string exe) => Descriptions.GetValueOrDefault(exe);

    public DateTime? GalaxyWritten() => Galaxy is null ? null : GalaxyChanged;

    public IReadOnlyDictionary<string, GalaxyGame>? GalaxyGames(IReadOnlyCollection<string> keys)
    {
        GalaxyReads++;
        if (GalaxyBusy)
            return null;
        return Galaxy is null
            ? new Dictionary<string, GalaxyGame>()
            : Galaxy.Where(g => keys.Contains(g.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(g => g.Key, g => g.Value, StringComparer.OrdinalIgnoreCase);
    }

    public string? GalaxyClient() => GalaxyExe;

    public byte[]? Icon(string exe)
    {
        IconReads++;
        return Icons.GetValueOrDefault(exe);
    }

    private static IEnumerable<string> Parents(string path)
    {
        for (var dir = Path.GetDirectoryName(path); dir is not null; dir = Path.GetDirectoryName(dir))
            yield return dir;
    }

    private static bool Same(string? a, string b) => string.Equals(a, Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}

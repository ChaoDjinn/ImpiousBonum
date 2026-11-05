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

    /// <summary>When each file was last written; a file not in here was written at <see cref="DateTime.MinValue"/>.</summary>
    public Dictionary<string, DateTime> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TextReads { get; private set; }

    public int PictureReads { get; private set; }

    public int IconReads { get; private set; }

    private IEnumerable<string> All => Texts.Keys.Concat(Pictures.Keys);

    public string? SteamPath() => Steam;

    public string? EpicDataPath() => Epic;

    public IReadOnlyList<RegistryEntry> LocalMachineEntries(string path) => path == GogLibrary.RegistryPath ? Gog : [];

    public string? ReadText(string path)
    {
        TextReads++;
        return Texts.GetValueOrDefault(path);
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

    public string? Description(string exe) => Descriptions.GetValueOrDefault(exe);

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

using System.Security;
using ImpiousBonum.Core.Native;
using Microsoft.Win32;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// Everything finding a game reads from the PC: where Steam is, Steam's own files, and the game's exe. Each read gives
/// null or nothing, rather than throwing, when what it wants isn't there. <see cref="GameFiles"/> reads the real ones;
/// tests swap in their own.
/// </summary>
public interface IGameFiles
{
    /// <summary>Steam's folder, from the registry (HKCU\Software\Valve\Steam, SteamPath), or null if Steam isn't installed.</summary>
    string? SteamPath();

    /// <summary>A text file's contents, or null.</summary>
    string? ReadText(string path);

    /// <summary>A picture's bytes, or null if it's missing, can't be read or is far too big for one.</summary>
    byte[]? ReadBytes(string path);

    bool FileExists(string path);

    /// <summary>The files in a folder that match a pattern such as <c>appmanifest_*.acf</c>; none if the folder isn't there.</summary>
    IEnumerable<string> Files(string folder, string pattern);

    /// <summary>The folders in a folder; none if it isn't there.</summary>
    IEnumerable<string> Folders(string folder);

    /// <summary>
    /// The files in a folder that match a pattern, each with when it was last written (UTC), from one look at the folder;
    /// none if the folder isn't there.
    /// </summary>
    IReadOnlyList<(string Path, DateTime Written)> FilesWritten(string folder, string pattern);

    /// <summary>
    /// Whether Steam says it's working on a game's download or update now: its <c>Updating</c> value under
    /// HKCU\Software\Valve\Steam\Apps\&lt;id&gt;, which it keeps up to the moment. Null if Steam doesn't say.
    /// </summary>
    bool? SteamUpdating(int appId);

    /// <summary>The exe's description, as Explorer's Properties → Details shows it ("Hollow Knight"), or null.</summary>
    string? Description(string exe);

    /// <summary>The exe's own icon at its largest size, as image bytes, or null if it has none.</summary>
    byte[]? Icon(string exe);

    /// <summary>
    /// The Epic Games launcher's data folder, which holds a manifest for each game it installed
    /// (<c>Manifests\*.item</c>): where the launcher's registry entry says, else where it usually is
    /// (<c>C:\ProgramData\Epic\EpicGamesLauncher\Data</c>). Null if the launcher isn't installed.
    /// </summary>
    string? EpicDataPath();

    /// <summary>
    /// Each key under <c>HKEY_LOCAL_MACHINE\<paramref name="path"/></c> in the 32-bit view (where 32-bit installers such
    /// as GOG Galaxy's write, under <c>WOW6432Node</c>), with its text values; none if the key isn't there.
    /// </summary>
    IReadOnlyList<RegistryEntry> LocalMachineEntries(string path);
}

/// <summary>A registry key's name and its text values, matched ignoring case as Windows does.</summary>
public sealed record RegistryEntry(string Key, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The value's text, or null if it's missing or empty.</summary>
    public string? Text(string name) => Values.GetValueOrDefault(name) is { Length: > 0 } text ? text : null;
}

/// <summary>The real registry and files, for <see cref="IGameFiles"/>.</summary>
public sealed class GameFiles : IGameFiles
{
    /// <summary>Steam's pictures are a few hundred KB at most; anything this big isn't one.</summary>
    private const long MaxPictureBytes = 16 << 20;

    public string? SteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") is string { Length: > 0 } path ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public byte[]? ReadBytes(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxPictureBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> Files(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    public IEnumerable<string> Folders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    public IReadOnlyList<(string Path, DateTime Written)> FilesWritten(string folder, string pattern)
    {
        try
        {
            var directory = new DirectoryInfo(folder);
            // Listing a folder gives each file's times along with its name, so this costs no more than the list.
            return directory.Exists ? [.. directory.EnumerateFiles(pattern).Select(file => (Path: file.FullName, Written: file.LastWriteTimeUtc))] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return [];
        }
    }

    public bool? SteamUpdating(int appId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}");
            return key?.GetValue("Updating") is int updating ? updating != 0 : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    public string? Description(string exe) => ExeFile.Description(exe);

    public byte[]? Icon(string exe) => ExeFile.LargestIcon(exe);

    public string? EpicDataPath()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = root.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
            if (key?.GetValue("AppDataPath") is string { Length: > 0 } path)
                return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Look where it usually is instead.
        }
        var usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data");
        return Directory.Exists(usual) ? usual : null;
    }

    public IReadOnlyList<RegistryEntry> LocalMachineEntries(string path)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var parent = root.OpenSubKey(path);
            if (parent is null)
                return [];
            var entries = new List<RegistryEntry>();
            foreach (var name in parent.GetSubKeyNames())
            {
                using var key = parent.OpenSubKey(name);
                if (key is null)
                    continue;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is string text)
                        values[valueName] = text;
                }
                entries.Add(new RegistryEntry(name, values));
            }
            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }
}

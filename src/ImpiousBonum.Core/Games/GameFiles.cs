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

    /// <summary>The exe's description, as Explorer's Properties → Details shows it ("Hollow Knight"), or null.</summary>
    string? Description(string exe);

    /// <summary>The exe's own icon at its largest size, as image bytes, or null if it has none.</summary>
    byte[]? Icon(string exe);
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

    public string? Description(string exe) => ExeFile.Description(exe);

    public byte[]? Icon(string exe) => ExeFile.LargestIcon(exe);
}

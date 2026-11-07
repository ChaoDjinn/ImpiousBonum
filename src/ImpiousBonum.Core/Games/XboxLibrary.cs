using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// The PC games the Xbox app (Game Pass and the Microsoft Store) has installed, from the folders it installs them in
/// (<c>D:\XboxGames\&lt;game&gt;</c>, as each drive's <c>.GamingRoot</c> file names it) and each game's own description
/// beside it (<c>Content\MicrosoftGame.config</c>): its name, its exe, and the package that starts it. A game is started
/// as the Start menu starts it (<c>shell:AppsFolder\&lt;package&gt;!&lt;app&gt;</c>). Nothing is fetched.
/// </summary>
public sealed class XboxLibrary(IGameFiles files)
{
    /// <summary>Every game the Xbox app has installed; none without it.</summary>
    public IReadOnlyList<StoreGame> Games()
    {
        var games = new List<StoreGame>();
        foreach (var root in files.GamingRoots())
        {
            foreach (var folder in files.Folders(root))
            {
                if (files.ReadText(Path.Combine(folder, "Content", "MicrosoftGame.config")) is { } config
                    && Game(folder, config, files.ReadText(Path.Combine(folder, "Content", "appxmanifest.xml"))) is { } game)
                {
                    games.Add(game);
                }
            }
        }
        return games;
    }

    /// <summary>
    /// The game a folder's <c>MicrosoftGame.config</c> describes, or null if it isn't one. The app that starts it is the
    /// package's (<paramref name="manifest"/>, its <c>appxmanifest.xml</c>), which can differ from the config's: Dune:
    /// Spice Wars' is Launcher, not Game.
    /// </summary>
    internal static StoreGame? Game(string folder, string config, string? manifest = null)
    {
        XElement root;
        try
        {
            root = XElement.Parse(config);
        }
        catch (XmlException)
        {
            return null;
        }
        var identity = Child(root, "Identity");
        var name = (string?)identity?.Attribute("Name");
        var publisher = (string?)identity?.Attribute("Publisher");
        var executable = Child(Child(root, "ExecutableList"), "Executable");
        if (name is null || publisher is null || InstallFolders.GameFolder(folder) is not { } gameFolder)
            return null;
        // A name that's a resource reference (ms-resource:…) can't be read here: the folder's name will do.
        var shown = (string?)Child(root, "ShellVisuals")?.Attribute("DefaultDisplayName") is { Length: > 0 } display && !display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? display
            : Path.GetFileName(gameFolder);
        var app = ManifestApp(manifest) ?? (string?)executable?.Attribute("Id") ?? "Game";
        var exe = (string?)executable?.Attribute("Name") is { Length: > 0 } relative ? Combine(gameFolder, relative) : null;
        return new StoreGame(GameStore.Xbox, name, shown.Trim(), gameFolder, $@"shell:AppsFolder\{FamilyName(name, publisher)}!{app}") { Exe = exe };
    }

    /// <summary>
    /// The folders a drive's <c>.GamingRoot</c> names: <c>RGBX</c>, how many, then each name (UTF-16, ending with a
    /// zero), relative to the drive.
    /// </summary>
    public static IReadOnlyList<string> GamingRoot(string drive, byte[] marker)
    {
        if (marker.Length < 8 || Encoding.ASCII.GetString(marker, 0, 4) != "RGBX")
            return [];
        var count = BitConverter.ToInt32(marker, 4);
        var names = Encoding.Unicode.GetString(marker, 8, (marker.Length - 8) / 2 * 2).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return [.. names.Take(Math.Max(0, count)).Select(name => Path.Combine(drive, name.Trim()))];
    }

    /// <summary>
    /// A package's family name, as Windows makes it: its name, and a hash of its publisher (SHA-256 of it in UTF-16, the
    /// first 64 bits written in Crockford's base 32): Microsoft's is <c>8wekyb3d8bbwe</c>.
    /// </summary>
    internal static string FamilyName(string name, string publisher)
    {
        const string Digits = "0123456789abcdefghjkmnpqrstvwxyz";
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        // 64 bits and a 0 make 65: thirteen digits of five bits each.
        var bits = (ulong)hash[0] << 56 | (ulong)hash[1] << 48 | (ulong)hash[2] << 40 | (ulong)hash[3] << 32
            | (ulong)hash[4] << 24 | (ulong)hash[5] << 16 | (ulong)hash[6] << 8 | hash[7];
        var id = new StringBuilder(13);
        for (var i = 0; i < 13; i++)
        {
            var shift = 64 - 5 * (i + 1);
            var digit = shift >= 0 ? (int)(bits >> shift & 31) : (int)(bits << -shift & 31);
            id.Append(Digits[digit]);
        }
        return $"{name}_{id}";
    }

    /// <summary>The first app in a package's manifest (<c>&lt;Applications&gt;&lt;Application Id="…"&gt;</c>), or null.</summary>
    private static string? ManifestApp(string? manifest)
    {
        if (manifest is null)
            return null;
        try
        {
            return (string?)Child(Child(XElement.Parse(manifest), "Applications"), "Application")?.Attribute("Id") is { Length: > 0 } id ? id : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? Combine(string folder, string relative)
    {
        try
        {
            return InstallFolders.Normalize(Path.Combine(folder, "Content", relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiousBonum.App.Layout;

/// <summary>A font or image carried in a layout package.</summary>
/// <param name="Extension">Lower case with the dot, e.g. <c>.ttf</c>; always one the app accepts.</param>
public sealed record PackagedFile(string Extension, byte[] Data);

/// <summary>What <see cref="LayoutPackage.Read"/> found in a package, ready for <see cref="LayoutLibrary.Import"/>.</summary>
public sealed class LayoutPackageContents
{
    /// <summary>The name the layout was exported under, or the package's file name. Always a valid layout name.</summary>
    public required string Name { get; init; }

    /// <summary>The layout, with the theme's font file and background image cleared; <see cref="Font"/> and <see cref="Background"/> carry them.</summary>
    public required LayoutDocument Layout { get; init; }

    public PackagedFile? Font { get; init; }

    public PackagedFile? Background { get; init; }

    /// <summary>Entries that were left out, and why, for telling the user.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];
}

/// <summary>
/// A layout packed into one shareable <c>.ibl</c> file: a zip with <c>layout.json</c>, a small <c>manifest.json</c> and
/// the theme's font file and background image under <c>assets/</c>, referenced by those relative paths so the
/// package works on another PC. Packages come from other people, so reading one trusts nothing in it: entry names
/// with <c>..</c> or absolute paths reject the whole package, only the layout and a font and image it references are
/// read, and sizes are capped by counting the bytes actually decompressed.
/// </summary>
public static class LayoutPackage
{
    public const string Extension = ".ibl";

    public const string DialogFilter = "Impious Bonum layout (*.ibl)|*.ibl";

    public static readonly IReadOnlyList<string> FontExtensions = [".ttf", ".otf"];

    public const long MaxLayoutBytes = 1024 * 1024;

    public const long MaxFileBytes = 50L * 1024 * 1024;

    public const int MaxEntries = 100;

    private const string LayoutEntry = "layout.json";
    private const string ManifestEntry = "manifest.json";
    private const string AssetFolder = "assets/";
    private const string FormatName = "impious-bonum-layout";
    private const int FormatVersion = 1;

    /// <summary>
    /// Writes <paramref name="layout"/> and the files its theme points at to <paramref name="destination"/>.
    /// A font or image that can't be found is left out and named in the returned warnings. A layout using one of your
    /// saved themes has that theme's values copied in (as the editor's Detach does), since the other PC won't have it;
    /// built-in themes stay as names.
    /// </summary>
    /// <param name="themes">The saved themes the layout may name; null for only the built-in ones.</param>
    public static IReadOnlyList<string> Write(LayoutDocument layout, string name, Stream destination, ThemeLibrary? themes = null)
    {
        var warnings = new List<string>();
        themes ??= new ThemeLibrary(null);
        var copy = Clone(layout);
        if (ThemeLibrary.BaseName(copy.Theme) is { } themeName && !ThemeLibrary.IsBuiltIn(themeName))
            copy.Theme = themes.Resolve(copy.Theme).ToJson();
        var theme = themes.Resolve(copy.Theme);

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        ThemeLibrary.SetString(copy.Theme, "fontFile", Pack(zip, theme.FontFile, "font", FontExtensions, "font file", warnings));
        ThemeLibrary.SetString(copy.Theme, "backgroundImage", Pack(zip, theme.BackgroundImage, "background", BackgroundImage.Extensions, "background image", warnings));

        WriteText(zip, ManifestEntry, new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["name"] = name,
        }.ToJsonString(JsonDefaults.Options));
        WriteText(zip, LayoutEntry, JsonSerializer.Serialize(copy, JsonDefaults.Options));
        return warnings;
    }

    /// <summary>Writes a package file, replacing any existing one only once the new one is complete.</summary>
    public static IReadOnlyList<string> Write(LayoutDocument layout, string name, string path, ThemeLibrary? themes = null)
    {
        var temp = path + ".tmp";
        try
        {
            IReadOnlyList<string> warnings;
            using (var file = File.Create(temp))
                warnings = Write(layout, name, file, themes);
            File.Move(temp, path, overwrite: true);
            return warnings;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// Reads a package. Throws <see cref="InvalidDataException"/> with a message for the user if it isn't a usable
    /// layout package or breaks the safety rules.
    /// </summary>
    /// <param name="fallbackName">Used when the package doesn't carry a usable name, normally its file name.</param>
    public static LayoutPackageContents Read(Stream source, string fallbackName)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("This isn't an Impious Bonum layout file.");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries)
                throw new InvalidDataException($"The file has too many entries ({zip.Entries.Count}).");

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            foreach (var entry in zip.Entries)
            {
                if (!IsSafeEntryName(entry.FullName))
                    throw new InvalidDataException($"The file contains an unsafe path: {entry.FullName}");
                if (entry.FullName.EndsWith('/'))
                    continue;
                var key = entry.FullName.Replace('\\', '/');
                if (!entries.TryAdd(key, entry))
                    throw new InvalidDataException($"The file contains {entry.FullName} twice.");
            }

            if (!entries.TryGetValue(LayoutEntry, out var layoutEntry))
                throw new InvalidDataException("This isn't an Impious Bonum layout file (there's no layout.json in it).");

            var layout = ParseLayout(ReadLimited(layoutEntry, MaxLayoutBytes));
            var name = ReadName(entries, fallbackName);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LayoutEntry, ManifestEntry };
            var font = Unpack(entries, ThemeLibrary.GetString(layout.Theme, "fontFile"), FontExtensions, "font file", used, skipped);
            var background = Unpack(entries, ThemeLibrary.GetString(layout.Theme, "backgroundImage"), BackgroundImage.Extensions, "background image", used, skipped);
            ThemeLibrary.SetString(layout.Theme, "fontFile", null);
            ThemeLibrary.SetString(layout.Theme, "backgroundImage", null);

            skipped.AddRange(entries.Keys.Where(key => !used.Contains(key)).Order(StringComparer.OrdinalIgnoreCase).Select(key => $"{key}: not used by the layout"));
            return new LayoutPackageContents { Name = name, Layout = layout, Font = font, Background = background, Skipped = skipped };
        }
    }

    public static LayoutPackageContents Read(string path)
    {
        using var file = File.OpenRead(path);
        return Read(file, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// False for anything that could land outside the folder it's unpacked into: absolute paths, drive letters,
    /// <c>..</c> segments, or characters Windows doesn't allow in names.
    /// </summary>
    public static bool IsSafeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] is '/' or '\\' || name.Contains(':') || name.Any(char.IsControl))
            return false;
        var segments = name.Split('/', '\\');
        return !segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0);
    }

    private static string? Pack(ZipArchive zip, string? path, string baseName, IReadOnlyList<string> extensions, string what, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!extensions.Contains(extension))
        {
            warnings.Add($"Left out the {what}, which isn't a supported type: {path}");
            return null;
        }
        if (!File.Exists(path))
        {
            warnings.Add($"Left out the {what}, which wasn't found: {path}");
            return null;
        }
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            warnings.Add($"Left out the {what}, which is bigger than {MaxFileBytes / (1024 * 1024)} MB: {path}");
            return null;
        }

        var entryName = AssetFolder + baseName + extension;
        zip.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        return entryName;
    }

    private static PackagedFile? Unpack(Dictionary<string, ZipArchiveEntry> entries, string? reference, IReadOnlyList<string> extensions, string what, HashSet<string> used, List<string> skipped)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        var key = reference.Replace('\\', '/');
        var extension = Path.GetExtension(key).ToLowerInvariant();
        if (!key.StartsWith(AssetFolder, StringComparison.OrdinalIgnoreCase) || !entries.TryGetValue(key, out var entry))
        {
            // A path on the other PC, or a file that isn't in the package: nothing to use here.
            skipped.Add($"The {what} isn't in the file: {reference}");
            return null;
        }
        used.Add(key);
        if (!extensions.Contains(extension))
        {
            skipped.Add($"{key}: a {what} should be one of {string.Join(", ", extensions)}");
            return null;
        }
        return new PackagedFile(extension, ReadLimited(entry, MaxFileBytes));
    }

    private static LayoutDocument ParseLayout(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize<LayoutDocument>(WithoutBom(json), JsonDefaults.Options)
                ?? throw new InvalidDataException("The layout in the file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The layout in the file can't be read: {ex.Message}");
        }
    }

    private static string ReadName(Dictionary<string, ZipArchiveEntry> entries, string fallbackName)
    {
        string? name = null;
        if (entries.TryGetValue(ManifestEntry, out var manifest))
        {
            try
            {
                name = (JsonNode.Parse(WithoutBom(ReadLimited(manifest, MaxLayoutBytes))) as JsonObject)?.GetString("name");
            }
            catch (JsonException)
            {
                // The name is a convenience; fall back to the file name.
            }
        }

        foreach (var candidate in new[] { name, fallbackName, "Imported layout" })
        {
            var trimmed = candidate?.Trim();
            if (trimmed is not null && LayoutLibrary.ValidateName(trimmed) is null)
                return trimmed;
        }
        return "Imported layout";
    }

    /// <summary>Hand-made files saved by Notepad may start with a UTF-8 byte order mark, which the JSON reader refuses.</summary>
    private static ReadOnlySpan<byte> WithoutBom(byte[] json) =>
        json.AsSpan().StartsWith("\uFEFF"u8) ? json.AsSpan(3) : json;

    /// <summary>Reads an entry, stopping as soon as it passes the limit: the size in the zip header can't be trusted.</summary>
    private static byte[] ReadLimited(ZipArchiveEntry entry, long limit)
    {
        var tooBig = new InvalidDataException($"{entry.FullName} is bigger than {limit / (1024 * 1024)} MB.");
        if (entry.Length > limit)
            throw tooBig;

        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > limit)
                throw tooBig;
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void WriteText(ZipArchive zip, string entryName, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entryName, CompressionLevel.Optimal).Open());
        writer.Write(text);
    }

    private static LayoutDocument Clone(LayoutDocument layout) =>
        JsonSerializer.Deserialize<LayoutDocument>(JsonSerializer.Serialize(layout, JsonDefaults.Options), JsonDefaults.Options)!;
}

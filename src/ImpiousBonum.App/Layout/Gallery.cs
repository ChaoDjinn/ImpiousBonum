using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiousBonum.App.Layout;

/// <summary>A layout someone has shared in the gallery.</summary>
/// <param name="File">The file's name in the gallery folder: an <c>.ibl</c> package or a bare <c>.json</c> layout.</param>
/// <param name="Width">The layout's canvas width, so a screen's shape can be matched; 0 if not given.</param>
/// <param name="Widgets">How many widgets it has, not counting shapes and pictures, to tell busy layouts from simple ones; 0 if not given.</param>
public sealed record GalleryEntry(string Name, string File, string? Author, string? Description, int Width, int Height, int Widgets = 0)
{
    /// <summary>"1920 × 480 · 12 widgets · by someone", for the list.</summary>
    public string Details => string.Join(" · ", new[]
    {
        Width > 0 && Height > 0 ? $"{Width} × {Height}" : null,
        Widgets > 0 ? $"{Widgets} widget{(Widgets == 1 ? "" : "s")}" : null,
        Author is { Length: > 0 } a ? $"by {a}" : null,
    }.OfType<string>());
}

/// <summary>
/// The shared layout gallery: an <c>index.json</c> listing layouts, and the layouts, in one web folder (the
/// <c>gallery</c> folder of the Impious Bonum repository unless settings say otherwise). Everything in it comes from
/// other people, so names are checked before they become addresses, sizes are capped, and packages are read with
/// <see cref="LayoutPackage.Read(Stream, string)"/>'s safety rules.
/// </summary>
public static class Gallery
{
    public const string DefaultAddress = "https://raw.githubusercontent.com/ChaoDjinn/ImpiousBonum/main/gallery/";

    public const string IndexFile = "index.json";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The entries in an index, skipping any without a name or with a file name that isn't a plain .ibl or .json name.</summary>
    public static IReadOnlyList<GalleryEntry> ParseIndex(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (root?["layouts"] is not JsonArray layouts)
            return [];
        var entries = new List<GalleryEntry>();
        foreach (var node in layouts.OfType<JsonObject>())
        {
            var name = node.GetString("name")?.Trim();
            var file = node.GetString("file")?.Trim();
            if (string.IsNullOrEmpty(name) || file is null || !IsPlainFileName(file))
                continue;
            entries.Add(new GalleryEntry(name, file, node.GetString("author"), node.GetString("description"),
                (int)node.GetDouble("width", 0), (int)node.GetDouble("height", 0), (int)node.GetDouble("widgets", 0)));
        }
        return entries;
    }

    /// <summary>A file name with nothing that could reach outside the gallery folder, ending .ibl or .json.</summary>
    public static bool IsPlainFileName(string file) =>
        file.Length is > 0 and <= 200
        && file.IndexOfAny(['/', '\\', ':', '?', '#', '%', '<', '>', '"', '|', '*']) < 0
        && !file.Contains("..")
        && !file.Any(char.IsControl)
        && (file.EndsWith(LayoutPackage.Extension, StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    public static async Task<IReadOnlyList<GalleryEntry>> ListAsync(string address, CancellationToken cancellationToken = default)
    {
        var bytes = await GetAsync(new Uri(Folder(address), IndexFile), LayoutPackage.MaxLayoutBytes, cancellationToken).ConfigureAwait(false);
        return ParseIndex(System.Text.Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Downloads and opens an entry's layout. Throws <see cref="InvalidDataException"/> if it isn't a usable layout.</summary>
    public static async Task<LayoutPackageContents> DownloadAsync(string address, GalleryEntry entry, CancellationToken cancellationToken = default)
    {
        var limit = entry.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? LayoutPackage.MaxLayoutBytes : LayoutPackage.MaxFileBytes;
        var bytes = await GetAsync(new Uri(Folder(address), Uri.EscapeDataString(entry.File)), limit, cancellationToken).ConfigureAwait(false);
        return Open(entry, bytes);
    }

    /// <summary>An entry's downloaded file as a package, named after the entry.</summary>
    public static LayoutPackageContents Open(GalleryEntry entry, byte[] data)
    {
        if (entry.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return LayoutPackage.FromLayoutJson(data, entry.Name);
        using var stream = new MemoryStream(data);
        var package = LayoutPackage.Read(stream, entry.Name);
        // The gallery's name is what was picked; the one inside the package may differ.
        return new LayoutPackageContents
        {
            Name = LayoutLibrary.ValidateName(entry.Name) is null ? entry.Name : package.Name,
            Layout = package.Layout,
            Font = package.Font,
            Background = package.Background,
            Images = package.Images,
            Skipped = package.Skipped,
        };
    }

    private static Uri Folder(string address) => new(address.EndsWith('/') ? address : address + "/");

    /// <summary>
    /// Reads at most <paramref name="limit"/> bytes, counting what actually arrives rather than trusting the header. A
    /// gallery in a folder on this PC (a copy of the repository's, say, to try layouts before sharing them) is read from disk.
    /// </summary>
    private static async Task<byte[]> GetAsync(Uri uri, long limit, CancellationToken cancellationToken)
    {
        if (uri.IsFile)
        {
            var info = new FileInfo(uri.LocalPath);
            if (info.Exists && info.Length > limit)
                throw new InvalidDataException($"{info.Name} is bigger than {limit / (1024 * 1024)} MB.");
            return await File.ReadAllBytesAsync(info.FullName, cancellationToken).ConfigureAwait(false);
        }

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > limit)
                throw new InvalidDataException($"{uri.Segments[^1]} is bigger than {limit / (1024 * 1024)} MB.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Core.WebIdentity.UserAgent);
        return client;
    }
}

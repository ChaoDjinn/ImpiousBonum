using System.IO;
using System.Text.Json;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// The saved layouts: one <c>layouts\&lt;name&gt;.json</c> file per layout in the data folder. File operations only;
/// <see cref="LayoutStore"/> adds the active layout and the file watcher on top.
/// </summary>
public sealed class LayoutLibrary
{
    public const string DefaultName = "Default";

    private const int MaxNameLength = 60;

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    public LayoutLibrary(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory = Path.Combine(dataDirectory, "layouts");
    }

    public string DataDirectory { get; }

    /// <summary>The <c>layouts</c> folder.</summary>
    public string Directory { get; }

    /// <summary>Where the single layout lived before saved layouts; moved to <c>layouts\Default.json</c> on first run.</summary>
    public string LegacyFile => Path.Combine(DataDirectory, "layout.json");

    public string PathOf(string name) => Path.Combine(Directory, name + ".json");

    /// <summary>
    /// Creates the layouts folder. An existing layout.json becomes the "Default" layout, so nothing is lost on
    /// upgrade; with no layouts at all, the built-in default is written.
    /// </summary>
    public void EnsureSeeded()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var defaultPath = PathOf(DefaultName);
        if (File.Exists(LegacyFile) && !File.Exists(defaultPath))
            File.Move(LegacyFile, defaultPath);

        if (List().Count == 0)
            File.WriteAllText(defaultPath, ReadDefaultJson());
    }

    /// <summary>Layout names, sorted.</summary>
    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .OfType<string>()
            .Where(name => ValidateName(name) is null)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The saved name matching <paramref name="name"/> (ignoring case), or null if there isn't one.</summary>
    public string? Find(string? name) =>
        name is null ? null : List().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary><paramref name="preferred"/> if it exists, else "Default", else the first layout.</summary>
    public string Resolve(string? preferred) =>
        Find(preferred) ?? Find(DefaultName) ?? List().FirstOrDefault() ?? DefaultName;

    /// <summary>Throws <see cref="JsonException"/> or <see cref="IOException"/> if the file can't be read.</summary>
    public LayoutDocument Load(string name)
    {
        var json = ReadWithRetry(PathOf(name));
        return JsonSerializer.Deserialize<LayoutDocument>(json, JsonDefaults.Options) ?? LoadDefault();
    }

    /// <summary>
    /// Writes a layout, creating it if it's new. JSON can't carry comments through a round trip, so a short header
    /// pointing at the widget reference replaces any hand-written ones.
    /// </summary>
    public void Save(string name, LayoutDocument layout)
    {
        RequireValid(name);
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(layout, JsonDefaults.Options);
        File.WriteAllText(PathOf(name), SavedHeader + json + Environment.NewLine);
    }

    public void Rename(string name, string newName)
    {
        RequireExisting(name);
        RequireAvailable(newName, except: name);
        File.Move(PathOf(name), PathOf(newName));
    }

    /// <summary>Copies the saved file as it is on disk (hand-written comments included).</summary>
    public void Duplicate(string name, string newName)
    {
        RequireExisting(name);
        RequireAvailable(newName);
        File.Copy(PathOf(name), PathOf(newName));
    }

    /// <summary>Deletes a layout. The last one can't be deleted: the dashboard always has something to show.</summary>
    public void Delete(string name)
    {
        RequireExisting(name);
        if (List().Count <= 1)
            throw new InvalidOperationException("The last layout can't be deleted.");
        File.Delete(PathOf(name));
        DeleteUnusedImportFolders();
    }

    /// <summary>
    /// Saves an imported layout as <paramref name="name"/>, replacing any layout of that name. Its font and image go
    /// in a folder of their own under <c>layouts</c> and the theme points at them there. Returns the saved name.
    /// </summary>
    public string Import(LayoutPackageContents package, string name)
    {
        RequireValid(name);
        var layout = package.Layout;
        if (package.Font is not null || package.Background is not null)
        {
            var folder = NewImportFolder(name);
            System.IO.Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, ImportMarker), "Files for an imported layout. Deleted when no layout uses them.\n");
            layout.Theme.FontFile = Place(folder, "font", package.Font);
            layout.Theme.BackgroundImage = Place(folder, "background", package.Background);
        }
        name = Find(name) ?? name;
        Save(name, layout);
        DeleteUnusedImportFolders();
        return name;
    }

    /// <summary>Marks a folder <see cref="Import"/> made, so only those are ever cleaned up.</summary>
    private const string ImportMarker = "imported.txt";

    private static string? Place(string folder, string baseName, PackagedFile? file)
    {
        if (file is null)
            return null;
        // Fixed names: nothing from the package decides where a file is written.
        var path = Path.Combine(folder, baseName + file.Extension);
        File.WriteAllBytes(path, file.Data);
        return path;
    }

    /// <summary><c>layouts\&lt;name&gt;</c>, or <c>&lt;name&gt; (2)</c> and so on if that's taken; a copy may still use the old files.</summary>
    private string NewImportFolder(string name)
    {
        var folder = Path.Combine(Directory, name);
        for (var i = 2; System.IO.Directory.Exists(folder) || File.Exists(folder); i++)
            folder = Path.Combine(Directory, $"{name} ({i})");
        return folder;
    }

    /// <summary>
    /// Removes folders made by <see cref="Import"/> that no saved layout points into any more (after a delete, or a
    /// re-import over the same name). Does nothing if a layout can't be read, since its references are then unknown.
    /// </summary>
    public void DeleteUnusedImportFolders()
    {
        if (!System.IO.Directory.Exists(Directory))
            return;
        var folders = System.IO.Directory.EnumerateDirectories(Directory)
            .Where(folder => File.Exists(Path.Combine(folder, ImportMarker)))
            .ToList();
        if (folders.Count == 0)
            return;

        var used = new List<string>();
        foreach (var name in List())
        {
            try
            {
                var theme = Load(name).Theme;
                used.AddRange(new[] { theme.FontFile, theme.BackgroundImage }.OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p)).Select(FullPathOrEmpty));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return;
            }
        }

        foreach (var folder in folders)
        {
            var prefix = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            if (used.Any(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use (e.g. the font is loaded); try again next time.
            }
        }
    }

    private static string FullPathOrEmpty(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>Why <paramref name="name"/> can't be used as a new layout name, or null if it can.</summary>
    /// <param name="except">The layout being renamed, which may keep its own name in different case.</param>
    public string? CheckNewName(string name, string? except = null)
    {
        if (ValidateName(name) is { } problem)
            return problem;
        var existing = Find(name);
        if (existing is not null && !string.Equals(existing, except, StringComparison.OrdinalIgnoreCase))
            return $"There's already a layout called \"{existing}\".";
        return null;
    }

    /// <summary>Why <paramref name="name"/> isn't a safe layout file name, or null if it is.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name != name.Trim())
            return "The name can't start or end with a space.";
        if (name.Length > MaxNameLength)
            return $"Keep the name under {MaxNameLength} characters.";
        if (name.Any(char.IsControl))
            return "The name can't contain control characters.";
        var bad = name.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']);
        if (bad >= 0)
            return $"The name can't contain {name[bad]}.";
        if (name.EndsWith('.'))
            return "The name can't end with a full stop.";
        if (ReservedNames.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            return $"\"{name}\" is reserved by Windows.";
        return null;
    }

    public static LayoutDocument LoadDefault() =>
        JsonSerializer.Deserialize<LayoutDocument>(ReadDefaultJson(), JsonDefaults.Options)!;

    private const string SavedHeader =
        "// Impious Bonum layout, saved by the layout editor. You can still edit it by hand; changes apply on save.\n" +
        "// Widget reference: https://github.com/ChaoDjinn/ImpiousBonum/blob/main/docs/widgets.md\n";

    private static string ReadDefaultJson()
    {
        using var stream = typeof(LayoutLibrary).Assembly.GetManifestResourceStream("ImpiousBonum.default-layout.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void RequireValid(string name)
    {
        if (ValidateName(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
    }

    private void RequireExisting(string name)
    {
        if (Find(name) is null)
            throw new FileNotFoundException($"There's no layout called \"{name}\".", PathOf(name));
    }

    private void RequireAvailable(string name, string? except = null)
    {
        if (CheckNewName(name, except) is { } problem)
            throw new ArgumentException(problem, nameof(name));
    }

    private static string ReadWithRetry(string path)
    {
        // The file may still be locked by the editor that just saved it.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}

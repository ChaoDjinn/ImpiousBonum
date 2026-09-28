using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// Named themes a layout can use with <c>"theme": "Name"</c>: the built-in ones shipped with the app, and the saved ones,
/// one <c>themes\&lt;name&gt;.json</c> file each (a <see cref="ThemeSettings"/> object) in the data folder.
/// Built-in themes can be used but not overwritten or deleted; a saved theme can't take a built-in theme's name.
/// Themes don't chain: a <c>base</c> key inside a theme file is ignored.
/// </summary>
public sealed class ThemeLibrary
{
    /// <summary>The key in a layout's theme that names the theme its other values override.</summary>
    public const string BaseKey = "base";

    private const string ResourcePrefix = "ImpiousBonum.themes.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> BuiltIns = new(ReadBuiltIns);

    /// <param name="dataDirectory">The data folder, or null for only the built-in themes.</param>
    public ThemeLibrary(string? dataDirectory)
    {
        Directory = dataDirectory is null ? null : Path.Combine(dataDirectory, "themes");
    }

    /// <summary>The <c>themes</c> folder, or null when there are only built-in themes.</summary>
    public string? Directory { get; }

    /// <summary>The built-in themes' names, sorted.</summary>
    public static IReadOnlyList<string> BuiltInNames => BuiltIns.Value.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsBuiltIn(string? name) => name is not null && BuiltIns.Value.ContainsKey(name);

    public string PathOf(string name) =>
        Path.Combine(Directory ?? throw new InvalidOperationException("This theme library has no folder."), name + ".json");

    public void EnsureCreated()
    {
        if (Directory is not null)
            System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>Saved themes' names, sorted. Files named like a built-in theme are left out: the built-in one wins.</summary>
    public IReadOnlyList<string> ListSaved()
    {
        if (Directory is null || !System.IO.Directory.Exists(Directory))
            return [];
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .OfType<string>()
            .Where(name => LayoutLibrary.ValidateName(name) is null && !IsBuiltIn(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Built-in themes first, then saved ones.</summary>
    public IReadOnlyList<string> List() => [.. BuiltInNames, .. ListSaved()];

    /// <summary>The theme name matching <paramref name="name"/> (ignoring case), or null if there isn't one.</summary>
    public string? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : List().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A theme's values as written in its file. Throws <see cref="FileNotFoundException"/> if there's no such theme, or
    /// <see cref="JsonException"/> or <see cref="IOException"/> if its file can't be read.
    /// </summary>
    public JsonObject Load(string name)
    {
        var found = Find(name) ?? throw new FileNotFoundException($"There's no theme called \"{name}\".", Directory is null ? null : PathOf(name));
        var json = BuiltIns.Value.TryGetValue(found, out var builtIn) ? builtIn : LayoutLibrary.ReadWithRetry(PathOf(found));
        var values = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException($"The theme \"{found}\" should be a {{ … }} object.");
        values.Remove(BaseKey);
        return values;
    }

    /// <summary>Writes a saved theme, creating it if it's new. Built-in themes can't be written.</summary>
    public void Save(string name, JsonObject values)
    {
        if (LayoutLibrary.ValidateName(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        if (IsBuiltIn(name))
            throw new ArgumentException($"\"{Find(name)}\" is a built-in theme and can't be changed.", nameof(name));

        var copy = values.DeepClone().AsObject();
        copy.Remove(BaseKey);
        EnsureCreated();
        File.WriteAllText(PathOf(Find(name) ?? name), SavedHeader + copy.ToJsonString(JsonDefaults.Options) + Environment.NewLine);
    }

    /// <summary>Deletes a saved theme. Layouts using it fall back to the built-in defaults (the validator says so).</summary>
    public void Delete(string name)
    {
        if (IsBuiltIn(name))
            throw new ArgumentException($"\"{name}\" is a built-in theme and can't be deleted.", nameof(name));
        var found = Find(name) ?? throw new FileNotFoundException($"There's no theme called \"{name}\".", PathOf(name));
        File.Delete(PathOf(found));
    }

    /// <summary>Why <paramref name="name"/> can't be used for a new theme, or null if it can.</summary>
    public string? CheckNewName(string name)
    {
        if (LayoutLibrary.ValidateName(name) is { } problem)
            return problem;
        if (Find(name) is { } existing)
            return IsBuiltIn(existing) ? $"\"{existing}\" is a built-in theme." : $"There's already a theme called \"{existing}\".";
        return null;
    }

    /// <summary>The theme named by a layout theme's <c>base</c> key, or null.</summary>
    public static string? BaseName(JsonObject? theme)
    {
        var node = theme?.FirstOrDefault(p => string.Equals(p.Key, BaseKey, StringComparison.OrdinalIgnoreCase)).Value;
        return node is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    /// <summary>
    /// The values to draw a layout with: its own theme values over its base theme's, over the built-in defaults.
    /// A base theme that's missing or broken counts as empty; <see cref="Problem"/> explains why.
    /// </summary>
    public ThemeSettings Resolve(JsonObject? theme) => ThemeSettings.Merge(TryLoadBase(theme), theme);

    /// <summary>Why a layout theme's base theme can't be used, or null if it can (or there isn't one).</summary>
    public string? Problem(JsonObject? theme)
    {
        if (BaseName(theme) is not { } name)
            return null;
        if (Find(name) is null)
            return $"there's no theme called \"{name}\". Themes: {string.Join(", ", List())}.";
        try
        {
            Load(name);
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return $"couldn't read the theme \"{name}\": {ex.Message}";
        }
    }

    private JsonObject? TryLoadBase(JsonObject? theme)
    {
        if (BaseName(theme) is not { } name || Find(name) is null)
            return null;
        try
        {
            return Load(name);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private const string SavedHeader =
        "// Impious Bonum theme, saved by the layout editor. Layouts use it with \"theme\": \"<name>\" (or \"base\": \"<name>\").\n" +
        "// Changes apply to every layout using it as soon as you save. Theme reference: https://github.com/ChaoDjinn/ImpiousBonum/blob/main/docs/widgets.md#theme\n";

    private static IReadOnlyDictionary<string, string> ReadBuiltIns()
    {
        var assembly = typeof(ThemeLibrary).Assembly;
        var themes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal))
                continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            themes[resource[ResourcePrefix.Length..^".json".Length]] = reader.ReadToEnd();
        }
        return themes;
    }
}

using System.Text.Json;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// The games the Epic Games launcher has installed, from the manifest it keeps for each in its data folder
/// (<c>Manifests\&lt;id&gt;.item</c>, which is JSON): the game's name, the folder it's in, and the ids that start it
/// through the launcher. Nothing is fetched. Unreal Engine and its plugins, add-ons and installs that haven't finished
/// are left out.
/// </summary>
public sealed class EpicLibrary(IGameFiles files)
{
    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>Every game the launcher has installed; none without it.</summary>
    public IReadOnlyList<StoreGame> Games()
    {
        if (files.EpicDataPath() is not { } data)
            return [];
        var games = new List<StoreGame>();
        foreach (var manifest in files.Files(Path.Combine(data, "Manifests"), "*.item"))
        {
            if (files.ReadText(manifest) is { } json && Manifest(json) is { } game)
                games.Add(game);
        }
        return games;
    }

    /// <summary>The game a manifest describes, or null if it's missing a part or isn't a game.</summary>
    internal static StoreGame? Manifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Lenient);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var appName = Text(root, "AppName");
            var folder = InstallFolders.GameFolder(Text(root, "InstallLocation"));
            if (appName is null || folder is null || IsTrue(root, "bIsIncompleteInstall"))
                return null;
            // An add-on is installed into its game's folder, and names that game as its main one.
            if (Text(root, "MainGameAppName") is { } main && !main.Equals(appName, StringComparison.OrdinalIgnoreCase))
                return null;
            // The engine and its plugins are in the launcher's library too, without "games" among their categories.
            if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
                && !categories.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            var name = (Text(root, "DisplayName") ?? appName).Trim();
            return new StoreGame(GameStore.Epic, appName, name, folder, LaunchLink(Text(root, "CatalogNamespace"), Text(root, "CatalogItemId"), appName))
            {
                Exe = Exe(folder, Text(root, "LaunchExecutable")),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The link that has the launcher start the game, as its desktop shortcuts do: by namespace, item and app name
    /// where the manifest has them all, else by app name, which older launchers used.
    /// </summary>
    internal static string LaunchLink(string? catalogNamespace, string? catalogItemId, string appName)
    {
        var id = catalogNamespace is null || catalogItemId is null ? appName : $"{catalogNamespace}:{catalogItemId}:{appName}";
        return $"com.epicgames.launcher://apps/{Uri.EscapeDataString(id)}?action=launch&silent=true";
    }

    /// <summary>The exe the launcher starts, which the manifest gives relative to the game's folder; null if it gives none.</summary>
    private static string? Exe(string folder, string? launchExecutable)
    {
        if (launchExecutable is null)
            return null;
        try
        {
            return InstallFolders.Normalize(Path.Combine(folder, launchExecutable));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

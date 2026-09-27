using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiousBonum.App;

internal static class JsonDefaults
{
    /// <summary>Hand-edit friendly: camelCase, comments and trailing commas allowed, indented output.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static string? GetString(this JsonObject obj, string key, string? fallback = null) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : fallback;

    public static double GetDouble(this JsonObject obj, string key, double fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : fallback;

    public static double? GetDouble(this JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;

    public static bool GetBool(this JsonObject obj, string key, bool fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;

    public static IEnumerable<JsonObject> GetObjects(this JsonObject obj, string key) =>
        obj[key] is JsonArray array ? array.OfType<JsonObject>() : [];
}

using System.Globalization;
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
        TryGetNumber(obj[key], out var d) ? d : fallback;

    public static double? GetDouble(this JsonObject obj, string key) =>
        TryGetNumber(obj[key], out var d) ? d : null;

    /// <summary>
    /// Reads any JSON number as a double. Needed because nodes built in code keep their CLR type
    /// (an int stays an int and <c>TryGetValue&lt;double&gt;</c> refuses it), unlike nodes parsed from a file.
    /// </summary>
    public static bool TryGetNumber(JsonNode? node, out double number)
    {
        number = 0;
        return node is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number);
    }

    public static bool GetBool(this JsonObject obj, string key, bool fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;

    public static IEnumerable<JsonObject> GetObjects(this JsonObject obj, string key) =>
        obj[key] is JsonArray array ? array.OfType<JsonObject>() : [];
}

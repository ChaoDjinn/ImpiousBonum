using System.Text.Json.Nodes;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A widget's settings from layout.json, read through its descriptors: missing or unusable values fall back to
/// the declared default, and numbers are clamped to their declared range.
/// Asking for a setting that isn't declared throws, so code and descriptors can't silently drift apart.
/// </summary>
public sealed class WidgetSettings(JsonObject json, IReadOnlyList<SettingDescriptor> settings)
{
    public JsonObject Json => json;

    public string String(string key) => OptionalString(key) ?? string.Empty;

    public string? OptionalString(string key)
    {
        var setting = Declared(key);
        return json[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : setting.Default as string;
    }

    public double Number(string key) =>
        OptionalNumber(key) ?? throw new InvalidOperationException($"Setting '{key}' has no default; use {nameof(OptionalNumber)}.");

    public double? OptionalNumber(string key)
    {
        var setting = Declared(key);
        var number = JsonDefaults.TryGetNumber(json[key], out var d) ? d : setting.Default as double?;
        return number is { } n ? Math.Clamp(n, setting.Min ?? double.MinValue, setting.Max ?? double.MaxValue) : null;
    }

    public bool Bool(string key)
    {
        var setting = Declared(key);
        return json[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : setting.Default is true;
    }

    public IReadOnlyList<WidgetSettings> Items(string key)
    {
        var setting = Declared(key);
        var array = json[key] as JsonArray ?? setting.Default as JsonArray;
        return array is null ? [] : array.OfType<JsonObject>().Select(item => new WidgetSettings(item, setting.ItemSettings ?? [])).ToList();
    }

    private SettingDescriptor Declared(string key) =>
        settings.FirstOrDefault(s => s.Key == key)
        ?? throw new InvalidOperationException($"Setting '{key}' is read but not declared in the widget's descriptor.");
}

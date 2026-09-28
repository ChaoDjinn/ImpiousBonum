using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// The saved design: a fixed-size canvas (scaled to fit the window), a theme, and positioned widgets.
/// Widgets stay as raw JSON objects so each widget type can own its own settings.
/// </summary>
public sealed class LayoutDocument
{
    /// <summary>Design width in layout units. The canvas is scaled uniformly to fill the window.</summary>
    public double Width { get; set; } = 1920;

    public double Height { get; set; } = 480;

    /// <summary>
    /// The layout's own theme values, exactly as written. <c>base</c> names a saved theme (see <see cref="ThemeLibrary"/>)
    /// and the other keys override its values; with no <c>base</c> they override the built-in defaults.
    /// In the file, <c>"theme": "Neon"</c> is short for <c>"theme": { "base": "Neon" }</c>.
    /// Use <see cref="ThemeLibrary.Resolve"/> for the values to draw with.
    /// </summary>
    [JsonConverter(typeof(ThemeJsonConverter))]
    public JsonObject Theme { get; set; } = new();

    /// <summary>Each widget has <c>type</c>, <c>x</c>, <c>y</c>, <c>width</c>, <c>height</c> plus type-specific settings.</summary>
    public List<JsonObject> Widgets { get; set; } = [];
}

/// <summary>A theme's values with every default filled in: what a layout is drawn with once its theme is resolved.</summary>
public sealed class ThemeSettings
{
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Optional path to a .ttf/.otf file to use instead of an installed font. The family name is read from the file.</summary>
    public string? FontFile { get; set; }

    public string FontWeight { get; set; } = "Light";

    public string Foreground { get; set; } = "#FFFFFF";

    /// <summary>Used for less important text such as the values in widget headers.</summary>
    public string Secondary { get; set; } = "#D0FFFFFF";

    public string Accent { get; set; } = "#FF9800";

    public string Background { get; set; } = "#000000";

    /// <summary>Optional path to a PNG/JPG drawn over <see cref="Background"/>, behind the widgets.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>How the image covers the canvas: fill, fit, stretch, center or tile.</summary>
    public string BackgroundFit { get; set; } = "fill";

    /// <summary>0–1. Lower values let the background colour show through the image.</summary>
    public double BackgroundOpacity { get; set; } = 1;

    /// <summary>Unfilled part of bars.</summary>
    public string Track { get; set; } = "#1A1A1A";

    /// <summary>Named by warning colour rules as <c>warning</c>.</summary>
    public string Warning { get; set; } = "#FFC107";

    /// <summary>Named by warning colour rules as <c>critical</c>.</summary>
    public string Critical { get; set; } = "#F44336";

    /// <summary>These values as a theme object: every key that has a value (the optional files left out when unset).</summary>
    public JsonObject ToJson()
    {
        var values = JsonSerializer.SerializeToNode(this, JsonDefaults.Options)!.AsObject();
        foreach (var key in values.Where(p => p.Value is null).Select(p => p.Key).ToList())
            values.Remove(key);
        return values;
    }

    /// <summary>
    /// <paramref name="values"/> laid over <paramref name="baseValues"/>, over the built-in defaults. Keys match ignoring
    /// case, like the rest of the file. A <c>base</c> key is ignored.
    /// </summary>
    public static ThemeSettings Merge(JsonObject? baseValues, JsonObject? values)
    {
        var merged = new JsonObject();
        foreach (var source in new[] { baseValues, values })
        {
            foreach (var (key, value) in source ?? new JsonObject())
            {
                if (string.Equals(key, ThemeLibrary.BaseKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (merged.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) is { } existing)
                    merged.Remove(existing);
                // Remove rather than keep an explicit null: a missing key deserialises to the default, a null doesn't.
                if (value is not null)
                    merged[key] = value.DeepClone();
            }
        }

        try
        {
            return merged.Deserialize<ThemeSettings>(JsonDefaults.Options) ?? new ThemeSettings();
        }
        catch (JsonException)
        {
            // A value of the wrong type (the validator explains which); draw with the defaults rather than nothing.
            return new ThemeSettings();
        }
    }
}

/// <summary>Reads a layout's theme as an object or a bare theme name; writes a bare name back when that's all there is.</summary>
public sealed class ThemeJsonConverter : JsonConverter<JsonObject>
{
    // "theme": null reads as an empty theme rather than leaving the property null.
    public override bool HandleNull => true;

    public override JsonObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonNode.Parse(ref reader) switch
        {
            JsonObject obj => obj,
            JsonValue value when value.TryGetValue<string>(out var name) => new JsonObject { [ThemeLibrary.BaseKey] = name },
            _ => new JsonObject(),
        };

    public override void Write(Utf8JsonWriter writer, JsonObject value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        else if (value.Count == 1 && ThemeLibrary.BaseName(value) is { } name)
            writer.WriteStringValue(name);
        else
            value.WriteTo(writer, options);
    }
}

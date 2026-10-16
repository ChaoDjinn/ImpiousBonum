using System.Text.Encodings.Web;
using System.Text.Json;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Remote;

/// <summary>
/// The readings as JSON, for other apps (Home Assistant, Rainmeter and the like) to read from the tablet view's server:
/// each one's id, name, category, unit, value, text, and the value as the dashboard shows it. Nothing else.
/// </summary>
public static class ReadingsJson
{
    // "°C" and "µg/m³" as they are, not as \u escapes, so an app that shows the text as it comes shows it right.
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Every reading: <c>{"metrics": [ … ]}</c>, by category and then id.</summary>
    public static byte[] All(MetricStore store) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteStartArray("metrics");
        foreach (var definition in store.Definitions)
        {
            if (store.TryGet(definition.Id, out var sample))
                WriteReading(writer, sample);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    /// <summary>One reading by its id (ignoring case), or null if there's no such reading.</summary>
    public static byte[]? One(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? Write(writer => WriteReading(writer, sample)) : null;

    /// <summary><c>{"error": …}</c>, for a request that didn't find a reading.</summary>
    public static byte[] Error(string message) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("error", message);
        writer.WriteEndObject();
    });

    /// <summary>The unit's name in the JSON, e.g. <c>percent</c> or <c>bytesPerSecond</c>.</summary>
    public static string UnitName(MetricUnit unit) => JsonNamingPolicy.CamelCase.ConvertName(unit.ToString());

    private static void WriteReading(Utf8JsonWriter writer, MetricSample sample)
    {
        var definition = sample.Definition;
        writer.WriteStartObject();
        writer.WriteString("id", definition.Id);
        writer.WriteString("name", definition.Name);
        writer.WriteString("category", definition.Category);
        writer.WriteString("unit", UnitName(definition.Unit));
        // No reading yet (or a sensor that's gone) is null, as JSON has no NaN.
        if (sample.Value is double value && double.IsFinite(value))
            writer.WriteNumber("value", value);
        else
            writer.WriteNull("value");
        writer.WriteString("text", sample.Text);
        writer.WriteString("formatted", MetricFormatter.Format(sample));
        writer.WriteEndObject();
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options))
            write(writer);
        return buffer.ToArray();
    }
}

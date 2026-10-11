using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.CustomData;

/// <summary>
/// Somewhere to read your own metrics from: a web address returning JSON or text, or a command whose output is JSON or
/// text. Each <see cref="CustomField"/> becomes the metric <c>custom.&lt;source&gt;.&lt;field&gt;</c>.
/// Sources are per PC (settings.json), never part of a layout, so a shared layout can't bring a command with it.
/// </summary>
/// <param name="Name">Letters, digits, <c>-</c> and <c>_</c>; the middle part of the metric ids.</param>
/// <param name="Url">A web address to GET. Either this or <paramref name="Command"/>.</param>
/// <param name="Command">A program to run, e.g. <c>powershell</c>, with <paramref name="Arguments"/>.</param>
/// <param name="Headers">Sent with web requests, e.g. <c>Authorization: Bearer …</c> for Home Assistant.</param>
/// <param name="Every">Seconds between reads (at least <see cref="MinimumEvery"/>).</param>
public sealed record CustomSource(
    string Name,
    string? Url = null,
    string? Command = null,
    string? Arguments = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    int Every = 60,
    IReadOnlyList<CustomField>? Fields = null)
{
    public const int MinimumEvery = 5;

    public const string Prefix = "custom.";

    [JsonIgnore]
    public bool IsWeb => !string.IsNullOrWhiteSpace(Url);

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(MinimumEvery, Every));

    /// <summary>The fields to read; a source without any gives its whole output as <c>value</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<CustomField> FieldsOrValue => Fields is { Count: > 0 } fields ? fields : [new CustomField("value")];

    public string MetricId(CustomField field) => $"{Prefix}{Name}.{field.Name}";

    /// <summary>All the settings as one string, to tell when a source was edited (records compare lists by reference).</summary>
    [JsonIgnore]
    public string Key => System.Text.Json.JsonSerializer.Serialize(this);

    /// <summary>The text metric saying whether the last read worked, e.g. "OK" or "HTTP 401 Unauthorized".</summary>
    [JsonIgnore]
    public string StatusId => $"{Prefix}{Name}.status";

    /// <summary>Why this source can't be used, or null if it can.</summary>
    public string? Problem()
    {
        if (!IsValidName(Name))
            return "Give it a name made of letters, digits, - and _.";
        if (IsWeb == !string.IsNullOrWhiteSpace(Command))
            return "Give it a web address or a command, not both.";
        if (IsWeb && (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            return "The web address should start with http:// or https://.";
        foreach (var field in FieldsOrValue)
        {
            if (!IsValidName(field.Name) || field.Name == "status")
                return $"\"{field.Name}\" can't be a field name: use letters, digits, - and _ (and not \"status\").";
            if (CustomField.ParseUnit(field.Unit) is null)
                return $"\"{field.Unit}\" isn't a unit. Units: {string.Join(", ", CustomField.UnitNames)}.";
        }
        if (FieldsOrValue.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != FieldsOrValue.Count)
            return "Two fields have the same name.";
        return null;
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Reads every field from what the source returned: JSON is looked up by each field's path, anything else is the
    /// whole text (for a field without a path). Numbers become values, text stays text.
    /// </summary>
    public IReadOnlyList<CustomReading> Read(string output)
    {
        JsonNode? json = null;
        var trimmed = output.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                json = JsonNode.Parse(trimmed);
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON after all: treat as text.
            }
        }

        var readings = new List<CustomReading>();
        foreach (var field in FieldsOrValue)
        {
            var unit = CustomField.ParseUnit(field.Unit) ?? MetricUnit.None;
            string? raw;
            if (string.IsNullOrWhiteSpace(field.Path))
                raw = json is null ? trimmed : json is JsonValue v ? v.ToString() : json.ToJsonString();
            else if (json is null)
                raw = null;
            else
                raw = JsonPath.Find(json, field.Path) switch
                {
                    null => null,
                    JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "1" : "0",
                    JsonValue value => value.ToString(),
                    var node => node.ToJsonString(),
                };
            readings.Add(CustomReading.From(field, unit, raw));
        }
        return readings;
    }
}

/// <param name="Name">The last part of the metric id.</param>
/// <param name="Path">Where in the JSON, e.g. <c>attributes.temperature</c> or <c>items[0].value</c>; empty for the whole output.</param>
/// <param name="Unit">One of <see cref="UnitNames"/>; <c>auto</c> is a number if it looks like one, else text.</param>
/// <param name="Max">A full bar or gauge, for units without a natural maximum.</param>
public sealed record CustomField(string Name, string? Path = null, string? Unit = null, double? Max = null)
{
    public static readonly IReadOnlyList<string> UnitNames =
        ["auto", "text", "number", "percent", "celsius", "fahrenheit", "bytes", "bytespersecond", "milliseconds", "watts", "volts", "megahertz", "rpm", "fps"];

    /// <summary>The unit for a name in <see cref="UnitNames"/> (empty is auto, which is <see cref="MetricUnit.None"/> here), or null for an unknown name.</summary>
    public static MetricUnit? ParseUnit(string? name) => (name ?? "auto").Trim().ToLowerInvariant() switch
    {
        "" or "auto" or "number" => MetricUnit.None,
        "text" => MetricUnit.Text,
        "percent" or "%" => MetricUnit.Percent,
        "celsius" or "°c" => MetricUnit.Celsius,
        "fahrenheit" or "°f" => MetricUnit.Fahrenheit,
        "bytes" => MetricUnit.Bytes,
        "bytespersecond" => MetricUnit.BytesPerSecond,
        "milliseconds" or "ms" => MetricUnit.Milliseconds,
        "watts" or "w" => MetricUnit.Watts,
        "volts" or "v" => MetricUnit.Volts,
        "megahertz" or "mhz" => MetricUnit.Megahertz,
        "rpm" => MetricUnit.Rpm,
        "fps" => MetricUnit.FramesPerSecond,
        _ => null,
    };

    [JsonIgnore]
    public bool IsAuto => string.IsNullOrWhiteSpace(Unit) || Unit.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One field's reading: a number, text, or nothing (missing from the output).</summary>
public readonly record struct CustomReading(CustomField Field, double? Value, string? Text)
{
    public static CustomReading From(CustomField field, MetricUnit unit, string? raw)
    {
        if (raw is null)
            return new CustomReading(field, null, null);
        var number = double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : (double?)null;
        if (unit == MetricUnit.Text || (field.IsAuto && number is null))
            return new CustomReading(field, null, raw.Trim());
        return new CustomReading(field, number, null);
    }
}

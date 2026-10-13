using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

/// <summary>Which air quality index to show.</summary>
public enum AqiScale
{
    /// <summary>The European index in Europe and the US index elsewhere (see <see cref="AirQualityReadings.ScaleFor"/>).</summary>
    Automatic,

    /// <summary>The European Environment Agency's: 0 to 100, and above for extremely poor air.</summary>
    European,

    /// <summary>The US Environmental Protection Agency's: 0 to 500.</summary>
    Us,
}

/// <summary>The pollen types Open-Meteo has counts for.</summary>
public enum PollenType
{
    Alder,
    Birch,
    Olive,
    Grass,
    Mugwort,
    Ragweed,
}

/// <summary>Pollen types put together the way hay fever forecasts give them.</summary>
public enum PollenGroup
{
    Grass,

    /// <summary>Alder, birch and olive.</summary>
    Tree,

    /// <summary>Mugwort and ragweed.</summary>
    Weed,
}

/// <summary>How much pollen there is, from none to very high.</summary>
public enum PollenLevel
{
    None,
    Low,
    Moderate,
    High,
    VeryHigh,
}

/// <summary>Where to get the air quality for, and which index to show it on.</summary>
public sealed record AirQualityRequest(double Latitude, double Longitude, AqiScale Scale)
{
    /// <summary>Changes when new readings are needed straight away: a new place. Both indices are fetched, so the scale doesn't count.</summary>
    public string Key => FormattableString.Invariant($"{Latitude:0.####},{Longitude:0.####}");
}

/// <summary>
/// The air now, from Open-Meteo's air quality forecast (made by CAMS, the Copernicus Atmosphere Monitoring Service):
/// both indices, particles and gases in µg/m³, and pollen in grains/m³. Null is unknown: pollen is only counted in
/// Europe, in season, and Open-Meteo sends null for it elsewhere.
/// </summary>
public sealed record AirQualityReport
{
    public double? EuropeanAqi { get; init; }

    public double? UsAqi { get; init; }

    /// <summary>Fine particles, 2.5 µm across or less.</summary>
    public double? Pm25 { get; init; }

    /// <summary>Particles 10 µm across or less.</summary>
    public double? Pm10 { get; init; }

    public double? Ozone { get; init; }

    public double? NitrogenDioxide { get; init; }

    public double? Alder { get; init; }

    public double? Birch { get; init; }

    public double? Olive { get; init; }

    public double? Grass { get; init; }

    public double? Mugwort { get; init; }

    public double? Ragweed { get; init; }

    /// <summary>The count of one type of pollen, if there is one here.</summary>
    public double? Pollen(PollenType type) => type switch
    {
        PollenType.Alder => Alder,
        PollenType.Birch => Birch,
        PollenType.Olive => Olive,
        PollenType.Grass => Grass,
        PollenType.Mugwort => Mugwort,
        PollenType.Ragweed => Ragweed,
        _ => null,
    };

    /// <summary>
    /// Reads Open-Meteo's air quality JSON (the current block, as <see cref="OpenMeteo"/> asks for it). Throws
    /// <see cref="JsonException"/>, <see cref="KeyNotFoundException"/> or the like if it isn't one.
    /// </summary>
    public static AirQualityReport Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement.GetProperty("current");
        return new AirQualityReport
        {
            EuropeanAqi = Value(current, "european_aqi"),
            UsAqi = Value(current, "us_aqi"),
            Pm25 = Value(current, "pm2_5"),
            Pm10 = Value(current, "pm10"),
            Ozone = Value(current, "ozone"),
            NitrogenDioxide = Value(current, "nitrogen_dioxide"),
            Alder = Value(current, "alder_pollen"),
            Birch = Value(current, "birch_pollen"),
            Olive = Value(current, "olive_pollen"),
            Grass = Value(current, "grass_pollen"),
            Mugwort = Value(current, "mugwort_pollen"),
            Ragweed = Value(current, "ragweed_pollen"),
        };
    }

    private static double? Value(JsonElement block, string name) =>
        block.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}

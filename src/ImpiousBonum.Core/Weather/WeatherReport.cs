using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

public enum WeatherUnits
{
    /// <summary>°C and km/h.</summary>
    Metric,

    /// <summary>°F and mph.</summary>
    Imperial,
}

/// <summary>Where to get the weather for, and in which units.</summary>
/// <param name="Name">The place's name, for showing; the coordinates decide the forecast.</param>
public sealed record WeatherRequest(string Name, double Latitude, double Longitude, WeatherUnits Units)
{
    /// <summary>Changes when a new forecast is needed straight away rather than at the next scheduled fetch.</summary>
    public string Key => FormattableString.Invariant($"{Latitude:0.####},{Longitude:0.####},{Units}");
}

/// <summary>A place found by name, to pick from when setting the weather location.</summary>
/// <param name="Region">The state, province or the like, e.g. England or Missouri.</param>
public sealed record WeatherPlace(string Name, string? Region, string? Country, double Latitude, double Longitude)
{
    /// <summary>
    /// Smaller areas the place is in, smallest first: a town or district, then a county (West Derby's is Liverpool). Shown
    /// between the name and the region, and what "West Derby, Liverpool" is matched against.
    /// </summary>
    public IReadOnlyList<string> Areas { get; init; } = [];

    /// <summary>The country's two-letter code, e.g. GB, so "London, GB" finds it too.</summary>
    public string? CountryCode { get; init; }

    /// <summary>
    /// "West Derby, Liverpool, England, United Kingdom". Empty parts are left out, and so are areas that only repeat a part
    /// before them, alone or within a longer name (London's "Greater London").
    /// </summary>
    public string Description
    {
        get
        {
            var parts = new List<string>();
            foreach (var area in Areas.Prepend(Name))
            {
                if (!string.IsNullOrWhiteSpace(area) && !parts.Any(p => Names(area.Trim(), p)))
                    parts.Add(area.Trim());
            }
            // A region or country is only left out when it's the same as a part before it: "York, New York" needs both.
            foreach (var part in new[] { Region, Country })
            {
                if (!string.IsNullOrWhiteSpace(part) && !parts.Contains(part.Trim(), StringComparer.OrdinalIgnoreCase))
                    parts.Add(part.Trim());
            }
            return string.Join(", ", parts);
        }
    }

    /// <summary>Whether <paramref name="text"/> is <paramref name="name"/>, or has it as whole words ("Greater London", "City of Springfield").</summary>
    private static bool Names(string text, string name)
    {
        for (var i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = i + name.Length;
            if ((i == 0 || !char.IsLetterOrDigit(text[i - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
        }
        return false;
    }
}

/// <summary>One step of the forecast: an hour, or a day with its low.</summary>
/// <param name="Time">Local time at the place.</param>
/// <param name="Low">For days, the day's low; null for hours.</param>
public sealed record WeatherPeriod(DateTime Time, double Temperature, double? Low, int Code, bool IsDay, double? RainChance);

/// <summary>A forecast from Open-Meteo: conditions now, today's range, and the hours and days ahead.</summary>
/// <param name="Time">When the current conditions are for, in the place's local time.</param>
public sealed record WeatherReport(
    WeatherUnits Units,
    DateTime Time,
    double Temperature,
    double FeelsLike,
    double Humidity,
    double WindSpeed,
    double WindDirection,
    int Code,
    bool IsDay,
    IReadOnlyList<WeatherPeriod> Hours,
    IReadOnlyList<WeatherPeriod> Days,
    DateTime? Sunrise,
    DateTime? Sunset)
{
    /// <summary>Today's forecast, if the report has days.</summary>
    public WeatherPeriod? Today => Days.Count > 0 ? Days[0] : null;

    public WeatherPeriod? Tomorrow => Days.Count > 1 ? Days[1] : null;

    /// <summary>The hours after the current one.</summary>
    public IEnumerable<WeatherPeriod> HoursAhead => Hours.Where(h => h.Time > Time);

    /// <summary>
    /// Reads Open-Meteo's forecast JSON (current, hourly and daily blocks as requested by <see cref="OpenMeteo"/>).
    /// Throws <see cref="JsonException"/> or <see cref="FormatException"/> if it isn't one.
    /// </summary>
    public static WeatherReport Parse(string json, WeatherUnits units)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = root.GetProperty("current");

        var hours = new List<WeatherPeriod>();
        if (root.TryGetProperty("hourly", out var hourly))
        {
            var times = hourly.GetProperty("time");
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                hours.Add(new WeatherPeriod(
                    ParseTime(times[i]),
                    Number(hourly, "temperature_2m", i) ?? double.NaN,
                    null,
                    (int)(Number(hourly, "weather_code", i) ?? 0),
                    Number(hourly, "is_day", i) != 0,
                    Number(hourly, "precipitation_probability", i)));
            }
        }

        var days = new List<WeatherPeriod>();
        DateTime? sunrise = null, sunset = null;
        if (root.TryGetProperty("daily", out var daily))
        {
            var times = daily.GetProperty("time");
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                days.Add(new WeatherPeriod(
                    ParseTime(times[i]),
                    Number(daily, "temperature_2m_max", i) ?? double.NaN,
                    Number(daily, "temperature_2m_min", i),
                    (int)(Number(daily, "weather_code", i) ?? 0),
                    true,
                    Number(daily, "precipitation_probability_max", i)));
            }
            if (daily.TryGetProperty("sunrise", out var rises) && rises.GetArrayLength() > 0 && rises[0].ValueKind == JsonValueKind.String)
                sunrise = ParseTime(rises[0]);
            if (daily.TryGetProperty("sunset", out var sets) && sets.GetArrayLength() > 0 && sets[0].ValueKind == JsonValueKind.String)
                sunset = ParseTime(sets[0]);
        }

        return new WeatherReport(
            units,
            ParseTime(current.GetProperty("time")),
            current.GetProperty("temperature_2m").GetDouble(),
            Value(current, "apparent_temperature") ?? current.GetProperty("temperature_2m").GetDouble(),
            Value(current, "relative_humidity_2m") ?? double.NaN,
            Value(current, "wind_speed_10m") ?? double.NaN,
            Value(current, "wind_direction_10m") ?? double.NaN,
            (int)(Value(current, "weather_code") ?? 0),
            Value(current, "is_day") != 0,
            hours,
            days,
            sunrise,
            sunset);
    }

    private static DateTime ParseTime(JsonElement value) =>
        DateTime.Parse(value.GetString() ?? throw new FormatException("A forecast time is missing."), CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static double? Value(JsonElement block, string name) =>
        block.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static double? Number(JsonElement block, string name, int index) =>
        block.TryGetProperty(name, out var values) && index < values.GetArrayLength() && values[index].ValueKind == JsonValueKind.Number
            ? values[index].GetDouble()
            : null;
}

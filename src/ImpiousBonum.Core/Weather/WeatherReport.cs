using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

public enum WeatherUnits
{
    /// <summary>°C, km/h, mm, km and hPa.</summary>
    Metric,

    /// <summary>°F, mph, inches, miles and inHg.</summary>
    Imperial,
}

/// <summary>Where to get the weather for, and in which units.</summary>
/// <param name="Name">The place's name, for showing; the coordinates decide the forecast.</param>
public sealed record WeatherRequest(string Name, double Latitude, double Longitude, WeatherUnits Units)
{
    /// <summary>Changes when a new forecast is needed straight away rather than at the next scheduled fetch.</summary>
    public string Key => FormattableString.Invariant($"{Latitude:0.####},{Longitude:0.####},{Units}");
}

/// <summary>A place's forecast, for the weather widget to move between places.</summary>
/// <param name="Name">The place's name as it was picked, e.g. "Paris, Île-de-France, France".</param>
/// <param name="Report">Null until it's fetched, and again once it's too old to show.</param>
public sealed record PlaceForecast(string Name, WeatherReport? Report);

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

/// <summary>
/// One step of the forecast: an hour, or a day with its low. The readings after the first few are properties, so a
/// period can be made with only those it needs; null is unknown (Open-Meteo leaves values out at times).
/// </summary>
/// <param name="Time">Local time at the place.</param>
/// <param name="Low">For days, the day's low; null for hours.</param>
/// <param name="RainChance">For hours, the chance of rain in the hour up to <paramref name="Time"/>; for days, the day's highest.</param>
public sealed record WeatherPeriod(DateTime Time, double Temperature, double? Low, int Code, bool IsDay, double? RainChance)
{
    /// <summary>Rain, showers and snow, in mm or inches: in the hour up to <see cref="Time"/>, or the day's total.</summary>
    public double? Precipitation { get; init; }

    /// <summary>Wind speed: in the hour, or the day's strongest.</summary>
    public double? Wind { get; init; }

    /// <summary>Wind gusts: in the hour up to <see cref="Time"/>, or the day's strongest.</summary>
    public double? Gusts { get; init; }

    /// <summary>UV index: in the hour, or the day's highest.</summary>
    public double? Uv { get; init; }

    /// <summary>For hours, the air pressure at sea level in hPa, whatever the units.</summary>
    public double? Pressure { get; init; }

    /// <summary>For days, how long the sun is up.</summary>
    public TimeSpan? Daylight { get; init; }
}

/// <summary>A quarter of an hour of the forecast's next few hours, for the rain soon (see <see cref="RainAhead"/>).</summary>
/// <param name="Time">Local time at the place: the end of the quarter-hour, as Open-Meteo gives it.</param>
/// <param name="Precipitation">Rain, showers and snow in the 15 minutes up to <paramref name="Time"/>, in mm or inches.</param>
public sealed record QuarterHour(DateTime Time, double Precipitation);

/// <summary>
/// A forecast from Open-Meteo: conditions now, today's range, and the hours and days ahead. The readings added since
/// the first few are properties, like <see cref="WeatherPeriod"/>'s, and null when the forecast didn't have them.
/// </summary>
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
    private const double MetresPerFoot = 0.3048;

    /// <summary>How far the place's clock is ahead of UTC.</summary>
    public TimeSpan? UtcOffset { get; init; }

    public double? Gusts { get; init; }

    public double? Uv { get; init; }

    /// <summary>Air pressure at sea level, in hPa whatever the units.</summary>
    public double? Pressure { get; init; }

    public double? DewPoint { get; init; }

    /// <summary>How far you can see, in metres whatever the units.</summary>
    public double? Visibility { get; init; }

    /// <summary>How much of the sky is cloud, in %.</summary>
    public double? Clouds { get; init; }

    /// <summary>Rain, showers and snow in the last 15 minutes, in mm or inches.</summary>
    public double? Precipitation { get; init; }

    /// <summary>
    /// The rain, showers and snow of the next three hours, a quarter of an hour at a time, from the one that ended last.
    /// Empty when the forecast hasn't got them; one Open-Meteo has no value for is left out.
    /// </summary>
    public IReadOnlyList<QuarterHour> QuarterHours { get; init; } = [];

    /// <summary>Today's forecast, if the report has days.</summary>
    public WeatherPeriod? Today => Days.Count > 0 ? Days[0] : null;

    public WeatherPeriod? Tomorrow => Days.Count > 1 ? Days[1] : null;

    /// <summary>The hours after the current one.</summary>
    public IEnumerable<WeatherPeriod> HoursAhead => Hours.Where(h => h.Time > Time);

    /// <summary>The time at the place at <paramref name="utc"/>, or <see cref="Time"/> if the forecast didn't give its offset from UTC.</summary>
    public DateTime LocalTime(DateTimeOffset utc) =>
        UtcOffset is { } offset ? DateTime.SpecifyKind(utc.UtcDateTime + offset, DateTimeKind.Unspecified) : Time;

    /// <summary>
    /// The hour that <paramref name="time"/> (local, at the place) is in, for its rain, gusts and chance of rain, if the
    /// forecast has it. Open-Meteo gives those for the hour up to each hour's time, so at 9:45 it's the 10:00 hour.
    /// </summary>
    public WeatherPeriod? HourEndingAfter(DateTime time) => Hours.FirstOrDefault(h => h.Time > time && h.Time <= time.AddHours(1));

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
                    Number(hourly, "precipitation_probability", i))
                {
                    Precipitation = Number(hourly, "precipitation", i),
                    Wind = Number(hourly, "wind_speed_10m", i),
                    Gusts = Number(hourly, "wind_gusts_10m", i),
                    Uv = Number(hourly, "uv_index", i),
                    Pressure = Number(hourly, "pressure_msl", i),
                });
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
                    Number(daily, "precipitation_probability_max", i))
                {
                    Precipitation = Number(daily, "precipitation_sum", i),
                    Wind = Number(daily, "wind_speed_10m_max", i),
                    Gusts = Number(daily, "wind_gusts_10m_max", i),
                    Uv = Number(daily, "uv_index_max", i),
                    Daylight = Number(daily, "daylight_duration", i) is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                });
            }
            if (daily.TryGetProperty("sunrise", out var rises) && rises.ValueKind == JsonValueKind.Array && rises.GetArrayLength() > 0 && rises[0].ValueKind == JsonValueKind.String)
                sunrise = ParseTime(rises[0]);
            if (daily.TryGetProperty("sunset", out var sets) && sets.ValueKind == JsonValueKind.Array && sets.GetArrayLength() > 0 && sets[0].ValueKind == JsonValueKind.String)
                sunset = ParseTime(sets[0]);
        }

        // Only for the rain soon, so a forecast without them is still a forecast.
        var quarters = new List<QuarterHour>();
        if (root.TryGetProperty("minutely_15", out var minutely) && minutely.TryGetProperty("time", out var quarterTimes) && quarterTimes.ValueKind == JsonValueKind.Array)
        {
            for (var i = 0; i < quarterTimes.GetArrayLength(); i++)
            {
                if (Number(minutely, "precipitation", i) is { } amount && quarterTimes[i].ValueKind == JsonValueKind.String)
                    quarters.Add(new QuarterHour(ParseTime(quarterTimes[i]), amount));
            }
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
            sunset)
        {
            UtcOffset = Value(root, "utc_offset_seconds") is { } offset ? TimeSpan.FromSeconds(offset) : null,
            Gusts = Value(current, "wind_gusts_10m"),
            Uv = Value(current, "uv_index"),
            Pressure = Value(current, "pressure_msl"),
            DewPoint = Value(current, "dew_point_2m"),
            Visibility = Value(current, "visibility") * (VisibilityInFeet(root, units) ? MetresPerFoot : 1),
            Clouds = Value(current, "cloud_cover"),
            Precipitation = Value(current, "precipitation"),
            QuarterHours = quarters,
        };
    }

    /// <summary>
    /// Open-Meteo gives visibility in feet once precipitation is asked for in inches, as it is for imperial units. The
    /// units block says which; without one, it's whatever was asked for.
    /// </summary>
    private static bool VisibilityInFeet(JsonElement root, WeatherUnits units) =>
        root.TryGetProperty("current_units", out var given) && given.TryGetProperty("visibility", out var unit) && unit.ValueKind == JsonValueKind.String
            ? unit.GetString() == "ft"
            : units == WeatherUnits.Imperial;

    private static DateTime ParseTime(JsonElement value) =>
        DateTime.Parse(value.GetString() ?? throw new FormatException("A forecast time is missing."), CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static double? Value(JsonElement block, string name) =>
        block.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static double? Number(JsonElement block, string name, int index) =>
        block.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array && index < values.GetArrayLength() && values[index].ValueKind == JsonValueKind.Number
            ? values[index].GetDouble()
            : null;
}

using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

/// <summary>
/// The Open-Meteo forecast and place-search APIs (open-meteo.com): free for non-commercial use, no account or key, and
/// the data is CC BY 4.0. Only the place name or coordinates you choose are sent.
/// </summary>
public static class OpenMeteo
{
    public const string Attribution = "Weather data by Open-Meteo.com";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The forecast for <paramref name="request"/>: now, 48 hours and 16 days. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static async Task<WeatherReport> ForecastAsync(WeatherRequest request, CancellationToken cancellationToken)
    {
        var json = await Http.GetStringAsync(ForecastUrl(request), cancellationToken).ConfigureAwait(false);
        return WeatherReport.Parse(json, request.Units);
    }

    /// <summary>
    /// Everything the forecast has in one request. The three hours before this one are for the pressure trend; the
    /// widget's hours start after the current one.
    /// </summary>
    internal static string ForecastUrl(WeatherRequest request) =>
        FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={request.Latitude:0.####}&longitude={request.Longitude:0.####}")
        + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m,"
        + "wind_gusts_10m,uv_index,pressure_msl,dew_point_2m,visibility,cloud_cover,precipitation"
        + "&hourly=temperature_2m,weather_code,is_day,precipitation_probability,precipitation,pressure_msl,wind_speed_10m,wind_gusts_10m,uv_index"
        + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,sunrise,sunset,"
        + "daylight_duration,uv_index_max,wind_speed_10m_max,wind_gusts_10m_max"
        + "&timezone=auto&forecast_days=16&past_hours=3&forecast_hours=48"
        + (request.Units == WeatherUnits.Imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" : "&wind_speed_unit=kmh");

    /// <summary>Places matching <paramref name="name"/> (a town, or "Town, Country"), best first, in the user's language where known.</summary>
    public static async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string name, CancellationToken cancellationToken)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count=10&language={language}&format=json";
        var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParsePlaces(json);
    }

    internal static IReadOnlyList<WeatherPlace> ParsePlaces(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];
        return results.EnumerateArray()
            .Where(r => r.TryGetProperty("latitude", out _) && r.TryGetProperty("longitude", out _))
            .Select(r => new WeatherPlace(
                Text(r, "name") ?? "?",
                Text(r, "admin1"),
                Text(r, "country"),
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble()))
            .ToList();
    }

    /// <summary>"51.5, -0.12" typed as a location: coordinates to use as they are, without searching.</summary>
    public static (double Latitude, double Longitude)? ParseCoordinates(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
                ? (latitude, longitude)
                : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}

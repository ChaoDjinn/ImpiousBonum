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

    /// <summary>The forecast for <paramref name="request"/>: now, 48 hours and 7 days. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static async Task<WeatherReport> ForecastAsync(WeatherRequest request, CancellationToken cancellationToken)
    {
        var imperial = request.Units == WeatherUnits.Imperial;
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={request.Latitude:0.####}&longitude={request.Longitude:0.####}")
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m"
            + "&hourly=temperature_2m,weather_code,is_day,precipitation_probability"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset"
            + "&timezone=auto&forecast_days=7&forecast_hours=48"
            + (imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "&wind_speed_unit=kmh");
        var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return WeatherReport.Parse(json, request.Units);
    }

    /// <summary>
    /// Up to <paramref name="count"/> places called <paramref name="name"/>, best first, in the user's language where known.
    /// <see cref="PlaceSearch"/> adds "Place, Area" searches and falls back to OpenStreetMap.
    /// </summary>
    public static async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string name, int count, CancellationToken cancellationToken)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var url = FormattableString.Invariant($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count={count}&language={language}&format=json");
        var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParsePlaces(json);
    }

    /// <summary>
    /// Reads Open-Meteo's place search. Of the finer areas, admin4 (often the town, such as Munich for Schwabing) and admin2
    /// (the county or district, such as Liverpool for West Derby) are kept; admin3 is mostly townships and arrondissements
    /// that people don't know places by.
    /// </summary>
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
                r.GetProperty("longitude").GetDouble())
            {
                Areas = [.. new[] { Text(r, "admin4"), Text(r, "admin2") }.OfType<string>()],
                CountryCode = Text(r, "country_code"),
            })
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

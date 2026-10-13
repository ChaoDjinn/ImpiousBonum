using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

/// <summary>
/// The US National Weather Service's alerts (api.weather.gov): free, no account or key, and in the public domain. It
/// asks apps to say who they are. Only a US place's coordinates are ever sent to it (see <see cref="WeatherWarnings.SourceFor"/>).
/// </summary>
public static class NationalWeatherService
{
    public const string Attribution = "Alerts from the US National Weather Service (weather.gov)";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Gets a page of the API as GeoJSON. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static Task<string> GetAsync(string url, CancellationToken cancellationToken) => Http.GetStringAsync(url, cancellationToken);

    /// <summary>The alerts in force at a point. It takes up to four decimal places, and no <c>limit</c>.</summary>
    internal static string AlertsUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"https://api.weather.gov/alerts/active?point={latitude:0.####},{longitude:0.####}");

    /// <summary>The forecast page for a place, which lists its alerts at the top: where a tap on one goes.</summary>
    internal static string PageUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"https://forecast.weather.gov/MapClick.php?lat={latitude:0.####}&lon={longitude:0.####}");

    /// <summary>
    /// Reads the alerts in a GeoJSON answer, in its order. Tests, exercises and cancellations are left out. Throws
    /// <see cref="JsonException"/>, <see cref="KeyNotFoundException"/> or the like if it isn't one.
    /// </summary>
    /// <param name="link">The page each warning opens.</param>
    public static IReadOnlyList<WeatherWarning> Parse(string json, string? link)
    {
        using var document = JsonDocument.Parse(json);
        var warnings = new List<WeatherWarning>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var alert) || alert.ValueKind != JsonValueKind.Object)
                continue;
            if (Text(alert, "status") is { } status && status != "Actual" || Text(alert, "messageType") == "Cancel")
                continue;
            if ((Text(alert, "id") ?? Text(feature, "id")) is not { } id)
                continue;

            var severity = Text(alert, "severity");
            var level = Level(severity);
            warnings.Add(new WeatherWarning(
                id,
                News.Feeds.Clean(Text(alert, "headline") ?? Text(alert, "event") ?? "Weather alert"),
                level,
                level == WarningLevel.Unknown ? null : severity,
                Time(alert, "onset") ?? Time(alert, "effective"),
                // When the weather itself ends, or failing that when this message does (the next one replaces it).
                Time(alert, "ends") ?? Time(alert, "expires"),
                link,
                WarningSource.NationalWeatherService)
            {
                Replaces = alert.TryGetProperty("references", out var references) && references.ValueKind == JsonValueKind.Array
                    ? [.. references.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Object ? Text(r, "identifier") : null).OfType<string>()]
                    : [],
            });
        }
        return warnings;
    }

    /// <summary>The NWS's severities are already the levels' words; anything else is unknown.</summary>
    internal static WarningLevel Level(string? severity) => severity switch
    {
        "Extreme" => WarningLevel.Extreme,
        "Severe" => WarningLevel.Severe,
        "Moderate" => WarningLevel.Moderate,
        "Minor" => WarningLevel.Minor,
        _ => WarningLevel.Unknown,
    };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json");
        return client;
    }
}

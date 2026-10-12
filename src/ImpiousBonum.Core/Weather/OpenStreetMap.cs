using System.Globalization;
using System.Text.Json;

namespace ImpiousBonum.Core.Weather;

/// <summary>
/// OpenStreetMap's place search (Nominatim, nominatim.openstreetmap.org), for the suburbs, parks and postcodes Open-Meteo
/// doesn't know. Its usage policy asks for no more than a request a second, an app that says who it is, and searches only
/// when someone asks for one, never as they type. <see cref="PlaceSearch"/> only uses it when Open-Meteo finds nothing (or
/// nothing in the area named).
/// </summary>
public static class OpenStreetMap
{
    public const string Attribution = "Place search by OpenStreetMap (© OpenStreetMap contributors)";

    public const string CopyrightPage = "https://www.openstreetmap.org/copyright";

    private static readonly HttpClient Http = CreateClient();
    private static readonly RequestSpacing Spacing = new(TimeSpan.FromSeconds(1));

    /// <summary>Places matching <paramref name="query"/> ("West Derby, Liverpool", "L12 5HJ"), best first, in the user's language where known.</summary>
    public static async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // "en-GB,en": the user's language as spoken where they are, then as spoken anywhere.
        var culture = CultureInfo.CurrentUICulture;
        var language = culture.Name.Length == 0 ? "en" : $"{culture.Name},{culture.TwoLetterISOLanguageName}";
        var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query.Trim())}&format=jsonv2&addressdetails=1&limit=10"
            + $"&accept-language={Uri.EscapeDataString(language)}";
        await Spacing.WaitAsync(cancellationToken).ConfigureAwait(false);
        var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParsePlaces(json);
    }

    /// <summary>
    /// Reads Nominatim's jsonv2 results with address details. The areas are the suburb, village, town or city it's in, then
    /// its county (or failing that its state district, such as Liverpool City Region, which is left out when it only
    /// repeats the city's name).
    /// </summary>
    internal static IReadOnlyList<WeatherPlace> ParsePlaces(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];
        var places = new List<WeatherPlace>();
        foreach (var result in document.RootElement.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object || Number(result, "lat") is not { } latitude || Number(result, "lon") is not { } longitude)
                continue;
            var address = result.TryGetProperty("address", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;
            string? Part(string name) => address.ValueKind == JsonValueKind.Object ? Text(address, name) : null;

            // A postcode or an address may have no name of its own; the first part of its full name is what it's called.
            var name = Text(result, "name") ?? Text(result, "display_name")?.Split(',')[0].Trim();
            places.Add(new WeatherPlace(string.IsNullOrEmpty(name) ? "?" : name, Part("state"), Part("country"), latitude, longitude)
            {
                Areas = [.. new[] { Part("suburb"), Part("village"), Part("town"), Part("city"), Part("county") ?? Part("state_district") }.OfType<string>()],
                CountryCode = Part("country_code")?.ToUpperInvariant(),
            });
        }
        return places;
    }

    /// <summary>Nominatim gives coordinates as strings, "53.4325964".</summary>
    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        double number;
        if (value.ValueKind == JsonValueKind.Number)
            number = value.GetDouble();
        else if (value.ValueKind != JsonValueKind.String || !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return null;
        return double.IsFinite(number) ? number : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}

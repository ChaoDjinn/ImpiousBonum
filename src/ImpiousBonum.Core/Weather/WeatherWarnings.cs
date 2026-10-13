namespace ImpiousBonum.Core.Weather;

/// <summary>
/// Which agency warns a place, the warnings in force now, and getting them: the Met Office's feed for a UK place's area,
/// or the National Weather Service's alerts for a US place. A place anywhere else is sent nowhere.
/// </summary>
public static class WeatherWarnings
{
    /// <summary>The US and the territories the NWS warns too: Puerto Rico, the US Virgin Islands, Guam, American Samoa and the Northern Marianas.</summary>
    private static readonly HashSet<string> NwsCountries = new(StringComparer.OrdinalIgnoreCase) { "US", "PR", "VI", "GU", "AS", "MP" };

    /// <summary>
    /// Who warns a place: by its country code when it was picked from a search, else by the name it was saved with
    /// ("…, United Kingdom" or "…, United States"), else for coordinates in Great Britain or the east of Northern Ireland
    /// (<see cref="InUk"/>), the Met Office. Coordinates alone never make a place American: the US's borders are too
    /// close to Canada's and Mexico's for a box, and only US places may go to the NWS.
    /// </summary>
    public static WarningSource SourceFor(string? countryCode, string? name, double latitude, double longitude)
    {
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            return string.Equals(countryCode.Trim(), "GB", StringComparison.OrdinalIgnoreCase) ? WarningSource.MetOffice
                : NwsCountries.Contains(countryCode.Trim()) ? WarningSource.NationalWeatherService
                : WarningSource.None;
        }
        var country = name?.Split(',').Last().Trim();
        if (string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase))
            return WarningSource.MetOffice;
        if (string.Equals(country, "United States", StringComparison.OrdinalIgnoreCase) || string.Equals(country, "United States of America", StringComparison.OrdinalIgnoreCase))
            return WarningSource.NationalWeatherService;
        return InUk(latitude, longitude) ? WarningSource.MetOffice : WarningSource.None;
    }

    /// <summary>
    /// Whether coordinates are in Great Britain or the east of Northern Ireland, and so nowhere else: not the Republic of
    /// Ireland (east to 5.99° W, north to 55.43° N), the Isle of Man, the Channel Islands or France. The rest of Northern
    /// Ireland is too near the border to tell by coordinates.
    /// </summary>
    internal static bool InUk(double latitude, double longitude)
    {
        if (latitude is < 49.8 or > 61)
            return false;
        if (latitude is >= 54 and <= 54.45 && longitude is >= -4.85 and <= -4.3)
            return false; // The Isle of Man.
        if (latitude is >= 54.45 and <= 55.35 && longitude is >= -6.85 and <= -5.4)
            return true; // Northern Ireland from Coleraine to the Ards, east of Donegal.
        // West: Scilly and Cornwall south of Ireland, then Wales and the Mull of Kintyre beside it, then the Hebrides north of it.
        var west = latitude < 51.35 ? -6.5 : latitude <= 55.45 ? -5.85 : -8.7;
        // East: Dungeness and Dover, keeping out Boulogne and Calais, then Lowestoft.
        var east = latitude < 51 ? 1 : latitude < 51.5 ? 1.5 : 1.8;
        return longitude >= west && longitude <= east;
    }

    /// <summary>The warnings still in force at <paramref name="now"/> (or yet to start), most serious first, then soonest.</summary>
    public static IReadOnlyList<WeatherWarning> Current(IEnumerable<WeatherWarning> warnings, DateTimeOffset now) =>
        [.. warnings
            .Where(w => w.End is null || w.End > now)
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.Start ?? DateTimeOffset.MinValue)
            .ThenBy(w => w.Headline, StringComparer.Ordinal)];

    /// <summary>The warnings for <paramref name="request"/>. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static Task<IReadOnlyList<WeatherWarning>> FetchAsync(WarningsRequest request, CancellationToken cancellationToken) =>
        FetchAsync(request, NationalWeatherService.GetAsync, MetOfficeWarnings.GetAsync, DateTimeOffset.UtcNow, cancellationToken);

    /// <param name="nws">Gets a page of the NWS's API; tests swap it.</param>
    /// <param name="metOffice">Gets a Met Office feed; tests swap it.</param>
    internal static async Task<IReadOnlyList<WeatherWarning>> FetchAsync(
        WarningsRequest request,
        Func<string, CancellationToken, Task<string>> nws,
        Func<string, CancellationToken, Task<string>> metOffice,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (request.Source)
        {
            case WarningSource.NationalWeatherService:
                var json = await nws(NationalWeatherService.AlertsUrl(request.Latitude, request.Longitude), cancellationToken).ConfigureAwait(false);
                return NationalWeatherService.Parse(json, NationalWeatherService.PageUrl(request.Latitude, request.Longitude));
            case WarningSource.MetOffice when MetOfficeWarnings.Find(request.Area) is { } area:
                var xml = await metOffice(MetOfficeWarnings.FeedUrl(area.Code), cancellationToken).ConfigureAwait(false);
                return MetOfficeWarnings.Parse(xml, now);
            default:
                return [];
        }
    }
}

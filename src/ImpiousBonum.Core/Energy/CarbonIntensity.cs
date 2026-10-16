using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImpiousBonum.Core.Energy;

/// <summary>Whose carbon intensity to get: all of Great Britain's, or the region of a postcode's first half.</summary>
/// <param name="Area">The first half of a postcode, e.g. LS1, or null for Great Britain as a whole.</param>
public sealed record GridCarbonRequest(string? Area)
{
    /// <summary>Changes when new figures are needed straight away: another area.</summary>
    public string Key => Area ?? "GB";
}

/// <summary>Half an hour of the grid's carbon intensity, in gCO₂/kWh: the forecast, and once it's past, the estimate of what it was.</summary>
/// <param name="Index">The API's level for it: very low, low, moderate, high or very high.</param>
public sealed record CarbonPeriod(DateTimeOffset From, DateTimeOffset To, double? Forecast, double? Actual, string? Index)
{
    /// <summary>The estimate where there is one, else the forecast.</summary>
    public double? Intensity => Actual ?? Forecast;
}

/// <summary>The grid's carbon intensity half an hour at a time, from now to a day ahead, for Great Britain or one of its regions.</summary>
/// <param name="Region">"Great Britain", or the region's name, e.g. Yorkshire.</param>
public sealed record CarbonForecast(string Region, IReadOnlyList<CarbonPeriod> Periods)
{
    public const string GreatBritain = "Great Britain";

    /// <summary>The half-hour <paramref name="now"/> is in, if the forecast has it.</summary>
    public CarbonPeriod? At(DateTimeOffset now) => Periods.FirstOrDefault(p => p.From <= now && now < p.To);

    /// <summary>The half-hour with the least carbon from now on (the earliest of any equal), if any are left.</summary>
    public CarbonPeriod? Lowest(DateTimeOffset now) => Periods.Where(p => p.To > now && p.Intensity is not null).MinBy(p => p.Intensity);

    /// <summary>
    /// Reads the API's answer: Great Britain's half-hours, or a region with its half-hours inside it. Throws
    /// <see cref="JsonException"/>, <see cref="KeyNotFoundException"/> or the like if it isn't one, including the
    /// <c>null</c> it sends for a postcode it doesn't know.
    /// </summary>
    public static CarbonForecast Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        // A region's is an object for the forecast, but an array of one for now alone.
        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 && data[0].TryGetProperty("data", out _))
            data = data[0];
        var region = GreatBritain;
        if (data.ValueKind == JsonValueKind.Object)
        {
            region = Text(data, "shortname") ?? region;
            data = data.GetProperty("data");
        }

        var periods = data.EnumerateArray().Select(Period).ToList();
        return periods.Count > 0 ? new CarbonForecast(region, periods) : throw new FormatException("The forecast has no half-hours.");
    }

    private static CarbonPeriod Period(JsonElement period)
    {
        var intensity = period.GetProperty("intensity");
        return new CarbonPeriod(Time(period, "from"), Time(period, "to"), Number(intensity, "forecast"), Number(intensity, "actual"), Text(intensity, "index"));
    }

    /// <summary>Times like <c>2026-10-16T16:30Z</c>, in UTC.</summary>
    private static DateTimeOffset Time(JsonElement element, string name) =>
        DateTimeOffset.Parse(element.GetProperty(name).GetString() ?? throw new FormatException($"No {name} time."), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// The Carbon Intensity API (carbonintensity.org.uk), run by NESO, Great Britain's National Energy System Operator
/// (formerly National Grid ESO): free, no account or key, under CC BY 4.0. Only for Great Britain: Northern Ireland's
/// grid is separate. Nothing is sent but the time, and the first half of a postcode if one is given for a region's
/// figures.
/// </summary>
public static partial class CarbonIntensity
{
    public const string Attribution = "Carbon intensity forecasts by NESO, from carbonintensity.org.uk (CC BY 4.0)";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The next 24 hours from now, for the request's area. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static async Task<CarbonForecast> ForecastAsync(GridCarbonRequest request, CancellationToken cancellationToken)
    {
        var json = await Http.GetStringAsync(ForecastUrl(request, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        return CarbonForecast.Parse(json);
    }

    /// <summary>
    /// The half-hour now and the 24 hours after it, in one request: Great Britain's, or with an area, its region's. The
    /// current half-hour comes first, so the readings now and the lowest ahead need nothing else.
    /// </summary>
    internal static string ForecastUrl(GridCarbonRequest request, DateTimeOffset now)
    {
        var from = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture);
        return request.Area is { } area
            ? $"https://api.carbonintensity.org.uk/regional/intensity/{from}/fw24h/postcode/{Uri.EscapeDataString(area)}"
            : $"https://api.carbonintensity.org.uk/intensity/{from}/fw24h";
    }

    /// <summary>
    /// The first half of a postcode typed in, e.g. LS1 from <c>ls1 4ap</c>: all the API needs for a region, and all
    /// that's kept. Null if it isn't one.
    /// </summary>
    public static string? Area(string? postcode)
    {
        var compact = string.Concat((postcode ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        // A whole postcode ends with its second half: a digit and two letters.
        if (compact.Length >= 5 && SecondHalf().IsMatch(compact[^3..]))
            compact = compact[..^3];
        return FirstHalf().IsMatch(compact) ? compact : null;
    }

    /// <summary>Northern Ireland's postcodes start BT; its grid isn't Great Britain's, so the API has nothing for them.</summary>
    public static bool InNorthernIreland(string area) => area.StartsWith("BT", StringComparison.OrdinalIgnoreCase);

    /// <summary>The API's level with a capital, e.g. Very low, or null without one.</summary>
    public static string? Level(string? index) =>
        string.IsNullOrWhiteSpace(index) ? null : char.ToUpperInvariant(index.Trim()[0]) + index.Trim()[1..].ToLowerInvariant();

    /// <summary>
    /// "Lowest now", or "Lowest around 03:30" when <paramref name="lowest"/> starts, on the PC's clock. Within a day ahead
    /// the time alone says which day: one earlier than now's is tomorrow's.
    /// </summary>
    public static string Lowest(CarbonPeriod lowest, DateTimeOffset now, TimeZoneInfo zone) =>
        lowest.From <= now ? "Lowest now" : $"Lowest around {TimeZoneInfo.ConvertTime(lowest.From, zone).ToString("t", CultureInfo.CurrentCulture)}";

    [GeneratedRegex("^[A-Z]{1,2}[0-9][A-Z0-9]?$")]
    private static partial Regex FirstHalf();

    [GeneratedRegex("^[0-9][A-Z]{2}$")]
    private static partial Regex SecondHalf();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

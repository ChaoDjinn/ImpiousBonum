namespace ImpiousBonum.Core.Weather;

/// <summary>Which way the air pressure has gone over the last three hours.</summary>
public enum PressureTrend
{
    Steady,
    Rising,
    Falling,
}

/// <summary>Readings worked out from a forecast rather than given by it, and the words and units to show them in.</summary>
public static class WeatherReadings
{
    /// <summary>A change of less than this, in hPa over three hours, counts as steady.</summary>
    public const double SteadyWithin = 1;

    private const double InchesOfMercuryPerHectopascal = 0.02953;
    private const double MetresPerMile = 1609.344;

    /// <summary>
    /// Whether the pressure rose, fell or held steady over the three hours to the hour <paramref name="time"/> is in
    /// (local, at the place), from the hourly pressures; null without both. A fall often comes before rain and wind,
    /// and some people feel it as a headache.
    /// </summary>
    public static PressureTrend? Trend(IReadOnlyList<WeatherPeriod> hours, DateTime time)
    {
        var now = hours.LastOrDefault(h => h.Time <= time);
        var before = now is null ? null : hours.FirstOrDefault(h => h.Time == now.Time.AddHours(-3));
        if (now?.Pressure is not double latest || before?.Pressure is not double earlier)
            return null;
        // Pressures come to 0.1 hPa; rounding keeps 1014.1 - 1013.1 from coming out a hair under 1.
        var change = Math.Round(latest - earlier, 1);
        return change >= SteadyWithin ? PressureTrend.Rising : change <= -SteadyWithin ? PressureTrend.Falling : PressureTrend.Steady;
    }

    /// <summary>The WHO's words for a UV index: Low (up to 2), Moderate (3–5), High (6–7), Very high (8–10) or Extreme (11 and up).</summary>
    public static string? UvLevel(double? index) => index is double uv && !double.IsNaN(uv)
        ? Math.Round(uv, MidpointRounding.AwayFromZero) switch
        {
            < 3 => "Low",
            < 6 => "Moderate",
            < 8 => "High",
            < 11 => "Very high",
            _ => "Extreme",
        }
        : null;

    /// <summary>"10 h 49 min", to the nearest minute.</summary>
    public static string Daylight(TimeSpan length)
    {
        var minutes = (int)Math.Round(length.TotalMinutes);
        return $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>A pressure in hPa, or inches of mercury for imperial units.</summary>
    public static double Pressure(double hectopascals, WeatherUnits units) =>
        units == WeatherUnits.Imperial ? hectopascals * InchesOfMercuryPerHectopascal : hectopascals;

    /// <summary>A distance in km, or miles for imperial units.</summary>
    public static double Distance(double metres, WeatherUnits units) =>
        units == WeatherUnits.Imperial ? metres / MetresPerMile : metres / 1000;
}

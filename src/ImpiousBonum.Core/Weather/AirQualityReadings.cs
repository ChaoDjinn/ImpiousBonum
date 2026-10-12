namespace ImpiousBonum.Core.Weather;

/// <summary>Words for the air quality indices and pollen counts, which index suits a place, and pollen by group.</summary>
public static class AirQualityReadings
{
    /// <summary>
    /// Whether a place is in CAMS's European forecast area, latitude 30° to 72° N and longitude 25° W to 45° E, where the
    /// European index and the pollen counts come from: Europe with Iceland and Turkey, North Africa's coast and the eastern
    /// Mediterranean. The Canary Islands and the Azores are just outside it.
    /// </summary>
    public static bool InEurope(double latitude, double longitude) => latitude is >= 30 and <= 72 && longitude is >= -25 and <= 45;

    /// <summary>The index to show at a place: the one chosen, or for <see cref="AqiScale.Automatic"/> the European index in Europe and the US index elsewhere.</summary>
    public static AqiScale ScaleFor(AqiScale setting, double latitude, double longitude) =>
        setting != AqiScale.Automatic ? setting : InEurope(latitude, longitude) ? AqiScale.European : AqiScale.Us;

    /// <summary>The level of an index in its own scale's words.</summary>
    public static string? AqiLevel(AqiScale scale, double? index) => scale == AqiScale.Us ? UsLevel(index) : EuropeanLevel(index);

    /// <summary>The European Environment Agency's words, as Open-Meteo's index bands them: Good (up to 20), Fair, Moderate, Poor, Very poor (to 100), then Extremely poor.</summary>
    public static string? EuropeanLevel(double? index) => Whole(index) switch
    {
        null => null,
        <= 20 => "Good",
        <= 40 => "Fair",
        <= 60 => "Moderate",
        <= 80 => "Poor",
        <= 100 => "Very poor",
        _ => "Extremely poor",
    };

    /// <summary>The US Environmental Protection Agency's words: Good (up to 50), Moderate (to 100), and so on to Hazardous (above 300).</summary>
    public static string? UsLevel(double? index) => Whole(index) switch
    {
        null => null,
        <= 50 => "Good",
        <= 100 => "Moderate",
        <= 150 => "Unhealthy for sensitive groups",
        <= 200 => "Unhealthy",
        <= 300 => "Very unhealthy",
        _ => "Hazardous",
    };

    /// <summary>
    /// How much pollen a count is for its type, in grains/m³. The bands are MeteoSwiss's ("Threshold values for pollen
    /// load classes of allergenic pollen types", 2024), which also suit the rest of central Europe; it doesn't band
    /// olive, so olive's are the Spanish Aerobiology Network's (Galán et al., 2007), which stop at high. Both are for a
    /// day's average, so an hour's count is a guide rather than the official level.
    /// </summary>
    public static PollenLevel? Level(PollenType type, double? count)
    {
        if (Whole(count) is not double grains)
            return null;
        // Where each level starts.
        (double Low, double Moderate, double High, double VeryHigh) from = type switch
        {
            PollenType.Alder => (1, 11, 70, 250),
            PollenType.Birch => (1, 11, 70, 300),
            PollenType.Olive => (1, 51, 201, double.PositiveInfinity),
            PollenType.Grass => (1, 20, 50, 150),
            PollenType.Mugwort => (1, 6, 15, 50),
            PollenType.Ragweed => (1, 6, 11, 40),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        return grains >= from.VeryHigh ? PollenLevel.VeryHigh
            : grains >= from.High ? PollenLevel.High
            : grains >= from.Moderate ? PollenLevel.Moderate
            : grains >= from.Low ? PollenLevel.Low
            : PollenLevel.None;
    }

    /// <summary>The pollen types in a group.</summary>
    public static IReadOnlyList<PollenType> Types(PollenGroup group) => group switch
    {
        PollenGroup.Grass => [PollenType.Grass],
        PollenGroup.Tree => [PollenType.Alder, PollenType.Birch, PollenType.Olive],
        PollenGroup.Weed => [PollenType.Mugwort, PollenType.Ragweed],
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };

    /// <summary>A group's pollen added up, or null where none of its types is counted.</summary>
    public static double? Count(AirQualityReport report, PollenGroup group)
    {
        var counts = Types(group).Select(report.Pollen).OfType<double>().ToList();
        return counts.Count > 0 ? counts.Sum() : null;
    }

    /// <summary>
    /// A group's level: that of its type with the most for its own bands, since each type's bands differ (20 grains of
    /// birch is moderate, of olive low). Null where none of its types is counted.
    /// </summary>
    public static PollenLevel? Level(AirQualityReport report, PollenGroup group) =>
        Types(group).Select(type => Level(type, report.Pollen(type))).Max();

    /// <summary>"None", "Low", "Moderate", "High" or "Very high".</summary>
    public static string Describe(PollenLevel level) => level == PollenLevel.VeryHigh ? "Very high" : level.ToString();

    /// <summary>
    /// The group with the most pollen, as "Grass high"; groups at the same level share it ("Grass and tree low"). "None"
    /// when no group has any, and null where there are no counts at all (outside Europe).
    /// </summary>
    public static string? PollenSummary(AirQualityReport report)
    {
        var levels = Enum.GetValues<PollenGroup>()
            .Select(group => (Group: group, Level: Level(report, group)))
            .Where(g => g.Level is not null)
            .ToList();
        if (levels.Count == 0)
            return null;
        var highest = levels.Max(g => g.Level!.Value);
        if (highest == PollenLevel.None)
            return Describe(PollenLevel.None);
        var names = levels.Where(g => g.Level == highest).Select(g => g.Group.ToString().ToLowerInvariant()).ToList();
        var groups = names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
        return $"{char.ToUpperInvariant(groups[0])}{groups[1..]} {Describe(highest).ToLowerInvariant()}";
    }

    /// <summary>Indices and counts are banded in whole numbers, so 20.4 is still 20 and 0.4 grains is none.</summary>
    private static double? Whole(double? value) =>
        value is double v && !double.IsNaN(v) ? Math.Round(v, MidpointRounding.AwayFromZero) : null;
}

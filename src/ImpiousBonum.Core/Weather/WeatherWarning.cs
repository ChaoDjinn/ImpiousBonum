namespace ImpiousBonum.Core.Weather;

/// <summary>Who a place's warnings come from.</summary>
public enum WarningSource
{
    /// <summary>No one yet: warnings are only for the UK and the US so far.</summary>
    None,

    /// <summary>The Met Office, for places in the UK, by area.</summary>
    MetOffice,

    /// <summary>The US National Weather Service, for places in the US, by coordinates.</summary>
    NationalWeatherService,
}

/// <summary>
/// How serious a warning is, on one scale for both agencies, to put the worst first and colour it. A place's warnings
/// all come from one agency, so levels are never compared across them. The NWS's severities are used as they are; the
/// Met Office's yellow ("be aware") is <see cref="Minor"/>, amber ("be prepared") <see cref="Moderate"/> and red ("take
/// action") <see cref="Extreme"/>.
/// </summary>
public enum WarningLevel
{
    /// <summary>The agency didn't say (the NWS's "Unknown", or a Met Office title without a colour).</summary>
    Unknown,
    Minor,
    Moderate,
    Severe,
    Extreme,
}

/// <summary>One warning of severe weather.</summary>
/// <param name="Id">The agency's id for it, the same each time it's fetched.</param>
/// <param name="Headline">What it's about, e.g. "Yellow warning of rain affecting North West England".</param>
/// <param name="Level">For order and colour.</param>
/// <param name="LevelText">The agency's own word for its level, e.g. Yellow or Severe; null when it doesn't say.</param>
/// <param name="Start">When it starts, if known.</param>
/// <param name="End">When it ends, if known; it's dropped after that.</param>
/// <param name="Link">The agency's page about it, to open when it's tapped or clicked.</param>
public sealed record WeatherWarning(
    string Id, string Headline, WarningLevel Level, string? LevelText, DateTimeOffset? Start, DateTimeOffset? End, string? Link, WarningSource Source)
{
    /// <summary>Ids of the earlier versions it updates: the NWS gives each update an id of its own, and an update isn't news.</summary>
    public IReadOnlyList<string> Replaces { get; init; } = [];

    /// <summary>Severe, extreme and red warnings take the theme's critical colour; the rest its warning colour.</summary>
    public bool IsCritical => Level >= WarningLevel.Severe;
}

/// <summary>Where to get warnings: which agency, and the place (for the NWS) or area (for the Met Office).</summary>
/// <param name="Area">For the Met Office, the area's code (see <see cref="MetOfficeWarnings.Areas"/>).</param>
public sealed record WarningsRequest(WarningSource Source, double Latitude, double Longitude, string? Area = null)
{
    /// <summary>Changes when new warnings are needed straight away: another place for the NWS, another area for the Met Office.</summary>
    public string Key => Source == WarningSource.MetOffice
        ? $"{Source}:{Area}"
        : FormattableString.Invariant($"{Source}:{Latitude:0.####},{Longitude:0.####}");
}

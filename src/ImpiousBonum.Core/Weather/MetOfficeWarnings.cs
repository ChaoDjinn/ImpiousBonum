using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ImpiousBonum.Core.News;

namespace ImpiousBonum.Core.Weather;

/// <summary>One of the 16 areas the Met Office's warnings feeds are for.</summary>
/// <param name="Code">The feed's code, e.g. <c>nw</c>.</param>
public sealed record MetOfficeArea(string Code, string Name);

/// <summary>
/// The Met Office's warnings of severe weather, from its regional RSS feeds (metoffice.gov.uk): free, no account or key.
/// Each feed is for one of 16 areas of the UK, so only the area is asked for, never the place. The Met Office asks for
/// its warnings to link to its own pages and to be credited.
/// </summary>
public static partial class MetOfficeWarnings
{
    public const string Attribution = "Warnings from the Met Office (© Crown copyright)";

    /// <summary>Every warning in force, for a warning with no link of its own.</summary>
    public const string WarningsPage = "https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The areas, north to south, as the Met Office lists them.</summary>
    public static IReadOnlyList<MetOfficeArea> Areas { get; } =
    [
        new("os", "Orkney & Shetland"),
        new("he", "Highlands & Eilean Siar"),
        new("gr", "Grampian"),
        new("st", "Strathclyde"),
        new("ta", "Central, Tayside & Fife"),
        new("dg", "Dumfries, Galloway, Lothian & Borders"),
        new("ni", "Northern Ireland"),
        new("wl", "Wales"),
        new("nw", "North West England"),
        new("ne", "North East England"),
        new("yh", "Yorkshire & Humber"),
        new("wm", "West Midlands"),
        new("em", "East Midlands"),
        new("ee", "East of England"),
        new("sw", "South West England"),
        new("se", "London & South East England"),
    ];

    /// <summary>
    /// Towns all over each area, many near its edges, to find a place's area by the nearest of them. One point an area
    /// would put towns near an edge in the wrong one (Wick is nearer Orkney than Inverness).
    /// </summary>
    private static readonly (string Area, double Latitude, double Longitude)[] Towns =
    [
        ("os", 58.98, -2.96), ("os", 58.96, -3.30), ("os", 59.30, -2.98), ("os", 60.15, -1.15), ("os", 59.88, -1.29), ("os", 60.76, -0.85),
        ("he", 57.48, -4.22), ("he", 56.82, -5.11), ("he", 58.44, -3.09), ("he", 58.59, -3.52), ("he", 57.90, -5.16), ("he", 57.41, -6.19),
        ("he", 58.21, -6.39), ("he", 57.45, -7.36), ("he", 57.19, -3.83), ("he", 58.57, -4.75), ("he", 57.59, -3.87), ("he", 57.28, -5.71),
        ("he", 57.00, -7.45), ("he", 58.02, -4.40), ("he", 57.08, -4.05), ("he", 57.00, -5.83), ("he", 57.33, -3.61), ("he", 56.68, -5.10),
        ("gr", 57.15, -2.09), ("gr", 57.65, -3.32), ("gr", 57.51, -1.78), ("gr", 57.69, -2.00), ("gr", 57.67, -2.52), ("gr", 56.96, -2.21),
        ("gr", 57.01, -3.40), ("gr", 57.45, -2.79), ("gr", 57.61, -3.61), ("gr", 57.25, -3.38), ("gr", 57.05, -3.04), ("gr", 57.28, -2.38),
        ("gr", 56.83, -2.47),
        ("st", 55.86, -4.25), ("st", 55.46, -4.63), ("st", 56.41, -5.47), ("st", 55.67, -3.78), ("st", 55.43, -5.61), ("st", 55.95, -4.76),
        ("st", 55.61, -4.50), ("st", 56.62, -6.07), ("st", 55.76, -6.29), ("st", 55.24, -4.86), ("st", 55.95, -4.92), ("st", 56.23, -5.07),
        ("st", 56.00, -4.73), ("st", 55.78, -4.04), ("st", 55.62, -3.53), ("st", 55.58, -5.15), ("st", 56.50, -6.88), ("st", 56.04, -5.43),
        ("st", 55.40, -4.30), ("st", 55.94, -4.56),
        ("ta", 56.12, -3.94), ("ta", 56.40, -3.43), ("ta", 56.46, -2.97), ("ta", 56.07, -3.45), ("ta", 56.11, -3.16), ("ta", 56.34, -2.80),
        ("ta", 56.70, -3.73), ("ta", 56.64, -2.89), ("ta", 56.56, -2.58), ("ta", 56.71, -2.47), ("ta", 56.00, -3.78), ("ta", 56.47, -4.32),
        ("ta", 56.37, -3.84), ("ta", 56.24, -4.21), ("ta", 56.39, -4.62), ("ta", 56.59, -3.34), ("ta", 56.69, -4.58), ("ta", 56.12, -3.79),
        ("ta", 56.85, -3.20), ("ta", 56.19, -4.48),
        ("dg", 55.95, -3.19), ("dg", 55.07, -3.61), ("dg", 54.90, -5.02), ("dg", 55.62, -2.81), ("dg", 55.42, -2.79), ("dg", 55.90, -3.52),
        ("dg", 56.00, -2.52), ("dg", 54.84, -4.05), ("dg", 55.87, -2.09), ("dg", 55.65, -3.19), ("dg", 55.33, -3.44), ("dg", 54.96, -4.48),
        ("dg", 55.60, -2.43), ("dg", 55.12, -3.35), ("dg", 54.99, -3.26), ("dg", 55.37, -3.92), ("dg", 55.15, -3.00), ("dg", 56.06, -2.72),
        ("dg", 55.98, -3.60), ("dg", 55.90, -3.64), ("dg", 55.09, -4.15), ("dg", 55.27, -4.44),
        ("ni", 54.60, -5.93), ("ni", 55.00, -7.31), ("ni", 54.34, -7.64), ("ni", 54.60, -7.30), ("ni", 54.18, -6.34), ("ni", 54.86, -6.28),
        ("ni", 54.35, -6.65), ("ni", 55.13, -6.67), ("ni", 55.20, -6.24), ("ni", 54.85, -5.82), ("ni", 54.33, -5.72), ("ni", 54.50, -6.77),
        ("ni", 54.64, -6.74), ("ni", 54.83, -7.46),
        ("wl", 51.48, -3.18), ("wl", 51.62, -3.94), ("wl", 52.42, -4.08), ("wl", 53.23, -4.13), ("wl", 53.05, -3.00), ("wl", 52.51, -3.31),
        ("wl", 51.86, -4.31), ("wl", 51.80, -4.97), ("wl", 51.95, -3.39), ("wl", 53.32, -3.83), ("wl", 53.31, -4.63), ("wl", 52.74, -3.88),
        ("wl", 51.58, -2.99), ("wl", 52.24, -3.38), ("wl", 51.81, -2.72), ("wl", 51.82, -3.02), ("wl", 52.66, -3.15), ("wl", 52.91, -3.60),
        ("wl", 52.89, -4.42), ("wl", 52.08, -4.66), ("wl", 53.32, -3.49), ("wl", 53.17, -3.14), ("wl", 51.64, -2.67), ("wl", 52.34, -3.05),
        ("wl", 52.07, -3.13), ("wl", 51.50, -3.58), ("wl", 51.75, -3.38), ("wl", 52.59, -3.85), ("wl", 53.11, -3.31), ("wl", 52.96, -3.17),
        ("nw", 53.48, -2.24), ("nw", 53.41, -2.98), ("nw", 53.76, -2.70), ("nw", 54.89, -2.93), ("nw", 54.33, -2.75), ("nw", 53.19, -2.89),
        ("nw", 53.82, -3.05), ("nw", 54.11, -3.23), ("nw", 54.60, -3.13), ("nw", 53.10, -2.44), ("nw", 54.05, -2.80), ("nw", 53.79, -2.25),
        ("nw", 54.55, -3.59), ("nw", 53.26, -2.13), ("nw", 54.66, -2.75), ("nw", 53.65, -3.01), ("nw", 53.58, -2.43), ("nw", 53.54, -2.12),
        ("nw", 53.87, -2.39), ("nw", 54.81, -2.44), ("nw", 54.47, -2.35), ("nw", 54.32, -2.53), ("nw", 53.39, -2.59), ("nw", 53.07, -2.52),
        ("nw", 53.41, -2.15), ("nw", 53.48, -2.06), ("nw", 53.92, -2.19),
        ("ne", 54.98, -1.62), ("ne", 54.78, -1.57), ("ne", 54.57, -1.23), ("ne", 55.77, -2.01), ("ne", 54.97, -2.10), ("ne", 55.41, -1.71),
        ("ne", 54.52, -1.55), ("ne", 54.91, -1.38), ("ne", 54.69, -1.21), ("ne", 55.17, -1.69), ("ne", 55.14, -2.25), ("ne", 55.55, -2.01),
        ("ne", 54.54, -1.92), ("ne", 54.75, -2.01), ("ne", 54.97, -2.46), ("ne", 54.54, -1.06), ("ne", 54.58, -0.97), ("ne", 55.23, -2.58),
        ("yh", 53.80, -1.55), ("yh", 53.38, -1.47), ("yh", 53.96, -1.08), ("yh", 53.74, -0.33), ("yh", 54.28, -0.40), ("yh", 53.99, -1.54),
        ("yh", 53.96, -2.02), ("yh", 54.34, -1.43), ("yh", 53.57, -0.08), ("yh", 53.79, -1.75), ("yh", 53.52, -1.13), ("yh", 54.49, -0.61),
        ("yh", 54.30, -2.20), ("yh", 53.65, -1.78), ("yh", 53.55, -1.48), ("yh", 53.59, -0.65), ("yh", 54.08, -0.19), ("yh", 54.13, -0.80),
        ("yh", 54.40, -1.74), ("yh", 54.07, -2.28), ("yh", 53.72, -1.86), ("yh", 53.70, -0.88), ("yh", 53.74, -2.01), ("yh", 53.53, -1.63),
        ("yh", 53.43, -1.36), ("yh", 54.45, -1.20),
        ("wm", 52.48, -1.90), ("wm", 52.41, -1.51), ("wm", 52.59, -2.13), ("wm", 53.00, -2.18), ("wm", 52.71, -2.75), ("wm", 52.06, -2.72),
        ("wm", 52.19, -2.22), ("wm", 52.81, -2.12), ("wm", 52.28, -1.58), ("wm", 52.37, -2.72), ("wm", 53.10, -2.02), ("wm", 52.86, -3.05),
        ("wm", 52.68, -2.45), ("wm", 52.39, -2.25), ("wm", 52.09, -1.95), ("wm", 52.19, -1.71), ("wm", 52.80, -1.64), ("wm", 52.63, -1.69),
        ("wm", 52.52, -1.47), ("wm", 52.37, -1.26), ("wm", 52.04, -2.42), ("wm", 52.90, -2.48), ("wm", 52.97, -2.68), ("wm", 52.90, -1.86),
        ("wm", 52.49, -3.00), ("wm", 52.20, -3.03), ("wm", 51.91, -2.59), ("wm", 52.06, -1.62),
        ("em", 52.95, -1.15), ("em", 52.92, -1.48), ("em", 52.64, -1.13), ("em", 53.23, -0.54), ("em", 52.24, -0.90), ("em", 53.26, -1.91),
        ("em", 53.14, 0.34), ("em", 52.98, -0.03), ("em", 52.40, -0.73), ("em", 53.24, -1.42), ("em", 52.91, -0.64), ("em", 52.67, -0.73),
        ("em", 53.37, 0.00), ("em", 53.44, -1.95), ("em", 53.14, -1.56), ("em", 53.02, -1.73), ("em", 52.77, -1.21), ("em", 53.14, -1.20),
        ("em", 53.08, -0.81), ("em", 53.30, -1.12), ("em", 53.40, -0.77), ("em", 52.65, -0.48), ("em", 52.79, -0.15), ("em", 52.26, -1.16),
        ("em", 52.03, -1.15), ("em", 52.48, -0.92), ("em", 52.54, -1.37), ("em", 53.21, -1.68), ("em", 53.34, 0.26), ("em", 52.13, -0.99),
        ("em", 52.48, -0.47), ("em", 53.45, -0.13),
        ("ee", 52.63, 1.30), ("ee", 52.21, 0.12), ("ee", 52.06, 1.16), ("ee", 51.74, 0.47), ("ee", 52.57, -0.24), ("ee", 51.88, -0.42),
        ("ee", 52.14, -0.47), ("ee", 52.75, 0.40), ("ee", 52.61, 1.73), ("ee", 51.89, 0.90), ("ee", 51.75, -0.34), ("ee", 51.54, 0.71),
        ("ee", 52.93, 1.30), ("ee", 51.90, -0.20), ("ee", 52.33, -0.18), ("ee", 52.25, 0.71), ("ee", 52.48, 1.75), ("ee", 52.41, 0.75),
        ("ee", 52.40, 0.26), ("ee", 52.67, 0.16), ("ee", 51.77, 0.10), ("ee", 51.66, -0.40), ("ee", 51.80, -0.08), ("ee", 51.79, 1.16),
        ("ee", 51.58, 0.49), ("ee", 51.62, 0.30), ("ee", 52.05, -0.02), ("ee", 51.92, -0.66), ("ee", 52.09, -0.26), ("ee", 52.02, 0.24),
        ("ee", 52.94, 0.49), ("ee", 51.79, -0.66), ("ee", 51.48, 0.33), ("ee", 51.70, 0.11), ("ee", 51.96, 1.35), ("ee", 52.23, -0.27),
        ("sw", 51.45, -2.59), ("sw", 50.72, -3.53), ("sw", 50.38, -4.14), ("sw", 50.26, -5.05), ("sw", 50.12, -5.54), ("sw", 51.38, -2.36),
        ("sw", 51.86, -2.24), ("sw", 51.02, -3.10), ("sw", 50.72, -1.88), ("sw", 50.71, -2.44), ("sw", 51.56, -1.78), ("sw", 51.07, -1.79),
        ("sw", 51.08, -4.06), ("sw", 51.90, -2.08), ("sw", 50.61, -2.46), ("sw", 50.83, -4.54), ("sw", 51.72, -1.97), ("sw", 50.94, -2.63),
        ("sw", 50.46, -3.53), ("sw", 50.47, -4.72), ("sw", 50.74, -4.00), ("sw", 51.20, -3.48), ("sw", 51.35, -2.98), ("sw", 51.21, -2.65),
        ("sw", 51.46, -2.12), ("sw", 51.42, -1.73), ("sw", 51.01, -2.20), ("sw", 50.72, -1.98), ("sw", 50.73, -1.78), ("sw", 51.99, -2.16),
        ("sw", 51.75, -2.22), ("sw", 51.99, -1.70), ("sw", 51.64, -2.16), ("sw", 50.90, -3.49), ("sw", 50.80, -3.19), ("sw", 50.73, -2.94),
        ("sw", 51.35, -1.99), ("sw", 51.79, -2.62), ("sw", 50.64, -4.36), ("sw", 49.92, -6.30), ("sw", 51.21, -4.12), ("sw", 51.20, -2.18),
        ("se", 51.51, -0.13), ("se", 50.82, -0.14), ("se", 50.90, -1.40), ("se", 51.75, -1.26), ("se", 51.45, -0.97), ("se", 51.28, 1.08),
        ("se", 51.24, -0.57), ("se", 50.82, -1.09), ("se", 51.27, 0.52), ("se", 52.04, -0.76), ("se", 51.13, 1.31), ("se", 50.70, -1.29),
        ("se", 51.82, -0.81), ("se", 50.86, 0.57), ("se", 52.06, -1.34), ("se", 51.06, -1.31), ("se", 50.84, -0.78), ("se", 51.27, -1.09),
        ("se", 51.40, -1.32), ("se", 51.63, -0.75), ("se", 51.11, -0.19), ("se", 50.77, 0.28), ("se", 51.15, 0.87), ("se", 51.13, 0.26),
        ("se", 51.39, 1.38), ("se", 50.76, -1.54), ("se", 51.21, -1.48), ("se", 51.00, -0.94), ("se", 51.67, -1.28), ("se", 51.79, -1.48),
        ("se", 51.94, -1.55), ("se", 51.90, -1.15), ("se", 52.00, -0.99), ("se", 52.15, -0.70), ("se", 51.67, -0.61), ("se", 51.42, -0.75),
        ("se", 51.37, -0.10), ("se", 51.65, -0.08), ("se", 51.55, -0.48), ("se", 51.58, 0.18), ("se", 50.85, -1.79), ("se", 50.93, -1.79),
        ("se", 51.66, -1.59), ("se", 51.54, -0.90), ("se", 51.41, -1.52), ("se", 51.81, -1.64), ("se", 50.95, 0.73), ("se", 51.44, 0.76),
    ];

    /// <summary>The area with this code, or null.</summary>
    public static MetOfficeArea? Find(string? code) => Areas.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>The area a place in the UK is in: that of the nearest of <see cref="Towns"/>.</summary>
    public static MetOfficeArea AreaFor(double latitude, double longitude)
    {
        // Flat-earth distances are near enough across the UK, with degrees of longitude shortened for the latitude.
        var shrink = Math.Cos(latitude * Math.PI / 180);
        var nearest = Towns.MinBy(t => Math.Pow(t.Latitude - latitude, 2) + Math.Pow((t.Longitude - longitude) * shrink, 2));
        return Find(nearest.Area)!;
    }

    /// <summary>Gets an area's feed. Throws <see cref="HttpRequestException"/> and the like on failure.</summary>
    public static Task<string> GetAsync(string url, CancellationToken cancellationToken) => Http.GetStringAsync(url, cancellationToken);

    internal static string FeedUrl(string area) => $"https://www.metoffice.gov.uk/public/data/PWSCache/WarningsRSS/Region/{area}";

    /// <summary>
    /// The warnings in an area's feed, in its order: an empty feed has none. Each is read tolerantly: its level from the
    /// colour in its title, the title as its headline, its link as it is, and when it's valid if the description says
    /// ("valid from 0600 Tue 02 Jan to 2359 Tue 02 Jan", in UK time). Throws <see cref="FormatException"/> if it isn't a
    /// feed. Like news feeds, DTDs are refused.
    /// </summary>
    /// <param name="now">For the year, which the times leave out, when the feed has no date.</param>
    public static IReadOnlyList<WeatherWarning> Parse(string xml, DateTimeOffset now)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 5_000_000,
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"it isn't a warnings feed ({ex.Message})", ex);
        }

        if (document.Root is not { Name.LocalName: "rss" } root || root.Element("channel") is not { } channel)
            throw new FormatException("it isn't a warnings feed");
        var published = Feeds.ParseDate(channel.Element("pubDate")?.Value) ?? now;

        var warnings = new List<WeatherWarning>();
        foreach (var item in channel.Elements("item"))
        {
            var title = Feeds.Clean(item.Element("title")?.Value ?? string.Empty);
            if (title.Length == 0)
                continue;
            var link = Feeds.WebLink(item.Element("link")?.Value);
            var colour = Colour().Match(title);
            var (start, end) = Validity(Feeds.Clean(item.Element("description")?.Value ?? string.Empty), Feeds.ParseDate(item.Element("pubDate")?.Value) ?? published);
            warnings.Add(new WeatherWarning(
                NonEmpty(item.Element("guid")?.Value) ?? link ?? title,
                title,
                colour.Success ? Level(colour.Value) : WarningLevel.Unknown,
                colour.Success ? char.ToUpperInvariant(colour.Value[0]) + colour.Value[1..].ToLowerInvariant() : null,
                start,
                end,
                link ?? WarningsPage,
                WarningSource.MetOffice));
        }
        return warnings;
    }

    /// <summary>Yellow is "be aware", amber "be prepared" and red "take action" (see <see cref="WarningLevel"/>).</summary>
    internal static WarningLevel Level(string colour) => colour.ToLowerInvariant() switch
    {
        "red" => WarningLevel.Extreme,
        "amber" => WarningLevel.Moderate,
        "yellow" => WarningLevel.Minor,
        _ => WarningLevel.Unknown,
    };

    /// <summary>
    /// "valid from 0600 Tue 02 Jan to 2359 Tue 02 Jan" as times, in UK time. The year is the one that puts the start
    /// nearest <paramref name="published"/>, so a warning out in December for January is next year's. Nulls if it isn't there.
    /// </summary>
    internal static (DateTimeOffset? Start, DateTimeOffset? End) Validity(string description, DateTimeOffset published)
    {
        var match = ValidFrom().Match(description);
        if (!match.Success)
            return (null, null);
        var start = Date(match.Groups["from"].Value, match.Groups["fromDay"].Value, match.Groups["fromMonth"].Value, published.UtcDateTime);
        if (start is null)
            return (null, null);
        // The end is a few days after the start at most, so it's in the year that puts it nearest that.
        var end = Date(match.Groups["to"].Value, match.Groups["toDay"].Value, match.Groups["toMonth"].Value, start.Value);
        return (UkTime(start.Value), end is null ? null : UkTime(end.Value));
    }

    /// <summary>A day and time without a year, in the year that puts it nearest <paramref name="near"/>.</summary>
    private static DateTime? Date(string time, string day, string month, DateTime near)
    {
        if (!int.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var hhmm) || hhmm / 100 > 23 || hhmm % 100 > 59
            || !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dayOfMonth)
            || !DateTime.TryParseExact(month[..Math.Min(3, month.Length)], "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthOnly))
        {
            return null;
        }
        DateTime? best = null;
        for (var year = near.Year - 1; year <= near.Year + 1; year++)
        {
            if (dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, monthOnly.Month))
                continue;
            var candidate = new DateTime(year, monthOnly.Month, dayOfMonth, hhmm / 100, hhmm % 100, 0);
            if (best is null || Math.Abs((candidate - near).Ticks) < Math.Abs((best.Value - near).Ticks))
                best = candidate;
        }
        return best;
    }

    private static readonly TimeZoneInfo? UkZone = FindUkZone();

    /// <summary>A time as on UK clocks: GMT in winter, BST in summer. UTC if Windows doesn't know the zone.</summary>
    private static DateTimeOffset UkTime(DateTime local) =>
        new(local, UkZone?.GetUtcOffset(local) ?? TimeSpan.Zero);

    private static TimeZoneInfo? FindUkZone()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Try the next name.
            }
        }
        return null;
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [GeneratedRegex(@"\b(yellow|amber|red)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Colour();

    [GeneratedRegex(@"valid from (?<from>\d{4}) [A-Za-z]+ (?<fromDay>\d{1,2}) (?<fromMonth>[A-Za-z]{3,}) to (?<to>\d{4}) [A-Za-z]+ (?<toDay>\d{1,2}) (?<toMonth>[A-Za-z]{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex ValidFrom();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}

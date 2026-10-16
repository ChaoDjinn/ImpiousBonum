using System.Globalization;

namespace ImpiousBonum.Core.Weather;

/// <summary>How heavy rain is, in the WMO's bands for an hour's rain.</summary>
public enum RainIntensity
{
    Dry,

    /// <summary>Up to 2.5 mm an hour.</summary>
    Light,

    /// <summary>From 2.5 to 10 mm an hour.</summary>
    Moderate,

    /// <summary>More than 10 mm an hour.</summary>
    Heavy,
}

/// <summary>
/// The rain in the next two hours, a quarter of an hour at a time, from the forecast's
/// <see cref="WeatherReport.QuarterHours"/>: what the rain widget draws, and the <c>weather.rain.soon</c> and
/// <c>weather.rain.next</c> readings. It says snow rather than rain when the forecast for that hour is snow.
/// </summary>
/// <param name="Steps">
/// The quarter-hour it is now and those after it, up to <see cref="Length"/>; fewer when the forecast doesn't reach that
/// far, as an old one may not.
/// </param>
/// <param name="Summary">
/// "Dry for the next 2 hours", "Rain from 14:30", "Rain stopping around 15:15" or "Light rain for the next 2 hours", in
/// the place's own time.
/// </param>
/// <param name="MinutesToRain">How long until it rains: 0 while it is, and null while it stays dry.</param>
public sealed record RainAhead(IReadOnlyList<QuarterHour> Steps, string Summary, int? MinutesToRain)
{
    /// <summary>Two hours of quarter-hours.</summary>
    public const int Length = 8;

    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Less than this in a quarter of an hour, in mm, counts as dry. Open-Meteo gives tenths of a millimetre (or
    /// thousandths of an inch), so it's anything at all.
    /// </summary>
    private const double WetFrom = 0.05;

    private const double MillimetresPerInch = 25.4;

    /// <summary>
    /// The rain ahead from <paramref name="now"/>, the time at the place (see <see cref="WeatherReport.LocalTime"/>), or
    /// null when the forecast has no quarter-hours from then.
    /// </summary>
    public static RainAhead? For(WeatherReport report, DateTime now)
    {
        // The quarter-hour it is now, then each one after it up to a gap.
        var steps = new List<QuarterHour>();
        foreach (var step in report.QuarterHours)
        {
            if (step.Time <= now)
                continue;
            if (step.Time - (steps.Count == 0 ? now : steps[^1].Time) > Step)
                break;
            steps.Add(step);
            if (steps.Count == Length)
                break;
        }
        if (steps.Count == 0)
            return null;

        var wet = steps.Select(s => Intensity(s.Precipitation, report.Units) != RainIntensity.Dry).ToList();
        var starts = wet.IndexOf(true);
        if (starts < 0)
            return new(steps, $"Dry for the next {Span(steps.Count)}", null);

        var snow = Snow(report, steps[starts]);
        if (starts > 0)
        {
            var start = steps[starts].Time - Step;
            // Rounded up, so it never says 0 before it's here.
            return new(steps, $"{(snow ? "Snow" : "Rain")} from {Clock(start)}", (int)Math.Ceiling((start - now).TotalMinutes));
        }
        var stops = wet.IndexOf(false);
        if (stops > 0)
            return new(steps, $"{(snow ? "Snow" : "Rain")} stopping around {Clock(steps[stops].Time - Step)}", 0);
        // Raining all the way: how hard, at its heaviest.
        var heaviest = steps.Max(s => Intensity(s.Precipitation, report.Units));
        return new(steps, $"{heaviest} {(snow ? "snow" : "rain")} for the next {Span(steps.Count)}", 0);
    }

    /// <summary>How heavy <paramref name="amount"/> in a quarter of an hour, in the forecast's units, is.</summary>
    public static RainIntensity Intensity(double amount, WeatherUnits units) => MillimetresPerHour(amount, units) switch
    {
        < WetFrom * 4 => RainIntensity.Dry,
        < 2.5 => RainIntensity.Light,
        < 10 => RainIntensity.Moderate,
        _ => RainIntensity.Heavy,
    };

    /// <summary>How fast <paramref name="amount"/> in a quarter of an hour, in the forecast's units, falls: in mm an hour.</summary>
    public static double MillimetresPerHour(double amount, WeatherUnits units) =>
        amount * (units == WeatherUnits.Imperial ? MillimetresPerInch : 1) * 4;

    /// <summary>Whether the forecast for the hour <paramref name="step"/> is in is snow.</summary>
    private static bool Snow(WeatherReport report, QuarterHour step) =>
        report.HourEndingAfter(step.Time - Step) is { } hour && WeatherCodes.Icon(hour.Code, isDay: true) == "snow";

    /// <summary>"2 hours", "hour" or "45 minutes": how far <paramref name="steps"/> quarter-hours reach.</summary>
    private static string Span(int steps) => (steps * 15) switch
    {
        60 => "hour",
        var minutes when minutes % 60 == 0 => string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} hours"),
        var minutes => string.Create(CultureInfo.CurrentCulture, $"{minutes} minutes"),
    };

    /// <summary>"14:30", or "2:30 PM", as Windows shows times.</summary>
    private static string Clock(DateTime time) => time.ToString("t", CultureInfo.CurrentCulture);
}

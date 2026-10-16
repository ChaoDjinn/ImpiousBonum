namespace ImpiousBonum.Core.Weather;

/// <summary>
/// The sun's day and the moon at a moment, worked out on the PC for the sun and moon widget (see
/// <see cref="Providers.MoonProvider"/>). The times are moments; the widget shows them in the PC's time.
/// </summary>
/// <param name="Time">When it was worked out.</param>
/// <param name="Sun">Today's sun at the weather's location, or null without a location.</param>
/// <param name="Latitude">The weather location's latitude, for which way up the moon looks from there; null without one.</param>
public sealed record SkyReport(DateTimeOffset Time, MoonReport Moon, SunDay? Sun, double? Latitude)
{
    /// <summary>Whether the moon is seen from the southern hemisphere, where it looks upside down: lit on the left as it waxes.</summary>
    public bool Southern => Latitude < 0;
}

/// <summary>The moon at a moment.</summary>
/// <param name="Illumination">How much of its face is lit, from 0 at new moon to 1 at full.</param>
/// <param name="Waxing">Whether it's growing, from new moon to full.</param>
/// <param name="Age">Days since the new moon.</param>
/// <param name="Phase">Its phase in words, e.g. Waxing gibbous.</param>
/// <param name="NextFull">When the next full moon is.</param>
/// <param name="NextNew">When the next new moon is.</param>
public sealed record MoonReport(double Illumination, bool Waxing, double Age, string Phase, DateTimeOffset NextFull, DateTimeOffset NextNew)
{
    /// <summary>When it rose, while it's up, or when it next rises; null without a location, or when it doesn't within a day or so.</summary>
    public DateTimeOffset? Rise { get; init; }

    /// <summary>When it sets after <see cref="Rise"/>; null without a location, or when it doesn't within a day or so.</summary>
    public DateTimeOffset? Set { get; init; }
}

/// <summary>The sun's day at a place, from midnight to midnight in the PC's time.</summary>
/// <param name="Start">Midnight at the start of the day.</param>
/// <param name="End">Midnight at the end of it: usually a day later, but an hour more or less when the clocks change.</param>
/// <param name="Rise">Sunrise, or null on a day it doesn't rise: in the far north and south it can stay up or down all day.</param>
/// <param name="Set">Sunset, or null on a day it doesn't set.</param>
/// <param name="Daylight">How long the sun is up that day.</param>
public sealed record SunDay(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset? Rise, DateTimeOffset? Set, TimeSpan Daylight)
{
    /// <summary>
    /// The sun's day at a place: its sunrise and sunset between <paramref name="start"/> and <paramref name="end"/>,
    /// and how long it's up in between.
    /// </summary>
    public static SunDay At(double latitude, double longitude, DateTimeOffset start, DateTimeOffset end)
    {
        var (rise, set) = SunPosition.RiseAndSet(latitude, longitude, start, end);
        var daylight = (rise, set) switch
        {
            ({ } up, { } down) when up < down => down - up,
            // Up at midnight, down for a while, then up again by the end of the day.
            ({ } up, { } down) => down - start + (end - up),
            ({ } up, null) => end - up,
            (null, { } down) => down - start,
            // No sunrise or sunset: up all day, or down all day.
            _ => SunPosition.IsDown(latitude, longitude, start + (end - start) / 2) ? TimeSpan.Zero : end - start,
        };
        return new SunDay(start, end, rise, set, daylight);
    }
}

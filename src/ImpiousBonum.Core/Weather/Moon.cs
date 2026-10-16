namespace ImpiousBonum.Core.Weather;

/// <summary>The moon's four main phases, in the order they come round.</summary>
public enum MoonPhase
{
    New,
    FirstQuarter,
    Full,
    LastQuarter,
}

/// <summary>
/// The moon, worked out on the PC from the time alone (and for when it rises and sets, the place) with the formulas in
/// Jean Meeus's <i>Astronomical Algorithms</i>, so nothing is fetched. Where it is comes to within a few hundredths of a
/// degree, so its phases come within a few minutes of the almanacs', and its rising and setting within a couple, for
/// centuries either side of 2000.
/// </summary>
public static class Moon
{
    /// <summary>The average time from one new moon to the next, in days.</summary>
    public const double SynodicMonth = 29.530588861;

    /// <summary>The Earth's radius at the equator, and its distance from the sun, in km.</summary>
    private const double EarthRadius = 6378.14, AstronomicalUnit = 149_597_870.7;

    /// <summary>
    /// How far round the moon is from the sun, seen from the Earth, in degrees eastwards along the ecliptic: 0 at new
    /// moon, 90 at first quarter, 180 at full moon and 270 at last quarter. Below 180 it's waxing.
    /// </summary>
    public static double Elongation(DateTimeOffset time)
    {
        var t = SunPosition.Centuries(time);
        return Mod(Apparent(Position(t).Longitude, t) - SunPosition.Longitude(time), 360);
    }

    /// <summary>How much of the moon's face is lit, from 0 at new moon to 1 at full moon.</summary>
    public static double Illumination(DateTimeOffset time)
    {
        var t = SunPosition.Centuries(time);
        var (longitude, latitude, distance) = Position(t);
        // The angle at the moon between the sun and the Earth; the sun is so far away it hardly matters exactly how far.
        var cosElongation = Cos(latitude) * Cos(Apparent(longitude, t) - SunPosition.Longitude(time));
        var sinElongation = Math.Sqrt(Math.Max(0, 1 - cosElongation * cosElongation));
        var phaseAngle = Math.Atan2(AstronomicalUnit * sinElongation, distance - AstronomicalUnit * cosElongation);
        return (1 + Math.Cos(phaseAngle)) / 2;
    }

    /// <summary>How many days it is since the last new moon: 0 to about 29.5.</summary>
    public static double Age(DateTimeOffset time) => (time - Previous(MoonPhase.New, time)).TotalDays;

    /// <summary>When <paramref name="phase"/> next comes, after <paramref name="after"/>.</summary>
    public static DateTimeOffset Next(MoonPhase phase, DateTimeOffset after)
    {
        var ahead = Mod(Target(phase) - Elongation(after), 360);
        var time = Find(phase, after, ahead);
        // One that's only just come can come out a few milliseconds before it; the next is a month on.
        return time > after ? time : Find(phase, after, ahead + 360);
    }

    /// <summary>When <paramref name="phase"/> last came, at or before <paramref name="before"/>.</summary>
    public static DateTimeOffset Previous(MoonPhase phase, DateTimeOffset before)
    {
        var time = Find(phase, before, Mod(Target(phase) - Elongation(before), 360) - 360);
        // One that's coming now can come out a few milliseconds after it.
        return time < before ? time : before;
    }

    /// <summary>
    /// The phase's name at <paramref name="time"/>, on the day from <paramref name="dayStart"/> to <paramref name="dayEnd"/>:
    /// New moon, First quarter, Full moon or Last quarter all through the day it comes, as calendars and almanacs have
    /// them, and in between Waxing crescent, Waxing gibbous, Waning gibbous or Waning crescent.
    /// </summary>
    public static string PhaseName(DateTimeOffset time, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        // The next of the four after the day starts is the only one that can come that day.
        var next = (MoonPhase)((int)Math.Floor(Elongation(dayStart) / 90 + 1) % 4);
        if (Next(next, dayStart) < dayEnd)
            return Name(next);
        return Elongation(time) switch
        {
            < 90 => "Waxing crescent",
            < 180 => "Waxing gibbous",
            < 270 => "Waning gibbous",
            _ => "Waning crescent",
        };
    }

    /// <summary>"New moon", "First quarter", "Full moon" or "Last quarter".</summary>
    public static string Name(MoonPhase phase) => phase switch
    {
        MoonPhase.New => "New moon",
        MoonPhase.FirstQuarter => "First quarter",
        MoonPhase.Full => "Full moon",
        _ => "Last quarter",
    };

    /// <summary>
    /// The first moonrise and the first moonset at a place between <paramref name="from"/> and <paramref name="to"/>,
    /// such as a day's. Either is null when it doesn't happen then: about once a month there's a day with no moonrise,
    /// and another with no moonset, and in the far north and south the moon can stay up or down for days.
    /// </summary>
    public static (DateTimeOffset? Rise, DateTimeOffset? Set) RiseAndSet(double latitude, double longitude, DateTimeOffset from, DateTimeOffset to) =>
        Horizon.FirstRiseAndSet(Height(latitude, longitude), from, to);

    /// <summary>
    /// The moon's time in the sky at a place: if it's up at <paramref name="time"/>, when it rose and when it'll set; if
    /// it's down, when it next rises and then sets. Either is null when it doesn't happen within a day or so, as in the
    /// far north and south.
    /// </summary>
    public static (DateTimeOffset? Rise, DateTimeOffset? Set) RiseAndSetAround(double latitude, double longitude, DateTimeOffset time)
    {
        // Longer than the moon's day of 24 hours and 50 minutes, so a rise or a set within it is the one wanted.
        var window = TimeSpan.FromHours(30);
        var height = Height(latitude, longitude);
        if (height(time) >= 0)
        {
            var rose = Horizon.Crossings(height, time - window, time).Where(c => c.Rising).Select(c => (DateTimeOffset?)c.Time).LastOrDefault();
            return (rose, Horizon.FirstRiseAndSet(height, time, time + window).Set);
        }
        var (rise, _) = Horizon.FirstRiseAndSet(height, time, time + window);
        return rise is { } risen ? (rise, Horizon.FirstRiseAndSet(height, risen, risen + window).Set) : (null, null);
    }

    /// <summary>
    /// How far the moon's centre is above where it counts as risen at a place, in degrees, seen from the Earth's centre.
    /// It's risen when its top edge shows over the horizon: the air lifts it about 0.57°, and it's about 0.25° across,
    /// but it's so close that from the Earth's surface it looks lower than from its centre, by up to about a degree.
    /// </summary>
    private static Func<DateTimeOffset, double> Height(double latitude, double longitude) => time =>
    {
        var t = SunPosition.Centuries(time);
        var (eclipticLongitude, eclipticLatitude, distance) = Position(t);
        var parallax = Degrees(Math.Asin(EarthRadius / distance));
        var (rightAscension, declination) = Equatorial(Apparent(eclipticLongitude, t), eclipticLatitude, t);
        var days = t * 36525;
        var sidereal = 280.46061837 + 360.98564736629 * days + t * t * (0.000387933 - t / 38710000);
        var hourAngle = sidereal + longitude - rightAscension;
        var elevation = Degrees(Math.Asin(Math.Clamp(Sin(latitude) * Sin(declination) + Cos(latitude) * Cos(declination) * Cos(hourAngle), -1, 1)));
        return elevation - (0.7275 * parallax - 0.5667);
    };

    /// <summary>Where the elongation is at <paramref name="phase"/>.</summary>
    private static double Target(MoonPhase phase) => (int)phase * 90.0;

    /// <summary>
    /// When the moon reaches <paramref name="phase"/>, starting <paramref name="degrees"/> of elongation on from
    /// <paramref name="from"/> (backwards when it's negative) at its average pace, then homing in on it: the moon's pace
    /// varies by a fifth either way, so a few goes bring it to well within a second.
    /// </summary>
    private static DateTimeOffset Find(MoonPhase phase, DateTimeOffset from, double degrees)
    {
        var time = from.AddDays(degrees / 360 * SynodicMonth);
        for (var i = 0; i < 12; i++)
        {
            var toGo = Mod(Target(phase) - Elongation(time) + 180, 360) - 180;
            time = time.AddDays(toGo / 360 * SynodicMonth);
            if (Math.Abs(toGo) < 1e-6)
                break;
        }
        return time;
    }

    /// <summary>
    /// The moon's geometric longitude and latitude on the ecliptic of the date (degrees) and its distance (km) at
    /// <paramref name="t"/> Julian centuries since 2000: Meeus's chapter 47, from ELP-2000/82, good to about 10″.
    /// </summary>
    internal static (double Longitude, double Latitude, double Distance) Position(double t)
    {
        var meanLongitude = 218.3164477 + t * (481267.88123421 + t * (-0.0015786 + t * (1 / 538841.0 - t / 65194000)));
        var elongation = 297.8501921 + t * (445267.1114034 + t * (-0.0018819 + t * (1 / 545868.0 - t / 113065000)));
        var sunAnomaly = 357.5291092 + t * (35999.0502909 + t * (-0.0001536 + t / 24490000));
        var moonAnomaly = 134.9633964 + t * (477198.8675055 + t * (0.0087414 + t * (1 / 69699.0 - t / 14712000)));
        var node = 93.2720950 + t * (483202.0175233 + t * (-0.0036539 + t * (-1 / 3526000.0 + t / 863310000)));
        // The Earth's orbit is getting rounder, which weakens the terms that depend on the sun's anomaly.
        var eccentricity = 1 - t * (0.002516 + t * 0.0000074);
        var venus = 119.75 + 131.849 * t;
        var jupiter = 53.09 + 479264.290 * t;
        var flattening = 313.45 + 481266.484 * t;

        double longitude = 0, distance = 0, latitude = 0;
        foreach (var (d, m, mm, f, l, r) in LongitudeAndDistance)
        {
            var angle = d * elongation + m * sunAnomaly + mm * moonAnomaly + f * node;
            var weight = Math.Pow(eccentricity, Math.Abs(m));
            longitude += l * weight * Sin(angle);
            distance += r * weight * Cos(angle);
        }
        foreach (var (d, m, mm, f, b) in Latitude)
            latitude += b * Math.Pow(eccentricity, Math.Abs(m)) * Sin(d * elongation + m * sunAnomaly + mm * moonAnomaly + f * node);

        // Venus and Jupiter pull on it a little too, and the Earth isn't quite round.
        longitude += 3958 * Sin(venus) + 1962 * Sin(meanLongitude - node) + 318 * Sin(jupiter);
        latitude += -2235 * Sin(meanLongitude) + 382 * Sin(flattening) + 175 * Sin(venus - node) + 175 * Sin(venus + node)
            + 127 * Sin(meanLongitude - moonAnomaly) - 115 * Sin(meanLongitude + moonAnomaly);
        return (Mod(meanLongitude + longitude / 1e6, 360), latitude / 1e6, 385000.56 + distance / 1000);
    }

    /// <summary>A longitude on the ecliptic as it's seen, nodding with the Earth's axis (nutation), as the sun's is.</summary>
    private static double Apparent(double longitude, double t) => longitude - 0.00478 * Sin(125.04 - 1934.136 * t);

    /// <summary>A place on the ecliptic as right ascension and declination, in degrees.</summary>
    private static (double RightAscension, double Declination) Equatorial(double longitude, double latitude, double t)
    {
        var obliquity = 23.4392911 - 0.0130042 * t + 0.00256 * Cos(125.04 - 1934.136 * t);
        var rightAscension = Degrees(Math.Atan2(Sin(longitude) * Cos(obliquity) - Math.Tan(Radians(latitude)) * Sin(obliquity), Cos(longitude)));
        var declination = Degrees(Math.Asin(Sin(latitude) * Cos(obliquity) + Cos(latitude) * Sin(obliquity) * Sin(longitude)));
        return (rightAscension, declination);
    }

    /// <summary>
    /// Meeus's table 47.A: how many of the sun's and moon's mean elongation (D), the sun's and moon's mean anomalies (M,
    /// M′) and the moon's argument of latitude (F) make each term, and its size in longitude (millionths of a degree,
    /// with sine) and distance (metres, with cosine).
    /// </summary>
    private static readonly (int D, int M, int MM, int F, double L, double R)[] LongitudeAndDistance =
    [
        (0, 0, 1, 0, 6288774, -20905355), (2, 0, -1, 0, 1274027, -3699111), (2, 0, 0, 0, 658314, -2955968),
        (0, 0, 2, 0, 213618, -569925), (0, 1, 0, 0, -185116, 48888), (0, 0, 0, 2, -114332, -3149),
        (2, 0, -2, 0, 58793, 246158), (2, -1, -1, 0, 57066, -152138), (2, 0, 1, 0, 53322, -170733),
        (2, -1, 0, 0, 45758, -204586), (0, 1, -1, 0, -40923, -129620), (1, 0, 0, 0, -34720, 108743),
        (0, 1, 1, 0, -30383, 104755), (2, 0, 0, -2, 15327, 10321), (0, 0, 1, 2, -12528, 0),
        (0, 0, 1, -2, 10980, 79661), (4, 0, -1, 0, 10675, -34782), (0, 0, 3, 0, 10034, -23210),
        (4, 0, -2, 0, 8548, -21636), (2, 1, -1, 0, -7888, 24208), (2, 1, 0, 0, -6766, 30824),
        (1, 0, -1, 0, -5163, -8379), (1, 1, 0, 0, 4987, -16675), (2, -1, 1, 0, 4036, -12831),
        (2, 0, 2, 0, 3994, -10445), (4, 0, 0, 0, 3861, -11650), (2, 0, -3, 0, 3665, 14403),
        (0, 1, -2, 0, -2689, -7003), (2, 0, -1, 2, -2602, 0), (2, -1, -2, 0, 2390, 10056),
        (1, 0, 1, 0, -2348, 6322), (2, -2, 0, 0, 2236, -9884), (0, 1, 2, 0, -2120, 5751),
        (0, 2, 0, 0, -2069, 0), (2, -2, -1, 0, 2048, -4950), (2, 0, 1, -2, -1773, 4130),
        (2, 0, 0, 2, -1595, 0), (4, -1, -1, 0, 1215, -3958), (0, 0, 2, 2, -1110, 0),
        (3, 0, -1, 0, -892, 3258), (2, 1, 1, 0, -810, 2616), (4, -1, -2, 0, 759, -1897),
        (0, 2, -1, 0, -713, -2117), (2, 2, -1, 0, -700, 2354), (2, 1, -2, 0, 691, 0),
        (2, -1, 0, -2, 596, 0), (4, 0, 1, 0, 549, -1423), (0, 0, 4, 0, 537, -1117),
        (4, -1, 0, 0, 520, -1571), (1, 0, -2, 0, -487, -1739), (2, 1, 0, -2, -399, 0),
        (0, 0, 2, -2, -381, -4421), (1, 1, 1, 0, 351, 0), (3, 0, -2, 0, -340, 0),
        (4, 0, -3, 0, 330, 0), (2, -1, 2, 0, 327, 0), (0, 2, 1, 0, -323, 1165),
        (1, 1, -1, 0, 299, 0), (2, 0, 3, 0, 294, 0), (2, 0, -1, -2, 0, 8752),
    ];

    /// <summary>Meeus's table 47.B: the same for latitude, in millionths of a degree, with sine.</summary>
    private static readonly (int D, int M, int MM, int F, double B)[] Latitude =
    [
        (0, 0, 0, 1, 5128122), (0, 0, 1, 1, 280602), (0, 0, 1, -1, 277693), (2, 0, 0, -1, 173237),
        (2, 0, -1, 1, 55413), (2, 0, -1, -1, 46271), (2, 0, 0, 1, 32573), (0, 0, 2, 1, 17198),
        (2, 0, 1, -1, 9266), (0, 0, 2, -1, 8822), (2, -1, 0, -1, 8216), (2, 0, -2, -1, 4324),
        (2, 0, 1, 1, 4200), (2, 1, 0, -1, -3359), (2, -1, -1, 1, 2463), (2, -1, 0, 1, 2211),
        (2, -1, -1, -1, 2065), (0, 1, -1, -1, -1870), (4, 0, -1, -1, 1828), (0, 1, 0, 1, -1794),
        (0, 0, 0, 3, -1749), (0, 1, -1, 1, -1565), (1, 0, 0, 1, -1491), (0, 1, 1, 1, -1475),
        (0, 1, 1, -1, -1410), (0, 1, 0, -1, -1344), (1, 0, 0, -1, -1335), (0, 0, 3, 1, 1107),
        (4, 0, 0, -1, 1021), (4, 0, -1, 1, 833), (0, 0, 1, -3, 777), (4, 0, -2, 1, 671),
        (2, 0, 0, -3, 607), (2, 0, 2, -1, 596), (2, -1, 1, -1, 491), (2, 0, -2, 1, -451),
        (0, 0, 3, -1, 439), (2, 0, 2, 1, 422), (2, 0, -3, -1, 421), (2, 1, -1, 1, -366),
        (2, 1, 0, 1, -351), (4, 0, 0, 1, 331), (2, -1, 1, 1, 315), (2, -2, 0, -1, 302),
        (0, 0, 1, 3, -283), (2, 1, 1, -1, -229), (1, 1, 0, -1, 223), (1, 1, 0, 1, 223),
        (0, 1, -2, -1, -220), (2, 1, -1, -1, -220), (1, 0, 1, 1, -185), (2, -1, -2, -1, 181),
        (0, 1, 2, 1, -177), (4, 0, -2, -1, 176), (4, -1, -1, -1, 166), (1, 0, 1, -1, -164),
        (4, 0, 1, -1, 132), (1, 0, -1, -1, -119), (4, -1, 0, -1, 115), (2, -2, 0, 1, 107),
    ];

    private static double Mod(double value, double by) => (value % by + by) % by;

    private static double Sin(double degrees) => Math.Sin(Radians(degrees));

    private static double Cos(double degrees) => Math.Cos(Radians(degrees));

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;
}

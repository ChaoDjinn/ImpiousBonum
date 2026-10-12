namespace ImpiousBonum.Core.Weather;

/// <summary>
/// Where the sun is in the sky, worked out on the PC with the formulas behind NOAA's solar calculator, so it needs
/// nothing from the forecast and moves on between fetches. Good to well under a degree for decades either side of 2000.
/// </summary>
public static class SunPosition
{
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// How high the sun is above the horizon at a place, in degrees: 90 overhead, below 0 after sunset. This is where
    /// it really is; the air lifts it a little near the horizon (half a degree at sunrise), which isn't allowed for.
    /// </summary>
    public static double Elevation(double latitude, double longitude, DateTimeOffset time)
    {
        // Julian centuries since noon on 1 January 2000.
        var t = (time.UtcDateTime - J2000).TotalDays / 36525;

        var meanLongitude = 280.46646 + t * (36000.76983 + t * 0.0003032);
        var meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        var eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        var centre = Sin(meanAnomaly) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + Sin(2 * meanAnomaly) * (0.019993 - 0.000101 * t)
            + Sin(3 * meanAnomaly) * 0.000289;
        var omega = 125.04 - 1934.136 * t;
        var apparentLongitude = meanLongitude + centre - 0.00569 - 0.00478 * Sin(omega);
        var meanObliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        var obliquity = meanObliquity + 0.00256 * Cos(omega);
        var declination = Degrees(Math.Asin(Sin(obliquity) * Sin(apparentLongitude)));

        // The equation of time: how far a sundial runs ahead of the clock, in minutes.
        var y = Math.Pow(Math.Tan(Radians(obliquity / 2)), 2);
        var equationOfTime = 4 * Degrees(
            y * Sin(2 * meanLongitude)
            - 2 * eccentricity * Sin(meanAnomaly)
            + 4 * eccentricity * y * Sin(meanAnomaly) * Cos(2 * meanLongitude)
            - 0.5 * y * y * Sin(4 * meanLongitude)
            - 1.25 * eccentricity * eccentricity * Sin(2 * meanAnomaly));

        // Sundial time at the place, then how far the sun is round from its highest (0 at solar noon).
        var solarMinutes = time.UtcDateTime.TimeOfDay.TotalMinutes + equationOfTime + 4 * longitude;
        var hourAngle = solarMinutes / 4 - 180;
        var cosZenith = Sin(latitude) * Sin(declination) + Cos(latitude) * Cos(declination) * Cos(hourAngle);
        return 90 - Degrees(Math.Acos(Math.Clamp(cosZenith, -1, 1)));
    }

    private static double Sin(double degrees) => Math.Sin(Radians(degrees));

    private static double Cos(double degrees) => Math.Cos(Radians(degrees));

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;
}

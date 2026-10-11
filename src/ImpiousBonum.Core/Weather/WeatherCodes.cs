namespace ImpiousBonum.Core.Weather;

/// <summary>What the WMO weather codes that Open-Meteo reports mean, in words and as one of the dashboard's weather icons.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Cloudy",
        45 => "Fog",
        48 => "Freezing fog",
        51 => "Light drizzle",
        53 => "Drizzle",
        55 => "Heavy drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Light showers",
        81 => "Showers",
        82 => "Heavy showers",
        85 => "Snow showers",
        86 => "Heavy snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        97 => "Heavy thunderstorm",
        _ => "Unknown",
    };

    /// <summary>
    /// The icon for a code: <c>clear-day</c>, <c>clear-night</c>, <c>partly-day</c>, <c>partly-night</c>, <c>cloud</c>,
    /// <c>fog</c>, <c>drizzle</c>, <c>rain</c>, <c>sleet</c>, <c>snow</c> or <c>storm</c>.
    /// </summary>
    public static string Icon(int code, bool isDay) => code switch
    {
        0 => isDay ? "clear-day" : "clear-night",
        1 or 2 => isDay ? "partly-day" : "partly-night",
        3 => "cloud",
        45 or 48 => "fog",
        51 or 53 or 55 => "drizzle",
        56 or 57 or 66 or 67 => "sleet",
        61 or 63 or 65 or 80 or 81 or 82 => "rain",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        >= 95 and <= 99 => "storm",
        _ => "cloud",
    };

    /// <summary>"N", "NE", "E"… from a direction the wind blows from, in degrees.</summary>
    public static string Compass(double degrees)
    {
        if (double.IsNaN(degrees))
            return string.Empty;
        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return points[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
    }
}

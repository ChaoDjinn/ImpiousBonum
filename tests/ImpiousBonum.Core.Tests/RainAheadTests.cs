using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class RainAheadTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public RainAheadTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    // Trimmed from a real Open-Meteo response for central Paris at 19:11 there, with a shower due at a quarter to eight.
    private const string Paris = """
        {
          "latitude": 48.853382, "longitude": 2.355208, "timezone": "Europe/Paris", "utc_offset_seconds": 7200,
          "current": { "time": "2026-10-16T19:00", "interval": 900, "temperature_2m": 16.3, "is_day": 0, "weather_code": 3, "precipitation": 0.00 },
          "hourly": { "time": ["2026-10-16T19:00", "2026-10-16T20:00", "2026-10-16T21:00", "2026-10-16T22:00"],
                      "temperature_2m": [16.3, 15.1, 14.4, 13.9], "weather_code": [3, 61, 61, 3], "is_day": [0, 0, 0, 0],
                      "precipitation_probability": [13, 78, 75, 33], "precipitation": [0.00, 0.30, 1.00, 0.00] },
          "minutely_15_units": { "time": "iso8601", "precipitation": "mm" },
          "minutely_15": { "time": ["2026-10-16T19:00", "2026-10-16T19:15", "2026-10-16T19:30", "2026-10-16T19:45", "2026-10-16T20:00",
                                    "2026-10-16T20:15", "2026-10-16T20:30", "2026-10-16T20:45", "2026-10-16T21:00", "2026-10-16T21:15",
                                    "2026-10-16T21:30", "2026-10-16T21:45"],
                           "precipitation": [0.00, 0.00, 0.00, 0.00, 0.30, 0.10, 0.30, 0.40, 0.20, 0.00, 0.00, 0.00] }
        }
        """;

    /// <summary>When <see cref="Paris"/> was fetched: 19:11 in Paris.</summary>
    private static readonly DateTimeOffset ParisTime = new(2026, 10, 16, 17, 11, 0, TimeSpan.Zero);

    private static readonly WeatherRequest ParisPlace = new("Paris", 48.85, 2.35, WeatherUnits.Metric);

    /// <summary>The quarter-hour that ended last in <see cref="Report"/>, and a few minutes after it.</summary>
    private static readonly DateTime Start = new(2026, 10, 16, 14, 0, 0);
    private static readonly DateTime Now = new(2026, 10, 16, 14, 7, 0);

    /// <summary>A forecast with <paramref name="amounts"/> a quarter of an hour at a time from the one ending at 14:00, and every hour's weather <paramref name="code"/>.</summary>
    private static WeatherReport Report(double[] amounts, WeatherUnits units = WeatherUnits.Metric, int code = 61) =>
        new(units, Start, 12, 11, 80, 10, 225, code, true,
            [.. Enumerable.Range(14, 4).Select(hour => new WeatherPeriod(Start.Date.AddHours(hour), 12, null, code, true, 60))],
            [], null, null)
        {
            QuarterHours = [.. amounts.Select((amount, i) => new QuarterHour(Start.AddMinutes(15 * i), amount))],
        };

    [Fact]
    public void A_forecast_reads_the_rain_a_quarter_of_an_hour_at_a_time()
    {
        var report = WeatherReport.Parse(Paris, WeatherUnits.Metric);
        Assert.Equal(12, report.QuarterHours.Count);
        Assert.Equal(new DateTime(2026, 10, 16, 19, 0, 0), report.QuarterHours[0].Time);
        Assert.Equal(new DateTime(2026, 10, 16, 21, 45, 0), report.QuarterHours[^1].Time);
        Assert.Equal([0, 0, 0, 0, 0.3, 0.1, 0.3, 0.4, 0.2, 0, 0, 0], report.QuarterHours.Select(q => q.Precipitation));

        // A forecast from before they were asked for has none, and a quarter-hour Open-Meteo has no value for is left out.
        Assert.Empty(WeatherReport.Parse("""{ "current": { "time": "2026-10-16T19:00", "temperature_2m": 16.3 } }""", WeatherUnits.Metric).QuarterHours);
        var gaps = WeatherReport.Parse("""
            { "current": { "time": "2026-10-16T19:00", "temperature_2m": 16.3 },
              "minutely_15": { "time": ["2026-10-16T19:00", "2026-10-16T19:15", "2026-10-16T19:30"], "precipitation": [0.1, null] } }
            """, WeatherUnits.Metric);
        Assert.Equal([new QuarterHour(new DateTime(2026, 10, 16, 19, 0, 0), 0.1)], gaps.QuarterHours);
    }

    [Fact]
    public void The_forecast_asks_for_three_hours_of_quarter_hours_in_the_same_request()
    {
        var url = OpenMeteo.ForecastUrl(ParisPlace);
        Assert.Contains("&minutely_15=precipitation&", url);
        Assert.Contains("&forecast_minutely_15=12", url);
        // In inches with imperial units, like the rest of the rain.
        Assert.EndsWith("&precipitation_unit=inch", OpenMeteo.ForecastUrl(ParisPlace with { Units = WeatherUnits.Imperial }));
    }

    [Theory]
    // Nothing in the next two hours: rain that's been, or that comes after them, doesn't count.
    [InlineData(new[] { 0.0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "Dry for the next 2 hours", null)]
    [InlineData(new[] { 0.8, 0, 0, 0, 0, 0, 0, 0, 0, 0.5, 0.5, 0.5 }, "Dry for the next 2 hours", null)]
    // Starting at 14:30, from the start of its quarter-hour.
    [InlineData(new[] { 0.0, 0, 0, 0.3, 0.5, 0, 0, 0, 0, 0, 0, 0 }, "Rain from 14:30", 23)]
    [InlineData(new[] { 0.0, 0, 0.1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "Rain from 14:15", 8)]
    // Raining now, and stopping.
    [InlineData(new[] { 0.0, 0.2, 0.4, 0.3, 0, 0, 0, 0.6, 0, 0, 0, 0 }, "Rain stopping around 14:45", 0)]
    // Raining all the way, as hard as its heaviest quarter-hour: up to 2.5 mm an hour is light, up to 10 moderate.
    [InlineData(new[] { 0.0, 0.2, 0.2, 0.2, 0.2, 0.3, 0.2, 0.1, 0.1, 0, 0, 0 }, "Light rain for the next 2 hours", 0)]
    [InlineData(new[] { 0.0, 0.2, 0.6, 1.2, 0.2, 0.3, 0.2, 0.1, 0.1, 0, 0, 0 }, "Moderate rain for the next 2 hours", 0)]
    [InlineData(new[] { 0.0, 0.2, 0.6, 3.1, 0.2, 0.3, 0.2, 0.1, 0.1, 0, 0, 0 }, "Heavy rain for the next 2 hours", 0)]
    public void The_summary_says_when_rain_starts_or_stops(double[] amounts, string summary, int? minutes)
    {
        var rain = RainAhead.For(Report(amounts), Now)!;
        Assert.Equal(summary, rain.Summary);
        Assert.Equal(minutes, rain.MinutesToRain);
        // The quarter-hour it is now and the seven after it.
        Assert.Equal(RainAhead.Length, rain.Steps.Count);
        Assert.Equal(new DateTime(2026, 10, 16, 14, 15, 0), rain.Steps[0].Time);
    }

    [Fact]
    public void Snow_is_called_snow()
    {
        Assert.Equal("Snow from 14:30", RainAhead.For(Report([0, 0, 0, 0.3, 0.5, 0, 0, 0, 0], code: 73), Now)!.Summary);
        Assert.Equal("Light snow for the next 2 hours", RainAhead.For(Report([0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2], code: 85), Now)!.Summary);
        // Sleet and freezing rain are still rain.
        Assert.Equal("Rain from 14:30", RainAhead.For(Report([0, 0, 0, 0.3, 0.5, 0, 0, 0, 0], code: 66), Now)!.Summary);
    }

    [Fact]
    public void Inches_are_read_as_inches()
    {
        // A thousandth of an inch is a trace; four is a tenth of a millimetre, a light shower's quarter-hour.
        Assert.Equal("Dry for the next 2 hours", RainAhead.For(Report([0, 0.001, 0.001, 0, 0, 0, 0, 0, 0], WeatherUnits.Imperial), Now)!.Summary);
        Assert.Equal("Rain from 14:30", RainAhead.For(Report([0, 0, 0, 0.004, 0, 0, 0, 0, 0], WeatherUnits.Imperial), Now)!.Summary);
        Assert.Equal(RainIntensity.Heavy, RainAhead.Intensity(0.1, WeatherUnits.Imperial));
        Assert.Equal(RainIntensity.Light, RainAhead.Intensity(0.1, WeatherUnits.Metric));
    }

    [Fact]
    public void A_forecast_that_doesnt_reach_two_hours_says_how_far_it_does()
    {
        Assert.Equal("Dry for the next hour", RainAhead.For(Report([0, 0, 0, 0, 0]), Now)!.Summary);
        Assert.Equal("Dry for the next 90 minutes", RainAhead.For(Report([0, 0, 0, 0, 0, 0, 0]), Now)!.Summary);
        Assert.Equal("Light rain for the next 45 minutes", RainAhead.For(Report([0, 0.1, 0.1, 0.1]), Now)!.Summary);

        // It stops at a quarter-hour that's missing, rather than guess.
        var full = Report([0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var gap = full with { QuarterHours = [.. full.QuarterHours.Where(q => q.Time.Minute != 45)] };
        Assert.Equal("Dry for the next 30 minutes", RainAhead.For(gap, Now)!.Summary);

        // Nothing from now: none at all, all of them past, or the next one starting later.
        Assert.Null(RainAhead.For(Report([]), Now));
        Assert.Null(RainAhead.For(Report([0, 0, 0]), Now.AddHours(1)));
        Assert.Null(RainAhead.For(Report([0, 0, 0, 0, 0, 0]), Now.AddMinutes(-30)));
    }

    [Fact]
    public void The_readings_are_for_the_time_at_the_place()
    {
        var provider = new WeatherProvider(() => null);
        var store = new MetricStore();
        provider.Initialize(store);

        // 17:11 UTC is 19:11 in Paris, whatever the time zone of the PC.
        provider.Publish(store, WeatherReport.Parse(Paris, WeatherUnits.Metric), ParisPlace, ParisTime);
        Assert.Equal("Rain from 19:45", Text(store, WeatherProvider.RainSoon));
        Assert.Equal("34", Format(store, WeatherProvider.RainNext));

        // It moves on between forecasts: raining by ten to eight, and stopping at nine.
        provider.Publish(store, WeatherReport.Parse(Paris, WeatherUnits.Metric), ParisPlace, ParisTime.AddMinutes(39));
        Assert.Equal("Rain stopping around 21:00", Text(store, WeatherProvider.RainSoon));
        Assert.Equal("0", Format(store, WeatherProvider.RainNext));

        // Without its offset from UTC, it's as of the forecast's own time.
        var noOffset = WeatherReport.Parse(Paris.Replace("\"utc_offset_seconds\": 7200,", string.Empty), WeatherUnits.Metric);
        provider.Publish(store, noOffset, ParisPlace, ParisTime);
        Assert.Equal("Rain from 19:45", Text(store, WeatherProvider.RainSoon));
        Assert.Equal("45", Format(store, WeatherProvider.RainNext));

        // Dry for two hours: no minutes until rain. Without a forecast, or quarter-hours, nothing.
        provider.Publish(store, Report([0, 0, 0, 0, 0, 0, 0, 0, 0]) with { UtcOffset = TimeSpan.Zero }, ParisPlace, new DateTimeOffset(Now, TimeSpan.Zero));
        Assert.Equal("Dry for the next 2 hours", Text(store, WeatherProvider.RainSoon));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.RainNext));
        provider.Publish(store, Report([]), ParisPlace, ParisTime);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.RainSoon));
        provider.Publish(store, null, null, ParisTime);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.RainNext));
    }

    [Fact]
    public void Times_are_shown_as_windows_shows_them()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var summary = RainAhead.For(Report([0, 0, 0, 0.3, 0.5, 0, 0, 0, 0]), Now)!.Summary;
        Assert.Equal("Rain from " + new DateTime(2026, 10, 16, 14, 30, 0).ToString("t", CultureInfo.CurrentCulture), summary);
        Assert.EndsWith("PM", summary);
    }

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";
}

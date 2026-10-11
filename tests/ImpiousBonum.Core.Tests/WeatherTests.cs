using System.Net.Http;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class WeatherTests
{
    // Trimmed from a real Open-Meteo response for central London.
    private const string Forecast = """
        {
          "latitude": 51.51, "longitude": -0.13, "timezone": "Europe/London", "utc_offset_seconds": 3600,
          "current": { "time": "2026-10-11T09:45", "interval": 900, "temperature_2m": 10.9, "apparent_temperature": 8.8,
                       "relative_humidity_2m": 72, "is_day": 1, "weather_code": 3, "wind_speed_10m": 7.6, "wind_direction_10m": 283 },
          "hourly": { "time": ["2026-10-11T09:00", "2026-10-11T10:00", "2026-10-11T11:00"],
                      "temperature_2m": [10.1, 11.1, 12.1], "weather_code": [3, 61, 3], "is_day": [1, 1, 1],
                      "precipitation_probability": [0, 40, null] },
          "daily": { "time": ["2026-10-11", "2026-10-12"], "weather_code": [3, 51],
                     "temperature_2m_max": [15.5, 17.3], "temperature_2m_min": [9.5, 13.3],
                     "precipitation_probability_max": [0, 45],
                     "sunrise": ["2026-10-11T07:17", "2026-10-12T07:19"], "sunset": ["2026-10-11T18:16", "2026-10-12T18:13"] }
        }
        """;

    private static readonly WeatherRequest London = new("London", 51.51, -0.13, WeatherUnits.Metric);

    [Fact]
    public void A_forecast_reads_now_hours_and_days()
    {
        var report = WeatherReport.Parse(Forecast, WeatherUnits.Metric);

        Assert.Equal(new DateTime(2026, 10, 11, 9, 45, 0), report.Time);
        Assert.Equal(10.9, report.Temperature);
        Assert.Equal(8.8, report.FeelsLike);
        Assert.Equal(72, report.Humidity);
        Assert.Equal(3, report.Code);
        Assert.True(report.IsDay);
        Assert.Equal(3, report.Hours.Count);
        Assert.Null(report.Hours[2].RainChance);
        Assert.Equal(new[] { 10, 11 }, report.HoursAhead.Select(h => h.Time.Hour));
        Assert.Equal(15.5, report.Today!.Temperature);
        Assert.Equal(9.5, report.Today.Low);
        Assert.Equal(51, report.Tomorrow!.Code);
        Assert.Equal(new DateTime(2026, 10, 11, 7, 17, 0), report.Sunrise);
    }

    [Fact]
    public void Something_that_isnt_a_forecast_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => WeatherReport.Parse("""{ "error": true, "reason": "Latitude must be in range" }""", WeatherUnits.Metric));
        Assert.ThrowsAny<Exception>(() => WeatherReport.Parse("not json", WeatherUnits.Metric));
    }

    [Theory]
    [InlineData(0, true, "Clear", "clear-day")]
    [InlineData(0, false, "Clear", "clear-night")]
    [InlineData(2, false, "Partly cloudy", "partly-night")]
    [InlineData(45, true, "Fog", "fog")]
    [InlineData(53, true, "Drizzle", "drizzle")]
    [InlineData(66, true, "Freezing rain", "sleet")]
    [InlineData(81, true, "Showers", "rain")]
    [InlineData(86, true, "Heavy snow showers", "snow")]
    [InlineData(99, true, "Thunderstorm with hail", "storm")]
    [InlineData(42, true, "Unknown", "cloud")]
    public void Weather_codes_have_words_and_icons(int code, bool isDay, string words, string icon)
    {
        Assert.Equal(words, WeatherCodes.Describe(code));
        Assert.Equal(icon, WeatherCodes.Icon(code, isDay));
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(283, "W")]
    [InlineData(338, "N")]
    [InlineData(-45, "NW")]
    [InlineData(135, "SE")]
    public void Wind_directions_read_as_compass_points(double degrees, string point) =>
        Assert.Equal(point, WeatherCodes.Compass(degrees));

    [Fact]
    public void Place_searches_and_typed_coordinates_are_understood()
    {
        var places = OpenMeteo.ParsePlaces("""
            {"results":[{"name":"Springfield","latitude":37.2,"longitude":-93.3,"country":"United States","admin1":"Missouri"},
                        {"name":"Springfield","latitude":39.8,"longitude":-89.6,"country":"United States","admin1":"Illinois"}]}
            """);
        Assert.Equal(new[] { "Springfield, Missouri, United States", "Springfield, Illinois, United States" }, places.Select(p => p.Description));
        Assert.Empty(OpenMeteo.ParsePlaces("""{"generationtime_ms": 0.5}"""));

        Assert.Equal((51.5, -0.12), OpenMeteo.ParseCoordinates(" 51.5, -0.12 "));
        Assert.Null(OpenMeteo.ParseCoordinates("London"));
        Assert.Null(OpenMeteo.ParseCoordinates("95, 10"));
    }

    [Fact]
    public async Task Nothing_is_fetched_until_a_location_is_set()
    {
        var fetches = 0;
        WeatherRequest? wanted = null;
        var provider = new WeatherProvider(() => wanted, (_, _) => { fetches++; return Task.FromResult(WeatherReport.Parse(Forecast, WeatherUnits.Metric)); });
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, fetches);
        Assert.Null(store.GetAttachment(WeatherProvider.Forecast));

        wanted = London;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("11 °C", Format(store, WeatherProvider.Temperature));
        Assert.Equal("Cloudy", Format(store, WeatherProvider.Condition));
        Assert.Equal("16 °C", Format(store, WeatherProvider.High));
        Assert.Equal("8 km/h", Format(store, WeatherProvider.Wind));
        Assert.Equal("W", Format(store, WeatherProvider.WindDirection));
        Assert.Equal("Light drizzle", Format(store, WeatherProvider.TomorrowCondition));
        Assert.Equal("London", Format(store, WeatherProvider.Location));
        Assert.IsType<WeatherReport>(store.GetAttachment(WeatherProvider.Forecast));
    }

    [Fact]
    public async Task Forecasts_refresh_every_15_minutes_retry_failures_and_follow_the_location()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var requests = new List<WeatherRequest>();
        var fail = false;
        var wanted = London;
        var provider = new WeatherProvider(() => wanted, (request, _) =>
        {
            requests.Add(request);
            return fail ? Task.FromException<WeatherReport>(new HttpRequestException("offline")) : Task.FromResult(WeatherReport.Parse(Forecast, request.Units));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(requests);

        // Due again, but offline: the old forecast stays and it tries again a couple of minutes later.
        fail = true;
        time.Advance(TimeSpan.FromMinutes(6));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests.Count);
        Assert.NotNull(store.GetAttachment(WeatherProvider.Forecast));
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests.Count);
        time.Advance(WeatherProvider.RetryAfter);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests.Count);

        // Still offline hours later: an old forecast is worse than none.
        time.Advance(WeatherProvider.KeepFor);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(WeatherProvider.Forecast));

        // A new place, or new units, fetches straight away.
        fail = false;
        wanted = London with { Units = WeatherUnits.Imperial };
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(WeatherUnits.Imperial, requests[^1].Units);
        store.TryGet(WeatherProvider.Temperature, out var temperature);
        Assert.Equal(MetricUnit.Fahrenheit, temperature.Definition.Unit);
    }

    [Fact]
    public void A_metric_can_bring_its_own_number_format()
    {
        var sample = new MetricSample(new MetricDefinition("t", "t", "t", MetricUnit.Fahrenheit, Format: "0"), 51.7, null);
        Assert.Equal("52 °F", MetricFormatter.Format(sample));
        Assert.Equal("51.7 °F", MetricFormatter.Format(sample, FormatSpec.Parse("0.0")));
        Assert.Equal("13 mph", MetricFormatter.Format(new MetricSample(new MetricDefinition("w", "w", "w", MetricUnit.MilesPerHour), 12.6, null)));
    }

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

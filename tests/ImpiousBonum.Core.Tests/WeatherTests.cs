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

    // The same with every reading the app asks for now, from three hours back (for the pressure trend).
    private const string FullForecast = """
        {
          "latitude": 51.51, "longitude": -0.13, "timezone": "Europe/London", "utc_offset_seconds": 3600,
          "current_units": { "time": "iso8601", "visibility": "m", "precipitation": "mm" },
          "current": { "time": "2026-10-12T09:45", "interval": 900, "temperature_2m": 12.4, "apparent_temperature": 10.1,
                       "relative_humidity_2m": 81, "is_day": 1, "weather_code": 61, "wind_speed_10m": 14.4, "wind_direction_10m": 225,
                       "wind_gusts_10m": 31.7, "uv_index": 2.35, "pressure_msl": 1009.6, "dew_point_2m": 9.2, "visibility": 20500.00,
                       "cloud_cover": 88, "precipitation": 0.30 },
          "hourly": { "time": ["2026-10-12T06:00", "2026-10-12T07:00", "2026-10-12T08:00", "2026-10-12T09:00", "2026-10-12T10:00", "2026-10-12T11:00"],
                      "temperature_2m": [9.8, 10.2, 11.0, 12.1, 12.9, 13.6], "weather_code": [3, 3, 61, 61, 63, 61], "is_day": [0, 1, 1, 1, 1, 1],
                      "precipitation_probability": [10, 20, 35, 60, 75, 40], "precipitation": [0.00, 0.00, 0.10, 0.40, 0.80, 0.20],
                      "pressure_msl": [1012.1, 1011.4, 1010.6, 1009.8, 1009.1, 1008.7], "wind_speed_10m": [9.4, 10.8, 12.6, 14.0, 15.1, 16.2],
                      "wind_gusts_10m": [20.5, 24.1, 28.8, 31.3, 34.6, 36.0], "uv_index": [0.00, 0.10, 0.60, 1.40, 2.30, 2.90] },
          "daily": { "time": ["2026-10-12", "2026-10-13"], "weather_code": [61, 3],
                     "temperature_2m_max": [14.2, 15.8], "temperature_2m_min": [8.9, 9.6],
                     "precipitation_probability_max": [80, 25], "precipitation_sum": [6.40, 0.20],
                     "sunrise": ["2026-10-12T07:19", "2026-10-13T07:21"], "sunset": ["2026-10-12T18:13", "2026-10-13T18:11"],
                     "daylight_duration": [39293.31, 39061.58], "uv_index_max": [3.10, 2.60],
                     "wind_speed_10m_max": [24.1, 18.0], "wind_gusts_10m_max": [52.9, 38.2] }
        }
        """;

    private static readonly WeatherRequest London = new("London", 51.51, -0.13, WeatherUnits.Metric);

    /// <summary>A quarter to ten in London on 12 October 2026, when <see cref="FullForecast"/> is for.</summary>
    private static readonly DateTimeOffset FullForecastTime = new(2026, 10, 12, 8, 45, 0, TimeSpan.Zero);

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

    [Fact]
    public void A_forecast_reads_wind_gusts_uv_pressure_and_the_rest()
    {
        var report = WeatherReport.Parse(FullForecast, WeatherUnits.Metric);

        Assert.Equal(TimeSpan.FromHours(1), report.UtcOffset);
        Assert.Equal(31.7, report.Gusts);
        Assert.Equal(2.35, report.Uv);
        Assert.Equal(1009.6, report.Pressure);
        Assert.Equal(9.2, report.DewPoint);
        Assert.Equal(20500, report.Visibility);
        Assert.Equal(88, report.Clouds);
        Assert.Equal(0.3, report.Precipitation);

        // Three hours back are there for the pressure trend, but the hours ahead still start after this one.
        Assert.Equal(6, report.Hours.Count);
        Assert.Equal(new[] { 10, 11 }, report.HoursAhead.Select(h => h.Time.Hour));
        Assert.Equal(1012.1, report.Hours[0].Pressure);
        Assert.Equal(31.3, report.Hours[3].Gusts);
        Assert.Equal(0.4, report.Hours[3].Precipitation);
        Assert.Equal(1.4, report.Hours[3].Uv);
        Assert.Equal(14.0, report.Hours[3].Wind);

        Assert.Equal(6.4, report.Today!.Precipitation);
        Assert.Equal(3.1, report.Today.Uv);
        Assert.Equal(52.9, report.Today.Gusts);
        Assert.Equal(24.1, report.Today.Wind);
        Assert.Equal(TimeSpan.FromSeconds(39293.31), report.Today.Daylight);
    }

    [Fact]
    public void Readings_a_forecast_leaves_out_are_unknown()
    {
        // From before these readings were asked for.
        var report = WeatherReport.Parse(Forecast, WeatherUnits.Metric);
        Assert.Null(report.Gusts);
        Assert.Null(report.Uv);
        Assert.Null(report.Pressure);
        Assert.Null(report.DewPoint);
        Assert.Null(report.Visibility);
        Assert.Null(report.Clouds);
        Assert.Null(report.Precipitation);
        Assert.Null(report.Hours[0].Pressure);
        Assert.Null(report.Today!.Daylight);
        Assert.Null(report.Today.Uv);

        // Open-Meteo sends null for values it hasn't got, and can leave a list short.
        var gaps = WeatherReport.Parse("""
            {
              "current": { "time": "2026-10-12T09:45", "temperature_2m": 12.4, "uv_index": null, "pressure_msl": null, "visibility": null },
              "hourly": { "time": ["2026-10-12T08:00", "2026-10-12T09:00"], "pressure_msl": [null, 1009.8], "uv_index": [1.2] },
              "daily": { "time": ["2026-10-12"], "daylight_duration": [null], "uv_index_max": null, "sunrise": null }
            }
            """, WeatherUnits.Metric);
        Assert.Null(gaps.Uv);
        Assert.Null(gaps.Pressure);
        Assert.Null(gaps.Visibility);
        Assert.Null(gaps.UtcOffset);
        Assert.Null(gaps.Hours[0].Pressure);
        Assert.Equal(1009.8, gaps.Hours[1].Pressure);
        Assert.Null(gaps.Hours[1].Uv);
        Assert.Null(gaps.Today!.Daylight);
        Assert.Null(gaps.Today.Uv);
        Assert.Null(gaps.Sunrise);
    }

    [Fact]
    public void Visibility_in_feet_is_read_as_metres()
    {
        // Asking for precipitation in inches, as imperial units do, turns Open-Meteo's visibility into feet.
        const string json = """
            { "current_units": { "visibility": "ft" }, "current": { "time": "2026-10-12T09:45", "temperature_2m": 54.3, "visibility": 67257.216 } }
            """;
        Assert.Equal(20500, WeatherReport.Parse(json, WeatherUnits.Imperial).Visibility!.Value, 0.01);
        Assert.Equal(20500, WeatherReport.Parse(json.Replace("\"ft\"", "\"m\"").Replace("67257.216", "20500"), WeatherUnits.Imperial).Visibility!.Value, 0.01);
    }

    [Fact]
    public void One_request_asks_for_everything_in_the_chosen_units()
    {
        var metric = OpenMeteo.ForecastUrl(London);
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?latitude=51.51&longitude=-0.13&", metric);
        Assert.Contains("wind_gusts_10m,uv_index,pressure_msl,dew_point_2m,visibility,cloud_cover,precipitation&", metric);
        Assert.Contains("uv_index_max", metric);
        Assert.Contains("daylight_duration", metric);
        // The current hour and 48 after it, so the widget has 48 hours ahead.
        Assert.Contains("&forecast_days=16&past_hours=3&forecast_hours=49", metric);
        Assert.DoesNotContain("inch", metric);

        var imperial = OpenMeteo.ForecastUrl(London with { Units = WeatherUnits.Imperial });
        Assert.EndsWith("&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch", imperial);
    }

    [Theory]
    [InlineData(1012.1, 1009.8, PressureTrend.Falling)]
    [InlineData(1013.1, 1014.1, PressureTrend.Rising)]
    [InlineData(1013.0, 1013.9, PressureTrend.Steady)]
    [InlineData(1013.0, 1012.1, PressureTrend.Steady)]
    public void The_pressure_trend_is_over_the_last_three_hours(double before, double now, PressureTrend expected)
    {
        var hours = new[] { before, 1020, 1000, now }
            .Select((pressure, i) => new WeatherPeriod(new DateTime(2026, 10, 12, 6 + i, 0, 0), 12, null, 3, true, null) { Pressure = pressure })
            .ToList();

        // Any time in the hour compares that hour with three before; the hours between don't matter.
        Assert.Equal(expected, WeatherReadings.Trend(hours, new DateTime(2026, 10, 12, 9, 45, 0)));
        Assert.Equal(expected, WeatherReadings.Trend(hours, new DateTime(2026, 10, 12, 9, 0, 0)));
    }

    [Fact]
    public void Without_both_pressures_there_is_no_trend()
    {
        var hours = WeatherReport.Parse(FullForecast, WeatherUnits.Metric).Hours;
        Assert.Equal(PressureTrend.Falling, WeatherReadings.Trend(hours, new DateTime(2026, 10, 12, 9, 45, 0)));
        // At 8:00 the forecast doesn't go back to 5:00.
        Assert.Null(WeatherReadings.Trend(hours, new DateTime(2026, 10, 12, 8, 30, 0)));
        Assert.Null(WeatherReadings.Trend(hours, new DateTime(2026, 10, 12, 5, 0, 0)));
        Assert.Null(WeatherReadings.Trend(hours.Select(h => h with { Pressure = null }).ToList(), new DateTime(2026, 10, 12, 9, 45, 0)));
    }

    [Fact]
    public void The_chance_of_rain_this_hour_is_from_the_hour_that_ends_next()
    {
        // Open-Meteo's 10:00 hour has the rain and its chance from 9:00 to 10:00.
        var report = WeatherReport.Parse(FullForecast, WeatherUnits.Metric);
        Assert.Equal(75, report.HourEndingAfter(new DateTime(2026, 10, 12, 9, 45, 0))!.RainChance);
        Assert.Equal(75, report.HourEndingAfter(new DateTime(2026, 10, 12, 9, 0, 0))!.RainChance);
        Assert.Equal(40, report.HourEndingAfter(new DateTime(2026, 10, 12, 10, 0, 0))!.RainChance);
        Assert.Equal(10, report.HourEndingAfter(new DateTime(2026, 10, 12, 5, 30, 0))!.RainChance);
        // Past the forecast's last hour, or more than an hour before its first, it isn't known.
        Assert.Null(report.HourEndingAfter(new DateTime(2026, 10, 12, 11, 0, 0)));
        Assert.Null(report.HourEndingAfter(new DateTime(2026, 10, 12, 4, 30, 0)));
    }

    [Theory]
    [InlineData(0.0, "Low")]
    [InlineData(2.4, "Low")]
    [InlineData(2.5, "Moderate")]
    [InlineData(5.0, "Moderate")]
    [InlineData(6.0, "High")]
    [InlineData(7.4, "High")]
    [InlineData(8.0, "Very high")]
    [InlineData(10.4, "Very high")]
    [InlineData(10.5, "Extreme")]
    [InlineData(14.0, "Extreme")]
    [InlineData(null, null)]
    public void Uv_levels_follow_the_who_bands(double? index, string? level) =>
        Assert.Equal(level, WeatherReadings.UvLevel(index));

    [Theory]
    [InlineData(39293.31, "10 h 55 min")]
    [InlineData(38940, "10 h 49 min")]
    [InlineData(86400, "24 h 0 min")]
    [InlineData(0, "0 h 0 min")]
    public void Daylight_reads_in_hours_and_minutes(double seconds, string expected) =>
        Assert.Equal(expected, WeatherReadings.Daylight(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task Every_new_reading_is_published_in_the_chosen_units()
    {
        var time = new FakeTime(FullForecastTime);
        var wanted = London;
        var provider = new WeatherProvider(() => wanted, (request, _) => Task.FromResult(WeatherReport.Parse(FullForecast, request.Units)), time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("32 km/h", Format(store, WeatherProvider.Gusts));
        Assert.Equal("225°", Format(store, WeatherProvider.WindDegrees));
        Assert.Equal("SW", Format(store, WeatherProvider.WindDirection));
        Assert.Equal("1010 hPa", Format(store, WeatherProvider.Pressure));
        Assert.Equal("Falling", Format(store, WeatherProvider.PressureTrend));
        Assert.Equal("9 °C", Format(store, WeatherProvider.DewPoint));
        Assert.Equal("20.5 km", Format(store, WeatherProvider.Visibility));
        Assert.Equal("88 %", Format(store, WeatherProvider.Clouds));
        Assert.Equal("0.3 mm", Format(store, WeatherProvider.Precipitation));
        Assert.Equal("6.4 mm", Format(store, WeatherProvider.PrecipitationToday));
        Assert.Equal("80 %", Format(store, WeatherProvider.RainChance));
        Assert.Equal("75 %", Format(store, WeatherProvider.RainChanceHour));
        Assert.Equal("25 %", Format(store, WeatherProvider.TomorrowRainChance));
        Assert.Equal("2", Format(store, WeatherProvider.Uv));
        Assert.Equal("3", Format(store, WeatherProvider.UvMax));
        Assert.Equal("Low", Format(store, WeatherProvider.UvLevel));
        Assert.Equal("10 h 55 min", Format(store, WeatherProvider.Daylight));
        Assert.Equal("19°", Format(store, WeatherProvider.SunElevation));

        // The sun is worked out on every sample, so it climbs between forecasts, and the hour moves on too.
        time.Advance(TimeSpan.FromMinutes(30));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("23°", Format(store, WeatherProvider.SunElevation));
        Assert.Equal("40 %", Format(store, WeatherProvider.RainChanceHour));

        wanted = London with { Units = WeatherUnits.Imperial };
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("29.81 inHg", Format(store, WeatherProvider.Pressure));
        Assert.Equal("12.7 mi", Format(store, WeatherProvider.Visibility));
        Assert.Equal("0.30 in", Format(store, WeatherProvider.Precipitation));
        Assert.Equal("32 mph", Format(store, WeatherProvider.Gusts));
    }

    [Fact]
    public void Readings_a_forecast_leaves_out_have_no_value_but_the_sun_still_does()
    {
        var provider = new WeatherProvider(() => null);
        var store = new MetricStore();
        provider.Initialize(store);
        provider.Publish(store, WeatherReport.Parse(Forecast, WeatherUnits.Metric), London, FullForecastTime);

        Assert.Equal("11 °C", Format(store, WeatherProvider.Temperature));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.Gusts));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.Pressure));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.PressureTrend));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.UvLevel));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.Daylight));
        Assert.Equal("19°", Format(store, WeatherProvider.SunElevation));

        // Without a forecast, nothing.
        provider.Publish(store, null, null, FullForecastTime);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherProvider.SunElevation));
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

    private static readonly WeatherRequest Paris = new("Paris, Île-de-France, France", 48.85, 2.35, WeatherUnits.Metric);

    private static readonly WeatherRequest Sydney = new("Sydney, New South Wales, Australia", -33.87, 151.21, WeatherUnits.Metric);

    /// <summary>A provider whose forecasts are all <see cref="Forecast"/>, noting which places were asked for, and failing for those in <paramref name="failing"/>.</summary>
    private static WeatherProvider Provider(
        List<string> asked, Func<IReadOnlyList<WeatherRequest>>? places, TimeProvider time, Func<WeatherRequest?>? main = null, HashSet<string>? failing = null) =>
        new(main ?? (() => London), (request, _) =>
        {
            asked.Add(request.Name.Split(',')[0]);
            return failing?.Contains(request.Name.Split(',')[0]) == true
                ? Task.FromException<WeatherReport>(new HttpRequestException("offline"))
                : Task.FromResult(WeatherReport.Parse(Forecast, request.Units));
        }, time, places: places);

    private static IReadOnlyList<PlaceForecast>? Places(MetricStore store) => store.GetAttachment(WeatherProvider.Places) as IReadOnlyList<PlaceForecast>;

    [Fact]
    public async Task Without_more_places_nothing_more_is_fetched_or_published()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        foreach (var places in new Func<IReadOnlyList<WeatherRequest>>?[] { null, () => [] })
        {
            var asked = new List<string>();
            var provider = Provider(asked, places, time);
            var store = new MetricStore();
            provider.Initialize(store);
            for (var i = 0; i < 4; i++)
            {
                await provider.SampleAsync(store, CancellationToken.None);
                time.Advance(provider.Interval);
            }
            Assert.Equal(["London"], asked);
            Assert.Null(Places(store));
        }

        // Nor with more places but no location: the weather is off.
        var none = new List<string>();
        var off = Provider(none, () => [Paris], time, main: () => null);
        var offStore = new MetricStore();
        off.Initialize(offStore);
        await off.SampleAsync(offStore, CancellationToken.None);
        time.Advance(off.Interval);
        await off.SampleAsync(offStore, CancellationToken.None);
        Assert.Empty(none);
        Assert.Null(Places(offStore));
    }

    [Fact]
    public async Task More_places_are_fetched_a_sample_apart_after_the_main_place_and_on_its_schedule()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var asked = new List<string>();
        var provider = Provider(asked, () => [Paris, Sydney], time);
        var store = new MetricStore();
        provider.Initialize(store);

        // The main place first, on its own; the others wait, named, with no forecast yet.
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London"], asked);
        var places = Places(store)!;
        Assert.Equal(["London", "Paris, Île-de-France, France", "Sydney, New South Wales, Australia"], places.Select(p => p.Name));
        Assert.Same(store.GetAttachment(WeatherProvider.Forecast), places[0].Report);
        Assert.Null(places[1].Report);

        // Then one a sample, in their order.
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris"], asked);
        Assert.NotNull(Places(store)![1].Report);
        Assert.Null(Places(store)![2].Report);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "Sydney"], asked);
        Assert.All(Places(store)!, p => Assert.NotNull(p.Report));

        // Nothing changed: the same list stays, so the widget can tell.
        var unchanged = Places(store);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, asked.Count);
        Assert.Same(unchanged, Places(store));

        // Every 15 minutes like the main place, and still a sample apart.
        time.Advance(WeatherProvider.RefreshEvery - provider.Interval * 3);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "Sydney", "London"], asked);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "Sydney", "London", "Paris", "Sydney"], asked);
    }

    [Fact]
    public async Task A_place_that_fails_tries_again_later_and_its_forecast_goes_once_too_old()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var asked = new List<string>();
        var failing = new HashSet<string>();
        var provider = Provider(asked, () => [Paris], time, failing: failing);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.NotNull(Places(store)![1].Report);

        // Offline when it's due: the last forecast stays, and it tries again a couple of minutes later.
        failing.Add("Paris");
        time.Advance(WeatherProvider.RefreshEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "London"], asked);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "London", "Paris"], asked);
        Assert.NotNull(Places(store)![1].Report);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4, asked.Count);
        time.Advance(WeatherProvider.RetryAfter);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Paris", asked[^1]);
        Assert.Equal(5, asked.Count);

        // Still offline hours later: an old forecast is worse than none, though the main place's is fine.
        time.Advance(WeatherProvider.KeepFor);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Places(store)![1].Report);
        Assert.NotNull(Places(store)![0].Report);
    }

    [Fact]
    public async Task Places_keep_their_forecasts_when_reordered_and_a_removed_one_is_never_fetched_again()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var asked = new List<string>();
        IReadOnlyList<WeatherRequest> places = [Paris, Sydney];
        var provider = Provider(asked, () => places, time);
        var store = new MetricStore();
        provider.Initialize(store);
        for (var i = 0; i < 3; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(provider.Interval);
        }
        var paris = Places(store)![1].Report;

        // In their new order, with the forecasts they had: nothing's fetched again.
        places = [Sydney, Paris];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, asked.Count);
        Assert.Equal(["London", "Sydney, New South Wales, Australia", "Paris, Île-de-France, France"], Places(store)!.Select(p => p.Name));
        Assert.Same(paris, Places(store)![2].Report);

        // Removed, Paris is forgotten, and when the others are due again it isn't among them.
        places = [Sydney];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Sydney, New South Wales, Australia"], Places(store)!.Select(p => p.Name));
        for (var i = 0; i < 5; i++)
        {
            time.Advance(WeatherProvider.RefreshEvery / 4);
            await provider.SampleAsync(store, CancellationToken.None);
        }
        Assert.Equal(["London", "Paris", "Sydney", "London", "Sydney"], asked);

        // None left: the list goes, and nothing more is asked for.
        places = [];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Places(store));

        // Added back, it's new again: due straight away.
        places = [Paris];
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Paris", asked[^1]);
    }

    [Fact]
    public async Task More_places_leave_the_main_place_as_it_was()
    {
        var time = new FakeTime(FullForecastTime);
        var alone = new List<string>();
        var withPlaces = new List<string>();
        var plain = Provider(alone, null, time);
        var more = Provider(withPlaces, () => [Paris, Sydney], time);
        var (plainStore, moreStore) = (new MetricStore(), new MetricStore());
        plain.Initialize(plainStore);
        more.Initialize(moreStore);

        // Sampled side by side for half an hour: the main place is asked for at the same times, and reads the same.
        for (var i = 0; i < 60; i++)
        {
            await plain.SampleAsync(plainStore, CancellationToken.None);
            await more.SampleAsync(moreStore, CancellationToken.None);
            Assert.Equal(alone.Count, withPlaces.Count(p => p == "London"));
            foreach (var id in new[] { WeatherProvider.Temperature, WeatherProvider.Location, WeatherProvider.Condition, WeatherProvider.SunElevation })
                Assert.Equal(Format(plainStore, id), Format(moreStore, id));
            time.Advance(plain.Interval);
        }
        // At the start and 15 minutes in.
        Assert.Equal(2, alone.Count);
        Assert.Equal("London", Format(moreStore, WeatherProvider.Location));
    }

    [Fact]
    public async Task New_units_fetch_every_place_again_still_a_sample_apart()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var asked = new List<string>();
        var units = WeatherUnits.Metric;
        var provider = Provider(asked, () => [Paris with { Units = units }, Sydney with { Units = units }], time, main: () => London with { Units = units });
        var store = new MetricStore();
        provider.Initialize(store);
        for (var i = 0; i < 3; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(provider.Interval);
        }

        units = WeatherUnits.Imperial;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "Sydney", "London"], asked);
        Assert.Null(Places(store)![1].Report);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["London", "Paris", "Sydney", "London", "Paris", "Sydney"], asked);
        Assert.All(Places(store)!, p => Assert.Equal(WeatherUnits.Imperial, p.Report?.Units));
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

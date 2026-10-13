using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class AirQualityTests
{
    // Open-Meteo's answer for Liverpool on the evening of 12 October 2026: no pollen out of season, but counted.
    private const string Liverpool = """
        {"latitude":53.4,"longitude":-2.8999996,"generationtime_ms":1.0077953338623047,"utc_offset_seconds":0,"timezone":"GMT","timezone_abbreviation":"GMT","elevation":37.0,
         "current_units":{"time":"iso8601","interval":"seconds","european_aqi":"EAQI","us_aqi":"USAQI","pm2_5":"μg/m³","pm10":"μg/m³","ozone":"μg/m³","nitrogen_dioxide":"μg/m³","alder_pollen":"grains/m³","birch_pollen":"grains/m³","grass_pollen":"grains/m³","mugwort_pollen":"grains/m³","olive_pollen":"grains/m³","ragweed_pollen":"grains/m³"},
         "current":{"time":"2026-10-12T21:00","interval":3600,"european_aqi":26,"us_aqi":33,"pm2_5":4.2,"pm10":8.8,"ozone":42.0,"nitrogen_dioxide":14.5,"alder_pollen":0.0,"birch_pollen":0.0,"grass_pollen":0.0,"mugwort_pollen":0.0,"olive_pollen":0.0,"ragweed_pollen":0.0}}
        """;

    // The same evening in New York: pollen is only counted in Europe, so it's null.
    private const string NewYork = """
        {"latitude":40.699997,"longitude":-74.0,"generationtime_ms":0.3533363342285156,"utc_offset_seconds":0,"timezone":"GMT","timezone_abbreviation":"GMT","elevation":0.0,
         "current_units":{"time":"iso8601","interval":"seconds","european_aqi":"EAQI","us_aqi":"USAQI","pm2_5":"μg/m³","pm10":"μg/m³","ozone":"μg/m³","nitrogen_dioxide":"μg/m³","alder_pollen":"grains/m³","birch_pollen":"grains/m³","grass_pollen":"grains/m³","mugwort_pollen":"grains/m³","olive_pollen":"grains/m³","ragweed_pollen":"grains/m³"},
         "current":{"time":"2026-10-12T21:00","interval":3600,"european_aqi":27,"us_aqi":39,"pm2_5":4.9,"pm10":4.9,"ozone":74.0,"nitrogen_dioxide":10.5,"alder_pollen":null,"birch_pollen":null,"grass_pollen":null,"mugwort_pollen":null,"olive_pollen":null,"ragweed_pollen":null}}
        """;

    // Made up, for a spring day in southern Europe with plenty in the air.
    private const string Spring = """
        {"current":{"time":"2026-05-02T13:00","european_aqi":64,"us_aqi":158,"pm2_5":38.6,"pm10":52.1,"ozone":131.0,"nitrogen_dioxide":22.0,
                    "alder_pollen":3.2,"birch_pollen":72.5,"grass_pollen":61.0,"mugwort_pollen":0.4,"olive_pollen":180.0,"ragweed_pollen":7.0}}
        """;

    private static readonly AirQualityRequest LiverpoolPlace = new(53.43, -2.91, AqiScale.Automatic);
    private static readonly AirQualityRequest NewYorkPlace = new(40.7, -74.0, AqiScale.Automatic);
    private static readonly DateTimeOffset Evening = new(2026, 10, 12, 20, 5, 0, TimeSpan.Zero);

    [Fact]
    public void A_report_reads_the_indices_particles_gases_and_pollen()
    {
        var report = AirQualityReport.Parse(Liverpool);

        Assert.Equal(26, report.EuropeanAqi);
        Assert.Equal(33, report.UsAqi);
        Assert.Equal(4.2, report.Pm25);
        Assert.Equal(8.8, report.Pm10);
        Assert.Equal(42, report.Ozone);
        Assert.Equal(14.5, report.NitrogenDioxide);
        Assert.All(Enum.GetValues<PollenType>(), type => Assert.Equal(0.0, report.Pollen(type)));
    }

    [Fact]
    public void Pollen_is_unknown_outside_europe_and_so_is_anything_left_out()
    {
        var newYork = AirQualityReport.Parse(NewYork);
        Assert.Equal(39, newYork.UsAqi);
        Assert.All(Enum.GetValues<PollenType>(), type => Assert.Null(newYork.Pollen(type)));
        Assert.All(Enum.GetValues<PollenGroup>(), group => Assert.Null(AirQualityReadings.Count(newYork, group)));
        Assert.All(Enum.GetValues<PollenGroup>(), group => Assert.Null(AirQualityReadings.Level(newYork, group)));
        Assert.Null(AirQualityReadings.PollenSummary(newYork));

        var gaps = AirQualityReport.Parse("""{ "current": { "time": "2026-10-12T21:00", "european_aqi": 31, "pm10": null, "grass_pollen": 2.0 } }""");
        Assert.Equal(31, gaps.EuropeanAqi);
        Assert.Null(gaps.UsAqi);
        Assert.Null(gaps.Pm10);
        Assert.Null(gaps.Ozone);
        Assert.Equal(2, gaps.Grass);
        Assert.Null(gaps.Birch);
        Assert.Null(AirQualityReadings.Count(gaps, PollenGroup.Tree));
    }

    [Fact]
    public void Something_that_isnt_a_report_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => AirQualityReport.Parse("""{ "reason": "Latitude must be in range of -90 to 90°. Given: 95.0.", "error": true }"""));
        Assert.ThrowsAny<Exception>(() => AirQualityReport.Parse("not json"));
        Assert.ThrowsAny<Exception>(() => AirQualityReport.Parse("[]"));
    }

    [Fact]
    public void One_small_request_asks_only_for_what_is_shown_now()
    {
        var url = OpenMeteo.AirQualityUrl(LiverpoolPlace);
        Assert.Equal("https://air-quality-api.open-meteo.com/v1/air-quality?latitude=53.43&longitude=-2.91"
            + "&current=european_aqi,us_aqi,pm2_5,pm10,ozone,nitrogen_dioxide,alder_pollen,birch_pollen,olive_pollen,grass_pollen,mugwort_pollen,ragweed_pollen", url);
        // The scale doesn't change what's asked for: both indices come every time.
        Assert.Equal(url, OpenMeteo.AirQualityUrl(LiverpoolPlace with { Scale = AqiScale.Us }));
    }

    [Theory]
    [InlineData(0.0, "Good")]
    [InlineData(20.0, "Good")]
    [InlineData(20.4, "Good")]
    [InlineData(21.0, "Fair")]
    [InlineData(40.0, "Fair")]
    [InlineData(41.0, "Moderate")]
    [InlineData(60.0, "Moderate")]
    [InlineData(61.0, "Poor")]
    [InlineData(80.0, "Poor")]
    [InlineData(81.0, "Very poor")]
    [InlineData(100.0, "Very poor")]
    [InlineData(101.0, "Extremely poor")]
    [InlineData(340.0, "Extremely poor")]
    [InlineData(null, null)]
    public void The_european_index_has_the_eeas_words(double? index, string? level) =>
        Assert.Equal(level, AirQualityReadings.EuropeanLevel(index));

    [Theory]
    [InlineData(0.0, "Good")]
    [InlineData(50.0, "Good")]
    [InlineData(51.0, "Moderate")]
    [InlineData(100.0, "Moderate")]
    [InlineData(101.0, "Unhealthy for sensitive groups")]
    [InlineData(150.0, "Unhealthy for sensitive groups")]
    [InlineData(151.0, "Unhealthy")]
    [InlineData(200.0, "Unhealthy")]
    [InlineData(201.0, "Very unhealthy")]
    [InlineData(300.0, "Very unhealthy")]
    [InlineData(301.0, "Hazardous")]
    [InlineData(500.0, "Hazardous")]
    [InlineData(null, null)]
    public void The_us_index_has_the_epas_words(double? index, string? level) =>
        Assert.Equal(level, AirQualityReadings.UsLevel(index));

    [Theory]
    // MeteoSwiss's bands for each type, at the edges.
    [InlineData(PollenType.Alder, 0.4, PollenLevel.None)]
    [InlineData(PollenType.Alder, 0.5, PollenLevel.Low)]
    [InlineData(PollenType.Alder, 10.0, PollenLevel.Low)]
    [InlineData(PollenType.Alder, 11.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Alder, 69.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Alder, 70.0, PollenLevel.High)]
    [InlineData(PollenType.Alder, 249.0, PollenLevel.High)]
    [InlineData(PollenType.Alder, 250.0, PollenLevel.VeryHigh)]
    [InlineData(PollenType.Birch, 0.0, PollenLevel.None)]
    [InlineData(PollenType.Birch, 1.0, PollenLevel.Low)]
    [InlineData(PollenType.Birch, 11.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Birch, 70.0, PollenLevel.High)]
    [InlineData(PollenType.Birch, 299.0, PollenLevel.High)]
    [InlineData(PollenType.Birch, 300.0, PollenLevel.VeryHigh)]
    [InlineData(PollenType.Grass, 1.0, PollenLevel.Low)]
    [InlineData(PollenType.Grass, 19.0, PollenLevel.Low)]
    [InlineData(PollenType.Grass, 20.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Grass, 49.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Grass, 50.0, PollenLevel.High)]
    [InlineData(PollenType.Grass, 149.0, PollenLevel.High)]
    [InlineData(PollenType.Grass, 150.0, PollenLevel.VeryHigh)]
    [InlineData(PollenType.Mugwort, 5.0, PollenLevel.Low)]
    [InlineData(PollenType.Mugwort, 6.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Mugwort, 15.0, PollenLevel.High)]
    [InlineData(PollenType.Mugwort, 50.0, PollenLevel.VeryHigh)]
    [InlineData(PollenType.Ragweed, 5.0, PollenLevel.Low)]
    [InlineData(PollenType.Ragweed, 6.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Ragweed, 11.0, PollenLevel.High)]
    [InlineData(PollenType.Ragweed, 39.0, PollenLevel.High)]
    [InlineData(PollenType.Ragweed, 40.0, PollenLevel.VeryHigh)]
    // The Spanish Aerobiology Network's for olive, which stop at high.
    [InlineData(PollenType.Olive, 50.0, PollenLevel.Low)]
    [InlineData(PollenType.Olive, 51.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Olive, 200.0, PollenLevel.Moderate)]
    [InlineData(PollenType.Olive, 201.0, PollenLevel.High)]
    [InlineData(PollenType.Olive, 5000.0, PollenLevel.High)]
    [InlineData(PollenType.Grass, null, null)]
    public void Each_pollen_type_has_its_own_bands(PollenType type, double? count, PollenLevel? level) =>
        Assert.Equal(level, AirQualityReadings.Level(type, count));

    [Fact]
    public void Groups_add_their_types_up_and_take_the_highest_level()
    {
        var report = AirQualityReport.Parse(Spring);

        Assert.Equal(61, AirQualityReadings.Count(report, PollenGroup.Grass));
        Assert.Equal(PollenLevel.High, AirQualityReadings.Level(report, PollenGroup.Grass));
        // 255.7 grains of tree pollen, mostly olive, but it's the birch that's high: olive is only moderate at 180.
        Assert.Equal(3.2 + 72.5 + 180, AirQualityReadings.Count(report, PollenGroup.Tree)!.Value, 6);
        Assert.Equal(PollenLevel.High, AirQualityReadings.Level(report, PollenGroup.Tree));
        Assert.Equal(7.4, AirQualityReadings.Count(report, PollenGroup.Weed)!.Value, 6);
        Assert.Equal(PollenLevel.Moderate, AirQualityReadings.Level(report, PollenGroup.Weed));

        Assert.Equal("Very high", AirQualityReadings.Describe(PollenLevel.VeryHigh));
        Assert.Equal("None", AirQualityReadings.Describe(PollenLevel.None));
    }

    [Fact]
    public void The_summary_names_the_group_at_the_highest_level()
    {
        Assert.Equal("Grass and tree high", AirQualityReadings.PollenSummary(AirQualityReport.Parse(Spring)));
        Assert.Equal("Grass very high", AirQualityReadings.PollenSummary(new AirQualityReport { Grass = 160, Birch = 12, Mugwort = 1 }));
        Assert.Equal("Weed low", AirQualityReadings.PollenSummary(new AirQualityReport { Grass = 0, Birch = 0, Mugwort = 2 }));
        // The level counts, not the number of grains: 19 of grass is low, but 7 of ragweed is moderate.
        Assert.Equal("Weed moderate", AirQualityReadings.PollenSummary(new AirQualityReport { Grass = 19, Ragweed = 7 }));
        Assert.Equal("Grass, tree and weed moderate", AirQualityReadings.PollenSummary(new AirQualityReport { Grass = 25, Alder = 30, Ragweed = 8 }));
        Assert.Equal("None", AirQualityReadings.PollenSummary(AirQualityReport.Parse(Liverpool)));
        Assert.Null(AirQualityReadings.PollenSummary(new AirQualityReport { EuropeanAqi = 20 }));
    }

    [Theory]
    [InlineData(53.43, -2.91, AqiScale.European)]   // Liverpool
    [InlineData(64.15, -21.94, AqiScale.European)]  // Reykjavík
    [InlineData(41.01, 28.98, AqiScale.European)]   // Istanbul
    [InlineData(36.8, 10.18, AqiScale.European)]    // Tunis, inside CAMS's European area
    [InlineData(69.65, 18.96, AqiScale.European)]   // Tromsø
    [InlineData(40.7, -74.0, AqiScale.Us)]          // New York
    [InlineData(35.68, 139.69, AqiScale.Us)]        // Tokyo
    [InlineData(-33.87, 151.21, AqiScale.Us)]       // Sydney
    [InlineData(28.29, -16.63, AqiScale.Us)]        // Tenerife, just south of it
    [InlineData(55.75, 49.1, AqiScale.Us)]          // Kazan, just east of it
    public void Automatically_places_in_europe_get_the_european_index(double latitude, double longitude, AqiScale scale)
    {
        Assert.Equal(scale, AirQualityReadings.ScaleFor(AqiScale.Automatic, latitude, longitude));
        // Choosing one keeps it wherever the place is.
        Assert.Equal(AqiScale.Us, AirQualityReadings.ScaleFor(AqiScale.Us, latitude, longitude));
        Assert.Equal(AqiScale.European, AirQualityReadings.ScaleFor(AqiScale.European, latitude, longitude));
    }

    [Fact]
    public async Task Nothing_is_fetched_or_shown_while_it_is_off()
    {
        var fetches = 0;
        AirQualityRequest? wanted = null;
        var time = new FakeTime(Evening);
        var provider = new AirQualityProvider(() => wanted, (_, _) =>
        {
            fetches++;
            return Task.FromResult(AirQualityReport.Parse(Liverpool));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        for (var i = 0; i < 5; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromHours(2));
        }
        Assert.Equal(0, fetches);
        Assert.All(store.Definitions.Where(d => d.Category == "Air quality"), d => Assert.False(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));

        // Turned on: straight away.
        wanted = LiverpoolPlace;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("Fair", Format(store, AirQualityProvider.Level));

        // Turned off again: the readings go and nothing more is fetched.
        wanted = null;
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromHours(2));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.All(store.Definitions.Where(d => d.Category == "Air quality"), d => Assert.False(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));
    }

    [Fact]
    public async Task Every_reading_is_published_on_the_chosen_scale()
    {
        var wanted = LiverpoolPlace;
        var provider = new AirQualityProvider(() => wanted, (_, _) => Task.FromResult(AirQualityReport.Parse(Liverpool)), new FakeTime(Evening));
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        // Liverpool is in Europe, so automatically it's the European index.
        Assert.Equal("26", Format(store, AirQualityProvider.Index));
        Assert.Equal("Fair", Format(store, AirQualityProvider.Level));
        Assert.Equal("26", Format(store, AirQualityProvider.European));
        Assert.Equal("33", Format(store, AirQualityProvider.Us));
        Assert.Equal("4 µg/m³", Format(store, AirQualityProvider.Pm25));
        Assert.Equal("9 µg/m³", Format(store, AirQualityProvider.Pm10));
        Assert.Equal("42 µg/m³", Format(store, AirQualityProvider.Ozone));
        Assert.Equal("15 µg/m³", Format(store, AirQualityProvider.NitrogenDioxide));
        Assert.Equal("0 grains/m³", Format(store, AirQualityProvider.GrassPollen));
        Assert.Equal("None", Format(store, AirQualityProvider.GrassLevel));
        Assert.Equal("0 grains/m³", Format(store, AirQualityProvider.TreePollen));
        Assert.Equal("None", Format(store, AirQualityProvider.TreeLevel));
        Assert.Equal("None", Format(store, AirQualityProvider.WeedLevel));
        Assert.Equal("None", Format(store, AirQualityProvider.Pollen));
        Assert.True(store.TryGet(AirQualityProvider.Index, out var index));
        Assert.Equal(100, index.Definition.Max);

        // Another scale applies at the next sample, without fetching again.
        wanted = LiverpoolPlace with { Scale = AqiScale.Us };
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("33", Format(store, AirQualityProvider.Index));
        Assert.Equal("Good", Format(store, AirQualityProvider.Level));
        Assert.True(store.TryGet(AirQualityProvider.Index, out index));
        Assert.Equal(500, index.Definition.Max);
    }

    [Fact]
    public void Where_pollen_isnt_counted_its_readings_have_no_value()
    {
        var provider = new AirQualityProvider(() => null);
        var store = new MetricStore();
        provider.Initialize(store);
        provider.Publish(store, AirQualityReport.Parse(NewYork), NewYorkPlace);

        // New York is outside Europe, so automatically it's the US index.
        Assert.Equal("39", Format(store, AirQualityProvider.Index));
        Assert.Equal("Good", Format(store, AirQualityProvider.Level));
        Assert.Equal("74 µg/m³", Format(store, AirQualityProvider.Ozone));
        foreach (var id in new[] { AirQualityProvider.Pollen, AirQualityProvider.GrassPollen, AirQualityProvider.GrassLevel, AirQualityProvider.TreePollen,
            AirQualityProvider.TreeLevel, AirQualityProvider.WeedPollen, AirQualityProvider.WeedLevel })
        {
            Assert.Equal(MetricFormatter.Missing, Format(store, id));
        }

        provider.Publish(store, AirQualityReport.Parse(Spring), LiverpoolPlace);
        Assert.Equal("Grass and tree high", Format(store, AirQualityProvider.Pollen));
        Assert.Equal("High", Format(store, AirQualityProvider.TreeLevel));
        Assert.Equal("256 grains/m³", Format(store, AirQualityProvider.TreePollen));
        Assert.Equal("Moderate", Format(store, AirQualityProvider.WeedLevel));
        Assert.Equal("Poor", Format(store, AirQualityProvider.Level));
    }

    [Fact]
    public async Task Readings_refresh_hourly_retry_failures_expire_and_follow_the_place()
    {
        var time = new FakeTime(Evening);
        var requests = new List<AirQualityRequest>();
        var fail = false;
        var wanted = LiverpoolPlace;
        var provider = new AirQualityProvider(() => wanted, (request, _) =>
        {
            requests.Add(request);
            return fail ? Task.FromException<AirQualityReport>(new HttpRequestException("offline")) : Task.FromResult(AirQualityReport.Parse(Liverpool));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(requests);
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests.Count);
        var lastFetched = time.GetUtcNow();

        // Due again, but offline: the readings stay and it tries again a few minutes later.
        fail = true;
        time.Advance(AirQualityProvider.RefreshEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests.Count);
        Assert.Equal("26", Format(store, AirQualityProvider.Index));
        time.Advance(TimeSpan.FromMinutes(4));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests.Count);
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4, requests.Count);

        // Readings stay for three hours after the last fetch that worked; after that, old readings are worse than none.
        time.Advance(lastFetched + AirQualityProvider.KeepFor - time.GetUtcNow());
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("26", Format(store, AirQualityProvider.Index));
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(MetricFormatter.Missing, Format(store, AirQualityProvider.Index));
        Assert.Equal(MetricFormatter.Missing, Format(store, AirQualityProvider.Level));

        // A new place fetches straight away.
        fail = false;
        var before = requests.Count;
        wanted = NewYorkPlace;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(before + 1, requests.Count);
        Assert.Equal(NewYorkPlace, requests[^1]);
    }

    [Fact]
    public async Task Refreshes_come_just_after_the_hour_when_the_new_readings_are_there()
    {
        // Turned on at two minutes to the hour: it fetches then, and again just after the hour rather than an hour later.
        var time = new FakeTime(new DateTimeOffset(2026, 10, 12, 10, 58, 0, TimeSpan.Zero));
        var fetches = new List<DateTimeOffset>();
        var provider = new AirQualityProvider(() => LiverpoolPlace, (_, _) =>
        {
            fetches.Add(time.GetUtcNow());
            return Task.FromResult(AirQualityReport.Parse(Liverpool));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        for (var i = 0; i < 4 * 60 * 2; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(30));
        }

        DateTimeOffset[] expected =
        [
            new(2026, 10, 12, 10, 58, 0, TimeSpan.Zero), new(2026, 10, 12, 11, 5, 0, TimeSpan.Zero), new(2026, 10, 12, 12, 5, 0, TimeSpan.Zero),
            new(2026, 10, 12, 13, 5, 0, TimeSpan.Zero), new(2026, 10, 12, 14, 5, 0, TimeSpan.Zero),
        ];
        Assert.Equal(expected, fetches);
        // Open-Meteo's hours are UTC's, whatever the PC's own time zone.
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 11, 5, 0, TimeSpan.Zero), AirQualityProvider.NextHour(new DateTimeOffset(2026, 10, 12, 16, 28, 0, TimeSpan.FromHours(5.5))));
    }

    [Fact]
    public async Task A_new_place_never_shows_the_old_places_readings()
    {
        var time = new FakeTime(Evening);
        var fail = false;
        var wanted = LiverpoolPlace;
        var provider = new AirQualityProvider(() => wanted, (_, _) =>
            fail ? Task.FromException<AirQualityReport>(new HttpRequestException("offline")) : Task.FromResult(AirQualityReport.Parse(Liverpool)), time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("26", Format(store, AirQualityProvider.Index));

        // Moved to New York minutes later, while offline: Liverpool's fresh readings would be wrong there.
        fail = true;
        wanted = NewYorkPlace;
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        foreach (var id in new[] { AirQualityProvider.Index, AirQualityProvider.Level, AirQualityProvider.European, AirQualityProvider.Pm25, AirQualityProvider.Pollen,
            AirQualityProvider.GrassLevel })
        {
            Assert.Equal(MetricFormatter.Missing, Format(store, id));
        }
    }

    [Fact]
    public async Task It_runs_on_the_weathers_loop_rather_than_one_of_its_own()
    {
        var fetches = 0;
        AirQualityRequest? wanted = null;
        var airQuality = new AirQualityProvider(() => wanted, (_, _) =>
        {
            fetches++;
            return Task.FromResult(AirQualityReport.Parse(Liverpool));
        }, new FakeTime(Evening));
        var weather = new WeatherProvider(() => null, airQuality: airQuality);
        var store = new MetricStore();
        weather.Initialize(store);

        // Its readings are there to pick in the editor, but nothing is fetched while it's off.
        Assert.Contains(store.Definitions, d => d.Id == AirQualityProvider.Pollen);
        await weather.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, fetches);

        wanted = LiverpoolPlace;
        await weather.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("Fair", Format(store, AirQualityProvider.Level));
    }

    [Fact]
    public async Task A_failed_first_fetch_shows_nothing_until_one_works()
    {
        var time = new FakeTime(Evening);
        var fail = true;
        var provider = new AirQualityProvider(() => LiverpoolPlace, (_, _) =>
            fail ? Task.FromException<AirQualityReport>(new System.Text.Json.JsonException("not air quality")) : Task.FromResult(AirQualityReport.Parse(Liverpool)), time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(MetricFormatter.Missing, Format(store, AirQualityProvider.Index));

        fail = false;
        time.Advance(AirQualityProvider.RetryAfter);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("26", Format(store, AirQualityProvider.Index));
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

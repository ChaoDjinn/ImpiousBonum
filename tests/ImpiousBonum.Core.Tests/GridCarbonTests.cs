using System.Globalization;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class GridCarbonTests : IDisposable
{
    // The Carbon Intensity API's answer for Great Britain from noon on 16 October 2026, fetched that afternoon: the
    // half-hours gone have an estimate of what they were as well as the forecast.
    private const string GreatBritain = """
        {"data":[
        {"from":"2026-10-16T11:30Z","to":"2026-10-16T12:00Z","intensity":{"forecast":73,"actual":72,"index":"low"}},
        {"from":"2026-10-16T12:00Z","to":"2026-10-16T12:30Z","intensity":{"forecast":70,"actual":74,"index":"low"}},
        {"from":"2026-10-16T12:30Z","to":"2026-10-16T13:00Z","intensity":{"forecast":70,"actual":78,"index":"low"}},
        {"from":"2026-10-16T13:00Z","to":"2026-10-16T13:30Z","intensity":{"forecast":69,"actual":77,"index":"low"}},
        {"from":"2026-10-16T13:30Z","to":"2026-10-16T14:00Z","intensity":{"forecast":71,"actual":77,"index":"low"}},
        {"from":"2026-10-16T14:00Z","to":"2026-10-16T14:30Z","intensity":{"forecast":77,"actual":88,"index":"low"}},
        {"from":"2026-10-16T14:30Z","to":"2026-10-16T15:00Z","intensity":{"forecast":97,"actual":99,"index":"moderate"}},
        {"from":"2026-10-16T15:00Z","to":"2026-10-16T15:30Z","intensity":{"forecast":109,"actual":115,"index":"moderate"}},
        {"from":"2026-10-16T15:30Z","to":"2026-10-16T16:00Z","intensity":{"forecast":118,"actual":127,"index":"moderate"}},
        {"from":"2026-10-16T16:00Z","to":"2026-10-16T16:30Z","intensity":{"forecast":125,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T16:30Z","to":"2026-10-16T17:00Z","intensity":{"forecast":141,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T17:00Z","to":"2026-10-16T17:30Z","intensity":{"forecast":150,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T17:30Z","to":"2026-10-16T18:00Z","intensity":{"forecast":149,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T18:00Z","to":"2026-10-16T18:30Z","intensity":{"forecast":145,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T18:30Z","to":"2026-10-16T19:00Z","intensity":{"forecast":140,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T19:00Z","to":"2026-10-16T19:30Z","intensity":{"forecast":132,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T19:30Z","to":"2026-10-16T20:00Z","intensity":{"forecast":127,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T20:00Z","to":"2026-10-16T20:30Z","intensity":{"forecast":116,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T20:30Z","to":"2026-10-16T21:00Z","intensity":{"forecast":104,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T21:00Z","to":"2026-10-16T21:30Z","intensity":{"forecast":92,"actual":null,"index":"moderate"}},
        {"from":"2026-10-16T21:30Z","to":"2026-10-16T22:00Z","intensity":{"forecast":82,"actual":null,"index":"low"}},
        {"from":"2026-10-16T22:00Z","to":"2026-10-16T22:30Z","intensity":{"forecast":75,"actual":null,"index":"low"}},
        {"from":"2026-10-16T22:30Z","to":"2026-10-16T23:00Z","intensity":{"forecast":73,"actual":null,"index":"low"}},
        {"from":"2026-10-16T23:00Z","to":"2026-10-16T23:30Z","intensity":{"forecast":72,"actual":null,"index":"low"}},
        {"from":"2026-10-16T23:30Z","to":"2026-10-17T00:00Z","intensity":{"forecast":71,"actual":null,"index":"low"}},
        {"from":"2026-10-17T00:00Z","to":"2026-10-17T00:30Z","intensity":{"forecast":71,"actual":null,"index":"low"}},
        {"from":"2026-10-17T00:30Z","to":"2026-10-17T01:00Z","intensity":{"forecast":66,"actual":null,"index":"low"}},
        {"from":"2026-10-17T01:00Z","to":"2026-10-17T01:30Z","intensity":{"forecast":66,"actual":null,"index":"low"}},
        {"from":"2026-10-17T01:30Z","to":"2026-10-17T02:00Z","intensity":{"forecast":68,"actual":null,"index":"low"}},
        {"from":"2026-10-17T02:00Z","to":"2026-10-17T02:30Z","intensity":{"forecast":69,"actual":null,"index":"low"}},
        {"from":"2026-10-17T02:30Z","to":"2026-10-17T03:00Z","intensity":{"forecast":70,"actual":null,"index":"low"}},
        {"from":"2026-10-17T03:00Z","to":"2026-10-17T03:30Z","intensity":{"forecast":69,"actual":null,"index":"low"}},
        {"from":"2026-10-17T03:30Z","to":"2026-10-17T04:00Z","intensity":{"forecast":70,"actual":null,"index":"low"}},
        {"from":"2026-10-17T04:00Z","to":"2026-10-17T04:30Z","intensity":{"forecast":71,"actual":null,"index":"low"}},
        {"from":"2026-10-17T04:30Z","to":"2026-10-17T05:00Z","intensity":{"forecast":73,"actual":null,"index":"low"}},
        {"from":"2026-10-17T05:00Z","to":"2026-10-17T05:30Z","intensity":{"forecast":75,"actual":null,"index":"low"}},
        {"from":"2026-10-17T05:30Z","to":"2026-10-17T06:00Z","intensity":{"forecast":82,"actual":null,"index":"low"}},
        {"from":"2026-10-17T06:00Z","to":"2026-10-17T06:30Z","intensity":{"forecast":83,"actual":null,"index":"low"}},
        {"from":"2026-10-17T06:30Z","to":"2026-10-17T07:00Z","intensity":{"forecast":84,"actual":null,"index":"low"}},
        {"from":"2026-10-17T07:00Z","to":"2026-10-17T07:30Z","intensity":{"forecast":88,"actual":null,"index":"low"}},
        {"from":"2026-10-17T07:30Z","to":"2026-10-17T08:00Z","intensity":{"forecast":85,"actual":null,"index":"low"}},
        {"from":"2026-10-17T08:00Z","to":"2026-10-17T08:30Z","intensity":{"forecast":82,"actual":null,"index":"low"}},
        {"from":"2026-10-17T08:30Z","to":"2026-10-17T09:00Z","intensity":{"forecast":81,"actual":null,"index":"low"}},
        {"from":"2026-10-17T09:00Z","to":"2026-10-17T09:30Z","intensity":{"forecast":80,"actual":null,"index":"low"}},
        {"from":"2026-10-17T09:30Z","to":"2026-10-17T10:00Z","intensity":{"forecast":81,"actual":null,"index":"low"}},
        {"from":"2026-10-17T10:00Z","to":"2026-10-17T10:30Z","intensity":{"forecast":81,"actual":null,"index":"low"}},
        {"from":"2026-10-17T10:30Z","to":"2026-10-17T11:00Z","intensity":{"forecast":79,"actual":null,"index":"low"}},
        {"from":"2026-10-17T11:00Z","to":"2026-10-17T11:30Z","intensity":{"forecast":78,"actual":null,"index":"low"}},
        {"from":"2026-10-17T11:30Z","to":"2026-10-17T12:00Z","intensity":{"forecast":78,"actual":null,"index":"low"}}
        ]}
        """;

    // The forecast for LS1 (Yorkshire) from 16:20 the same day, cut to its first three half-hours: a region's comes with
    // the region around it, and the fuels each half-hour's electricity is made from.
    private const string Yorkshire = """
        {"data":{"regionid":5,"dnoregion":"NPG Yorkshire","shortname":"Yorkshire","postcode":"LS1","data":[
        {"from":"2026-10-16T16:00Z","to":"2026-10-16T16:30Z","intensity":{"forecast":106,"index":"moderate"},"generationmix":[{"fuel":"biomass","perc":45.9},{"fuel":"coal","perc":0},{"fuel":"imports","perc":5.7},{"fuel":"gas","perc":12.9},{"fuel":"nuclear","perc":4.1},{"fuel":"other","perc":0},{"fuel":"hydro","perc":0},{"fuel":"solar","perc":1.2},{"fuel":"wind","perc":30.3}]},
        {"from":"2026-10-16T16:30Z","to":"2026-10-16T17:00Z","intensity":{"forecast":99,"index":"moderate"},"generationmix":[{"fuel":"biomass","perc":44.5},{"fuel":"coal","perc":0},{"fuel":"imports","perc":6.2},{"fuel":"gas","perc":11.6},{"fuel":"nuclear","perc":4.3},{"fuel":"other","perc":0},{"fuel":"hydro","perc":0},{"fuel":"solar","perc":0.4},{"fuel":"wind","perc":33.1}]},
        {"from":"2026-10-16T17:00Z","to":"2026-10-16T17:30Z","intensity":{"forecast":94,"index":"moderate"},"generationmix":[{"fuel":"biomass","perc":43.2},{"fuel":"coal","perc":0},{"fuel":"imports","perc":6.8},{"fuel":"gas","perc":10.6},{"fuel":"nuclear","perc":4.6},{"fuel":"other","perc":0},{"fuel":"hydro","perc":0},{"fuel":"solar","perc":0},{"fuel":"wind","perc":34.8}]}
        ]}}
        """;

    // LS1's region for the half-hour now alone, which is an array of one region rather than the region itself.
    private const string YorkshireNow = """
        {"data":[{"regionid":5,"dnoregion":"NPG Yorkshire","shortname":"Yorkshire","postcode":"LS1","data":[{"from":"2026-10-16T16:00Z","to":"2026-10-16T16:30Z","intensity":{"forecast":106,"index":"moderate"},"generationmix":[{"fuel":"biomass","perc":45.9},{"fuel":"coal","perc":0},{"fuel":"imports","perc":5.7},{"fuel":"gas","perc":12.9},{"fuel":"nuclear","perc":4.1},{"fuel":"other","perc":0},{"fuel":"hydro","perc":0},{"fuel":"solar","perc":1.2},{"fuel":"wind","perc":30.3}]}]}]}
        """;

    /// <summary>When the samples were fetched.</summary>
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 16, 16, 42, 0, TimeSpan.Zero);

    /// <summary>British Summer Time, all year, so the tests don't depend on the date.</summary>
    private static readonly TimeZoneInfo Bst = TimeZoneInfo.CreateCustomTimeZone("Test BST", TimeSpan.FromHours(1), "Test BST", "Test BST");

    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public GridCarbonTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    [Fact]
    public void Great_britains_forecast_reads_every_half_hour_with_the_estimate_where_there_is_one()
    {
        var forecast = CarbonForecast.Parse(GreatBritain);

        Assert.Equal(CarbonForecast.GreatBritain, forecast.Region);
        Assert.Equal(49, forecast.Periods.Count);
        var first = forecast.Periods[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 11, 30, 0, TimeSpan.Zero), first.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), first.To);
        Assert.Equal(73, first.Forecast);
        Assert.Equal(72, first.Actual);
        Assert.Equal(72, first.Intensity);
        Assert.Equal("low", first.Index);
        // Ahead, there's only the forecast.
        Assert.Null(forecast.Periods[^1].Actual);
        Assert.Equal(78, forecast.Periods[^1].Intensity);
    }

    [Fact]
    public void A_regions_forecast_has_its_name()
    {
        var forecast = CarbonForecast.Parse(Yorkshire);
        Assert.Equal("Yorkshire", forecast.Region);
        Assert.Equal([106.0, 99.0, 94.0], forecast.Periods.Select(p => p.Intensity!.Value));
        Assert.All(forecast.Periods, p => Assert.Null(p.Actual));

        var now = CarbonForecast.Parse(YorkshireNow);
        Assert.Equal("Yorkshire", now.Region);
        Assert.Equal(106, Assert.Single(now.Periods).Intensity);
    }

    [Fact]
    public void Something_that_isnt_a_forecast_is_rejected()
    {
        // What the forecast sends for a postcode it doesn't know, and what the region now sends.
        Assert.ThrowsAny<Exception>(() => CarbonForecast.Parse("null"));
        Assert.ThrowsAny<Exception>(() => CarbonForecast.Parse("""{ "error": { "code": "400 Bad Request", "message": "No postcode match can be found." } }"""));
        Assert.ThrowsAny<Exception>(() => CarbonForecast.Parse("""{ "data": [] }"""));
        Assert.ThrowsAny<Exception>(() => CarbonForecast.Parse("not json"));
    }

    [Fact]
    public void Now_is_the_half_hour_its_in_and_the_lowest_is_the_first_of_the_least()
    {
        var forecast = CarbonForecast.Parse(GreatBritain);

        var now = forecast.At(Afternoon)!;
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 16, 30, 0, TimeSpan.Zero), now.From);
        Assert.Equal(141, now.Intensity);

        // 66 from half past midnight and from one, UTC: the earlier, at half past one in the morning in Britain.
        var lowest = forecast.Lowest(Afternoon)!;
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 0, 30, 0, TimeSpan.Zero), lowest.From);
        Assert.Equal("Lowest around 01:30", CarbonIntensity.Lowest(lowest, Afternoon, Bst));

        // In the lowest half-hour, it's now.
        var night = new DateTimeOffset(2026, 10, 17, 0, 40, 0, TimeSpan.Zero);
        Assert.Equal("Lowest now", CarbonIntensity.Lowest(forecast.Lowest(night)!, night, Bst));

        // Past the end of the forecast, there's nothing to go on.
        Assert.Null(forecast.At(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero)));
        Assert.Null(forecast.Lowest(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("very low", "Very low")]
    [InlineData("moderate", "Moderate")]
    [InlineData("VERY HIGH", "Very high")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Levels_have_a_capital(string? index, string? level) => Assert.Equal(level, CarbonIntensity.Level(index));

    [Theory]
    [InlineData("LS1", "LS1")]
    [InlineData("ls1 4ap", "LS1")]
    [InlineData("SW1A 1AA", "SW1A")]
    [InlineData("M1 1AE", "M1")]
    [InlineData("LS14", "LS14")]
    [InlineData("B338TH", "B33")]
    [InlineData(" eh1 ", "EH1")]
    [InlineData("Leeds", null)]
    [InlineData("12345", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_a_postcodes_first_half_is_kept(string? postcode, string? area) => Assert.Equal(area, CarbonIntensity.Area(postcode));

    [Fact]
    public void Northern_ireland_isnt_great_britain()
    {
        Assert.True(CarbonIntensity.InNorthernIreland("BT1"));
        Assert.False(CarbonIntensity.InNorthernIreland("B1"));
    }

    [Fact]
    public void One_request_gets_now_and_the_day_ahead_sending_nothing_but_the_area()
    {
        Assert.Equal("https://api.carbonintensity.org.uk/intensity/2026-10-16T16:42Z/fw24h", CarbonIntensity.ForecastUrl(new GridCarbonRequest(null), Afternoon));
        Assert.Equal("https://api.carbonintensity.org.uk/regional/intensity/2026-10-16T16:42Z/fw24h/postcode/LS1",
            CarbonIntensity.ForecastUrl(new GridCarbonRequest("LS1"), Afternoon.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public async Task Nothing_is_fetched_or_shown_while_it_is_off()
    {
        var fetches = 0;
        GridCarbonRequest? wanted = null;
        var time = new FakeTime(Afternoon);
        var provider = new GridCarbonProvider(() => wanted, (_, _) =>
        {
            fetches++;
            return Task.FromResult(CarbonForecast.Parse(GreatBritain));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        for (var i = 0; i < 5; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromHours(1));
        }
        Assert.Equal(0, fetches);
        Assert.All(store.Definitions.Where(d => d.Category == "Grid carbon"), d => Assert.False(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));

        // Turned on: straight away.
        time.Advance(Afternoon - time.GetUtcNow());
        wanted = new GridCarbonRequest(null);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("141 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));
        Assert.Equal("Moderate", Format(store, GridCarbonProvider.Index));
        Assert.Equal("Lowest around 01:30", Format(store, GridCarbonProvider.Best));
        Assert.Equal("Great Britain", Format(store, GridCarbonProvider.Region));

        // Turned off again: the readings go and nothing more is fetched.
        wanted = null;
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.All(store.Definitions.Where(d => d.Category == "Grid carbon"), d => Assert.False(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));
    }

    [Fact]
    public async Task Between_fetches_the_readings_move_on_through_the_forecast()
    {
        var time = new FakeTime(Afternoon);
        var provider = new GridCarbonProvider(() => new GridCarbonRequest(null), (_, _) => Task.FromResult(CarbonForecast.Parse(GreatBritain)), time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("141 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));

        // 17:02: the next half-hour's forecast.
        time.Advance(TimeSpan.FromMinutes(20));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("150 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));
    }

    [Fact]
    public async Task Readings_refresh_every_half_hour_retry_failures_expire_and_follow_the_area()
    {
        var time = new FakeTime(Afternoon);
        var requests = new List<GridCarbonRequest>();
        var fail = false;
        var wanted = new GridCarbonRequest(null);
        var provider = new GridCarbonProvider(() => wanted, (request, _) =>
        {
            requests.Add(request);
            return fail ? Task.FromException<CarbonForecast>(new HttpRequestException("offline"))
                : Task.FromResult(CarbonForecast.Parse(request.Area is null ? GreatBritain : Yorkshire));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(29));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(requests);
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests.Count);
        var lastFetched = time.GetUtcNow();

        // Due again, but offline: the readings stay and it tries again a few minutes later.
        fail = true;
        time.Advance(GridCarbonProvider.RefreshEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests.Count);
        Assert.Equal("149 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));
        time.Advance(TimeSpan.FromMinutes(4));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests.Count);
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4, requests.Count);

        // Readings stay for three hours after the last fetch that worked; after that, old readings are worse than none.
        time.Advance(lastFetched + GridCarbonProvider.KeepFor - time.GetUtcNow());
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Moderate", Format(store, GridCarbonProvider.Index));
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(MetricFormatter.Missing, Format(store, GridCarbonProvider.Intensity));
        Assert.Equal(MetricFormatter.Missing, Format(store, GridCarbonProvider.Best));

        // An area fetches straight away, and its region's figures replace Great Britain's.
        fail = false;
        time.Advance(Afternoon - time.GetUtcNow());
        var before = requests.Count;
        wanted = new GridCarbonRequest("LS1");
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(before + 1, requests.Count);
        Assert.Equal("LS1", requests[^1].Area);
        Assert.Equal("Yorkshire", Format(store, GridCarbonProvider.Region));
        Assert.Equal("99 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));
        Assert.Equal("Lowest around 18:00", Format(store, GridCarbonProvider.Best));
    }

    [Fact]
    public async Task A_new_area_never_shows_the_old_areas_readings()
    {
        var time = new FakeTime(Afternoon);
        var fail = false;
        var wanted = new GridCarbonRequest(null);
        var provider = new GridCarbonProvider(() => wanted, (_, _) =>
            fail ? Task.FromException<CarbonForecast>(new HttpRequestException("offline")) : Task.FromResult(CarbonForecast.Parse(GreatBritain)), time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("141 gCO₂/kWh", Format(store, GridCarbonProvider.Intensity));

        // Given an area while offline: Great Britain's fresh figures would be wrong for it.
        fail = true;
        wanted = new GridCarbonRequest("LS1");
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        foreach (var id in new[] { GridCarbonProvider.Intensity, GridCarbonProvider.Index, GridCarbonProvider.Best, GridCarbonProvider.Region })
            Assert.Equal(MetricFormatter.Missing, Format(store, id));
    }

    [Fact]
    public async Task It_runs_on_the_energys_loop_every_half_minute_without_holding_it_up()
    {
        var time = new FakeTime(Afternoon);
        var fetches = 0;
        var answer = new TaskCompletionSource<CarbonForecast>();
        var grid = new GridCarbonProvider(() => new GridCarbonRequest(null), (_, _) =>
        {
            fetches++;
            return answer.Task;
        }, time);
        var energy = new EnergyProvider(() => new EnergySettings(50, null, "£"), grid: grid, time: time);
        var store = new MetricStore();
        energy.Initialize(store);

        // Its readings are there to pick in the editor, and the first sample asks for the forecast...
        Assert.Contains(store.Definitions, d => d.Id == GridCarbonProvider.Best);
        await energy.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);

        // ...but the energy carries on each second while the answer is on its way.
        for (var i = 0; i < 40; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(energy.SampleAsync(store, CancellationToken.None).IsCompletedSuccessfully);
        }
        Assert.Equal(1, fetches);
        // The answer's taken up on another thread, as the test has a synchronisation context of its own.
        answer.SetResult(CarbonForecast.Parse(GreatBritain));
        Assert.True(SpinWait.SpinUntil(() => Format(store, GridCarbonProvider.Index) == "Moderate", TimeSpan.FromSeconds(10)));

        // Then it's sampled again every 30 seconds, without fetching until the half-hour's up.
        time.Advance(TimeSpan.FromMinutes(5));
        await energy.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
    }

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => Bst;
    }
}

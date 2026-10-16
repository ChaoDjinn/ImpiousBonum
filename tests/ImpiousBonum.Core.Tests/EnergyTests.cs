using System.Globalization;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Core.Tests;

public sealed class EnergyTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An hour ahead of UTC all year, so midnight there is 23:00 UTC.</summary>
    private static readonly TimeZoneInfo HourAhead = TimeZoneInfo.CreateCustomTimeZone("Test +1", TimeSpan.FromHours(1), "Test +1", "Test +1");

    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ImpiousBonumTests", Guid.NewGuid().ToString("N"));

    public EnergyTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previous;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Steady_power_adds_up_to_kilowatt_hours()
    {
        var meter = new EnergyMeter(TimeZoneInfo.Utc);
        for (var second = 0; second <= 3600; second++)
            meter.Add(Morning.AddSeconds(second), 400);

        Assert.Equal(new DateOnly(2026, 10, 16), meter.Totals!.Day);
        Assert.Equal(0.4, meter.Totals.Today, 9);
        Assert.Equal(0.4, meter.Totals.Month, 9);
    }

    [Fact]
    public void The_time_between_two_readings_counts_their_average()
    {
        var meter = new EnergyMeter(TimeZoneInfo.Utc);
        meter.Add(Morning, 100);
        meter.Add(Morning.AddSeconds(10), 300);

        // 200 W for ten seconds.
        Assert.Equal(200.0 * 10 / 3600 / 1000, meter.Totals!.Today, 12);
    }

    [Fact]
    public void Gaps_and_missing_readings_arent_counted()
    {
        var meter = new EnergyMeter(TimeZoneInfo.Utc);

        // Nothing to go on yet, so nothing is counted, and there are no totals to show.
        meter.Add(Morning, 400);
        Assert.Null(meter.Totals);

        // The PC was asleep, or the app held up: longer than the longest gap isn't counted, however much it was drawing.
        var now = Morning + EnergyMeter.LongestGap + TimeSpan.FromSeconds(1);
        meter.Add(now, 400);
        Assert.Null(meter.Totals);

        // The longest gap itself still counts.
        now += EnergyMeter.LongestGap;
        meter.Add(now, 400);
        Assert.Equal(400 * EnergyMeter.LongestGap.TotalHours / 1000, meter.Totals!.Today, 12);
        var counted = meter.Totals.Today;

        // A second without a reading (the sensor service restarting) leaves out the time either side of it.
        meter.Add(now.AddSeconds(1), null);
        meter.Add(now.AddSeconds(2), 400);
        Assert.Equal(counted, meter.Totals.Today);
        meter.Add(now.AddSeconds(3), 400);
        Assert.Equal(counted + 400.0 / 3600 / 1000, meter.Totals.Today, 12);

        // A clock put back doesn't count backwards.
        meter.Add(now, 400);
        Assert.Equal(counted + 400.0 / 3600 / 1000, meter.Totals.Today, 12);
    }

    [Fact]
    public void Midnight_starts_today_again_with_the_time_across_it_split_between_the_days()
    {
        // 23:59:30 to 00:00:30 on the PC's clock, an hour ahead of UTC, at 3.6 kW: 0.03 kWh either side of midnight.
        var meter = new EnergyMeter(HourAhead);
        meter.Restore(new EnergyTotals(new DateOnly(2026, 10, 16), 1.5, 20));
        var beforeMidnight = new DateTimeOffset(2026, 10, 16, 22, 59, 30, TimeSpan.Zero);
        meter.Add(beforeMidnight, 3600);
        Assert.Equal(new EnergyTotals(new DateOnly(2026, 10, 16), 1.5, 20), meter.Totals);

        meter.Add(beforeMidnight.AddMinutes(1), 3600);
        Assert.Equal(new DateOnly(2026, 10, 17), meter.Totals!.Day);
        Assert.Equal(0.03, meter.Totals.Today, 9);
        Assert.Equal(20.06, meter.Totals.Month, 9);
    }

    [Fact]
    public void The_first_of_the_month_starts_the_months_total_again()
    {
        var meter = new EnergyMeter(TimeZoneInfo.Utc);
        meter.Restore(new EnergyTotals(new DateOnly(2026, 10, 31), 2, 40));
        meter.Add(new DateTimeOffset(2026, 10, 31, 23, 59, 50, TimeSpan.Zero), 3600);
        meter.Add(new DateTimeOffset(2026, 11, 1, 0, 0, 10, TimeSpan.Zero), 3600);

        // Ten seconds of the new month: 0.01 kWh.
        Assert.Equal(new DateOnly(2026, 11, 1), meter.Totals!.Day);
        Assert.Equal(0.01, meter.Totals.Today, 9);
        Assert.Equal(0.01, meter.Totals.Month, 9);
    }

    [Fact]
    public void Totals_from_an_earlier_day_start_again_at_the_next_reading_even_without_power()
    {
        var meter = new EnergyMeter(TimeZoneInfo.Utc);
        meter.Restore(new EnergyTotals(new DateOnly(2026, 10, 14), 3, 25));
        meter.Add(Morning, null);
        Assert.Equal(new EnergyTotals(new DateOnly(2026, 10, 16), 0, 25), meter.Totals);

        meter.Restore(new EnergyTotals(new DateOnly(2026, 9, 30), 3, 80));
        meter.Add(Morning.AddSeconds(1), null);
        Assert.Equal(new EnergyTotals(new DateOnly(2026, 10, 16), 0, 0), meter.Totals);
    }

    [Theory]
    [InlineData(96.0, 287.0, 50.0, 433.0)]
    [InlineData(null, 287.0, 50.0, 337.0)]
    [InlineData(96.0, null, 50.0, 146.0)]
    [InlineData(96.0, 287.0, -20.0, 383.0)]
    [InlineData(null, null, 50.0, null)]
    public void The_whole_pc_is_the_cpu_and_graphics_card_and_everything_else(double? cpu, double? gpu, double other, double? total) =>
        Assert.Equal(total, EnergyProvider.TotalPower(cpu, gpu, other));

    [Fact]
    public async Task Without_the_sensor_service_there_is_no_power_and_nothing_is_counted()
    {
        var time = new FakeTime(Morning);
        var store = SensorStore();
        var provider = new EnergyProvider(() => new EnergySettings(50, 0.25, "£"), time: time);
        provider.Initialize(store);

        for (var i = 0; i < 60; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        foreach (var id in new[] { EnergyProvider.Power, EnergyProvider.Today, EnergyProvider.Month, EnergyProvider.CostToday, EnergyProvider.CostMonth })
            Assert.Equal(MetricFormatter.Missing, Format(store, id));
    }

    [Fact]
    public async Task The_energy_and_what_it_costs_are_published_in_the_currency_chosen()
    {
        var time = new FakeTime(Morning);
        var store = SensorStore();
        store.Set(SensorAliases.CpuPower, 100);
        store.Set(SensorAliases.GpuPower, 250);
        var settings = new EnergySettings(50, 0.25, "£");
        var provider = new EnergyProvider(() => settings, time: time);
        provider.Initialize(store);

        // An hour at 400 W.
        for (var second = 0; second <= 3600; second++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal("400 W", Format(store, EnergyProvider.Power));
        Assert.Equal("0.40 kWh", Format(store, EnergyProvider.Today));
        Assert.Equal("0.4 kWh", Format(store, EnergyProvider.Month));
        Assert.Equal("£0.10", Format(store, EnergyProvider.CostToday));
        Assert.Equal("£0.10", Format(store, EnergyProvider.CostMonth));

        // Another currency or price applies at the next sample, to everything so far.
        settings = new EnergySettings(50, 0.3, "€");
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("€0.12", Format(store, EnergyProvider.CostToday));

        // No price, no costs; the energy is still there.
        settings = settings with { Price = null };
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(MetricFormatter.Missing, Format(store, EnergyProvider.CostToday));
        Assert.Equal("0.40 kWh", Format(store, EnergyProvider.Today));
    }

    [Fact]
    public async Task A_restart_carries_on_from_todays_total_without_counting_the_time_between()
    {
        var path = Path.Combine(_folder, EnergyTotals.FileName);
        var time = new FakeTime(Morning);
        var store = SensorStore();
        store.Set(SensorAliases.CpuPower, 1000);
        var morning = new EnergyProvider(() => new EnergySettings(0, null, "£"), path, time: time);
        morning.Initialize(store);
        for (var second = 0; second <= 1800; second++)
        {
            await morning.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        // Closing keeps the last of it.
        morning.Dispose();
        Assert.Equal(0.5, EnergyTotals.Read(path)!.Today, 6);

        // Two hours later, the same day: half an hour at 1 kW so far, and nothing for the two hours it wasn't running.
        time.Advance(TimeSpan.FromHours(2));
        var midday = new EnergyProvider(() => new EnergySettings(0, null, "£"), path, time: time);
        midday.Initialize(store);
        await midday.SampleAsync(store, CancellationToken.None);
        Assert.Equal("0.50 kWh", Format(store, EnergyProvider.Today));
        for (var second = 0; second < 1800; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await midday.SampleAsync(store, CancellationToken.None);
        }
        Assert.Equal("1.00 kWh", Format(store, EnergyProvider.Today));
        midday.Dispose();

        // The next morning, today starts again; the month carries on.
        time.Advance(TimeSpan.FromDays(1));
        var tomorrow = new EnergyProvider(() => new EnergySettings(0, null, "£"), path, time: time);
        tomorrow.Initialize(store);
        await tomorrow.SampleAsync(store, CancellationToken.None);
        Assert.Equal("0.00 kWh", Format(store, EnergyProvider.Today));
        Assert.Equal("1.0 kWh", Format(store, EnergyProvider.Month));
    }

    [Fact]
    public async Task The_totals_are_saved_once_a_minute_while_they_change()
    {
        var path = Path.Combine(_folder, EnergyTotals.FileName);
        var time = new FakeTime(Morning);
        var store = SensorStore();
        store.Set(SensorAliases.GpuPower, 3600);
        var provider = new EnergyProvider(() => new EnergySettings(0, null, "£"), path, time: time);
        provider.Initialize(store);

        // Saved as soon as there's something, then not again for a minute.
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.False(File.Exists(path));
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0.001, EnergyTotals.Read(path)!.Today, 9);
        for (var i = 0; i < 59; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await provider.SampleAsync(store, CancellationToken.None);
        }
        Assert.Equal(0.001, EnergyTotals.Read(path)!.Today, 9);
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0.061, EnergyTotals.Read(path)!.Today, 9);
    }

    [Fact]
    public void A_totals_file_that_cant_be_used_counts_as_none()
    {
        var path = Path.Combine(_folder, EnergyTotals.FileName);
        Assert.Null(EnergyTotals.Read(path));
        Directory.CreateDirectory(_folder);

        File.WriteAllText(path, "not json");
        Assert.Null(EnergyTotals.Read(path));
        File.WriteAllText(path, """{ "day": "2026-10-16", "today": -1, "month": 4 }""");
        Assert.Null(EnergyTotals.Read(path));

        EnergyTotals.Write(path, new EnergyTotals(new DateOnly(2026, 10, 16), 1.92, 38.6));
        Assert.Equal(new EnergyTotals(new DateOnly(2026, 10, 16), 1.92, 38.6), EnergyTotals.Read(path));
        Assert.Contains("\"day\":\"2026-10-16\"", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("0.25", 0.25)]
    [InlineData(" 0,25 ", 0.25)]
    [InlineData("24.5p", 0.245)]
    [InlineData("24.5 p", 0.245)]
    [InlineData("17c", 0.17)]
    [InlineData("£0.30", 0.3)]
    [InlineData("", null)]
    [InlineData("cheap", null)]
    [InlineData("0", null)]
    [InlineData("-0.25", null)]
    [InlineData("2500", null)]
    public void Prices_can_be_typed_as_money_or_in_pence(string text, double? price)
    {
        var parsed = EnergyCost.ParsePrice(text, "£");
        Assert.Equal(price is null, parsed is null);
        if (price is { } expected)
            Assert.Equal(expected, parsed!.Value, 9);
    }

    [Theory]
    [InlineData("en-GB", "£", 0.48, "£0.48")]
    [InlineData("de-DE", "€", 0.48, "0,48 €")]
    [InlineData("ja-JP", "¥", 59.3, "¥59")]
    [InlineData("en-GB", "kr.", 0.48, "kr.0.48")]
    [InlineData("en-GB", "#0%", 0.48, "#0%0.48")]
    public void Costs_are_written_as_the_region_writes_money(string region, string symbol, double cost, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(region);
        Assert.Equal(expected, cost.ToString(EnergyCost.Format(symbol, culture.NumberFormat), culture));
    }

    private static MetricStore SensorStore()
    {
        var store = new MetricStore();
        foreach (var alias in SensorAliases.All)
            store.Register(alias.ToDefinition());
        return store;
    }

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

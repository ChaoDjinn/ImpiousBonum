using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class MoonProviderTests : IDisposable
{
    private static readonly WeatherRequest London = new("London", 51.51, -0.13, WeatherUnits.Metric);

    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public MoonProviderTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    [Fact]
    public async Task Without_a_location_the_moon_has_its_phase_but_no_moonrise_or_sun()
    {
        var store = new MetricStore();
        var provider = new MoonProvider(() => null, new FakeTime(Time("2026-10-16T18:00Z"), TimeZoneInfo.Utc));
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("Waxing crescent", Format(store, MoonProvider.Phase));
        Assert.Equal("32 %", Format(store, MoonProvider.Illumination));
        Assert.Equal("6.1", Format(store, MoonProvider.Age));
        Assert.Equal("Mon 26 Oct", Format(store, MoonProvider.NextFull));
        Assert.Equal("Mon 9 Nov", Format(store, MoonProvider.NextNew));
        Assert.Equal(MetricFormatter.Missing, Format(store, MoonProvider.Rise));
        Assert.Equal(MetricFormatter.Missing, Format(store, MoonProvider.Set));

        var sky = (SkyReport)store.GetAttachment(MoonProvider.Sky)!;
        Assert.Null(sky.Sun);
        Assert.False(sky.Southern);
        Assert.True(sky.Moon.Waxing);
    }

    [Fact]
    public async Task With_a_location_it_has_the_days_sun_and_when_the_moon_rose_and_sets()
    {
        // The Naval Observatory has sunrise at 06:26 and sunset at 17:05 in London on 16 October 2026, and the moon
        // rising at 13:07 and setting at 19:49, all in UTC.
        var store = new MetricStore();
        var provider = new MoonProvider(() => London, new FakeTime(Time("2026-10-16T18:00Z"), TimeZoneInfo.Utc));
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        var sky = (SkyReport)store.GetAttachment(MoonProvider.Sky)!;
        Assert.Equal(51.51, sky.Latitude);
        var sun = sky.Sun!;
        Assert.Equal(Time("2026-10-16T00:00Z"), sun.Start);
        Assert.Equal(Time("2026-10-17T00:00Z"), sun.End);
        Assert.Equal(Time("2026-10-16T06:26Z"), sun.Rise!.Value, TimeSpan.FromMinutes(2));
        Assert.Equal(Time("2026-10-16T17:05Z"), sun.Set!.Value, TimeSpan.FromMinutes(2));
        Assert.Equal(sun.Set.Value - sun.Rise.Value, sun.Daylight);
        Assert.Equal(Time("2026-10-16T13:07Z"), sky.Moon.Rise!.Value, TimeSpan.FromMinutes(3));
        Assert.Equal(Time("2026-10-16T19:49Z"), sky.Moon.Set!.Value, TimeSpan.FromMinutes(3));
        Assert.InRange(Format(store, MoonProvider.Rise), "13:05", "13:10");
        Assert.InRange(Format(store, MoonProvider.Set), "19:46", "19:52");
    }

    [Fact]
    public void Moonrise_and_moonset_move_on_once_it_sets()
    {
        var time = new FakeTime(Time("2026-10-16T19:00Z"), TimeZoneInfo.Utc);
        var provider = new MoonProvider(() => London, time);
        var up = provider.Work(London, time.GetUtcNow()).Moon;
        Assert.Equal(Time("2026-10-16T13:07Z"), up.Rise!.Value, TimeSpan.FromMinutes(3));

        // After it sets at 19:49, it next rises at 13:47 on the 17th and sets at 20:54.
        var down = provider.Work(London, Time("2026-10-16T20:00Z")).Moon;
        Assert.Equal(Time("2026-10-17T13:47Z"), down.Rise!.Value, TimeSpan.FromMinutes(3));
        Assert.Equal(Time("2026-10-17T20:54Z"), down.Set!.Value, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void A_place_in_the_south_sees_it_the_other_way_up()
    {
        var sydney = new WeatherRequest("Sydney", -33.87, 151.21, WeatherUnits.Metric);
        var provider = new MoonProvider(() => sydney, new FakeTime(Time("2026-10-16T17:00Z"), TimeZoneInfo.Utc));
        Assert.True(provider.Work(sydney, Time("2026-10-16T17:00Z")).Southern);
    }

    [Fact]
    public void A_day_is_midnight_to_midnight_in_the_PCs_time_even_when_the_clocks_go_back()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        // British Summer Time ended at 01:00 UTC on 25 October 2026, so that day was 25 hours long.
        var (start, end) = MoonProvider.Today(Time("2026-10-25T12:00Z"), london);
        Assert.Equal(Time("2026-10-24T23:00Z"), start);
        Assert.Equal(Time("2026-10-26T00:00Z"), end);
        // Late in the evening in summer time it's still the same day there, though it's the next in UTC.
        Assert.Equal(Time("2026-07-01T23:00Z"), MoonProvider.Today(Time("2026-07-02T22:30Z"), london).Start);
    }

    [Fact]
    public void A_day_without_a_sunrise_is_all_daylight_or_none()
    {
        Assert.Equal(TimeSpan.FromDays(1), SunDay.At(69.65, 18.96, Time("2026-06-21T00:00Z"), Time("2026-06-22T00:00Z")).Daylight);
        Assert.Equal(TimeSpan.Zero, SunDay.At(69.65, 18.96, Time("2026-12-21T00:00Z"), Time("2026-12-22T00:00Z")).Daylight);
    }

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);

    private sealed class FakeTime(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}

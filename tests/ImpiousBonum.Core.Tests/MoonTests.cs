using System.Globalization;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class MoonTests
{
    /// <summary>The US Naval Observatory's times for 2026's full and new moons (aa.usno.navy.mil), in UTC.</summary>
    public static TheoryData<string, MoonPhase> Phases2026 => new()
    {
        { "2026-01-03T10:03Z", MoonPhase.Full }, { "2026-01-18T19:52Z", MoonPhase.New },
        { "2026-02-01T22:09Z", MoonPhase.Full }, { "2026-02-17T12:01Z", MoonPhase.New },
        { "2026-03-03T11:38Z", MoonPhase.Full }, { "2026-03-19T01:23Z", MoonPhase.New },
        { "2026-04-02T02:12Z", MoonPhase.Full }, { "2026-04-17T11:52Z", MoonPhase.New },
        { "2026-05-01T17:23Z", MoonPhase.Full }, { "2026-05-16T20:01Z", MoonPhase.New },
        { "2026-05-31T08:45Z", MoonPhase.Full }, { "2026-06-15T02:54Z", MoonPhase.New },
        { "2026-06-29T23:56Z", MoonPhase.Full }, { "2026-07-14T09:43Z", MoonPhase.New },
        { "2026-07-29T14:36Z", MoonPhase.Full }, { "2026-08-12T17:37Z", MoonPhase.New },
        { "2026-08-28T04:18Z", MoonPhase.Full }, { "2026-09-11T03:27Z", MoonPhase.New },
        { "2026-09-26T16:49Z", MoonPhase.Full }, { "2026-10-10T15:50Z", MoonPhase.New },
        { "2026-10-26T04:12Z", MoonPhase.Full }, { "2026-11-09T07:02Z", MoonPhase.New },
        { "2026-11-24T14:53Z", MoonPhase.Full }, { "2026-12-09T00:52Z", MoonPhase.New },
        { "2026-12-24T01:28Z", MoonPhase.Full },
        // And some quarters.
        { "2026-01-26T04:47Z", MoonPhase.FirstQuarter }, { "2026-10-03T13:25Z", MoonPhase.LastQuarter },
        { "2026-10-18T16:12Z", MoonPhase.FirstQuarter }, { "2026-12-30T18:59Z", MoonPhase.LastQuarter },
    };

    [Theory]
    [MemberData(nameof(Phases2026))]
    public void Full_and_new_moons_come_when_the_almanac_says(string published, MoonPhase phase)
    {
        var when = Time(published);
        // From a few days either side, it finds the same moment, within a few minutes of the published one.
        Assert.Equal(when, Moon.Next(phase, when.AddDays(-5)), TimeSpan.FromMinutes(3));
        Assert.Equal(when, Moon.Previous(phase, when.AddDays(5)), TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void The_next_one_is_after_and_the_last_one_before()
    {
        var full = Moon.Next(MoonPhase.Full, Time("2026-10-16T12:00Z"));
        Assert.Equal(Time("2026-10-26T04:12Z"), full, TimeSpan.FromMinutes(3));
        // Just after it, the next is a month on; just before it, it's still this one.
        Assert.Equal(Time("2026-11-24T14:53Z"), Moon.Next(MoonPhase.Full, full.AddSeconds(1)), TimeSpan.FromMinutes(3));
        Assert.Equal(full, Moon.Next(MoonPhase.Full, full.AddSeconds(-1)), TimeSpan.FromSeconds(1));
        Assert.Equal(full, Moon.Previous(MoonPhase.Full, full.AddSeconds(1)), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Its_age_is_the_days_since_the_new_moon()
    {
        // The new moon was at 15:50 on 10 October 2026.
        Assert.Equal(5.84, Moon.Age(Time("2026-10-16T12:00Z")), 0.01);
        Assert.InRange(Moon.Age(Time("2026-10-10T15:40Z")), 29.4, 29.6);
        Assert.InRange(Moon.Age(Time("2026-10-10T16:00Z")), 0, 0.02);
    }

    [Fact]
    public void How_much_is_lit_goes_from_none_at_new_moon_through_half_at_the_quarters_to_all_at_full()
    {
        Assert.InRange(Moon.Illumination(Time("2026-10-10T15:50Z")), 0, 0.005);
        Assert.Equal(0.5, Moon.Illumination(Time("2026-10-18T16:12Z")), 0.01);
        Assert.InRange(Moon.Illumination(Time("2026-10-26T04:12Z")), 0.995, 1);
        Assert.Equal(0.5, Moon.Illumination(Time("2026-11-01T20:28Z")), 0.01);
        // The Naval Observatory has 30% at noon UTC on 16 October 2026, 62% on 2 October, 51% on 3 October and 29% on 5 October.
        Assert.Equal(0.30, Moon.Illumination(Time("2026-10-16T12:00Z")), 0.006);
        Assert.Equal(0.62, Moon.Illumination(Time("2026-10-02T12:00Z")), 0.006);
        Assert.Equal(0.51, Moon.Illumination(Time("2026-10-03T12:00Z")), 0.006);
        Assert.Equal(0.29, Moon.Illumination(Time("2026-10-05T12:00Z")), 0.006);
    }

    [Fact]
    public void It_waxes_from_new_to_full_and_wanes_after()
    {
        Assert.InRange(Moon.Elongation(Time("2026-10-16T12:00Z")), 0, 90);
        Assert.InRange(Moon.Elongation(Time("2026-10-22T12:00Z")), 90, 180);
        Assert.InRange(Moon.Elongation(Time("2026-10-29T12:00Z")), 180, 270);
        Assert.InRange(Moon.Elongation(Time("2026-11-05T12:00Z")), 270, 360);
    }

    [Theory]
    [InlineData("2026-10-16", "Waxing crescent")]
    [InlineData("2026-10-18", "First quarter")]
    [InlineData("2026-10-22", "Waxing gibbous")]
    [InlineData("2026-10-25", "Waxing gibbous")]
    [InlineData("2026-10-26", "Full moon")]
    [InlineData("2026-10-27", "Waning gibbous")]
    [InlineData("2026-11-01", "Last quarter")]
    [InlineData("2026-11-05", "Waning crescent")]
    [InlineData("2026-11-09", "New moon")]
    public void A_main_phase_has_its_name_all_through_its_day(string day, string name)
    {
        var start = Time(day + "T00:00Z");
        foreach (var hour in new[] { 0, 1, 12, 23 })
            Assert.Equal(name, Moon.PhaseName(start.AddHours(hour), start, start.AddDays(1)));
    }

    [Fact]
    public void Where_it_is_matches_Meeus_worked_example()
    {
        // Astronomical Algorithms, example 47.a: 12 April 1992 at 0h.
        var (longitude, latitude, distance) = Moon.Position(SunPosition.Centuries(Time("1992-04-12T00:00Z")));
        Assert.Equal(133.162655, longitude, 0.0001);
        Assert.Equal(-3.229126, latitude, 0.0001);
        Assert.Equal(368409.7, distance, 0.5);
    }

    [Theory]
    // The Naval Observatory's times, in UTC: London on 16 and 26 October 2026, and Sydney on 10 March 2026.
    [InlineData(51.51, -0.13, "2026-10-16T00:00Z", "2026-10-16T13:07Z", "2026-10-16T19:49Z")]
    [InlineData(51.51, -0.13, "2026-10-26T00:00Z", "2026-10-26T16:21Z", "2026-10-26T07:16Z")]
    [InlineData(51.51, -0.13, "2026-10-02T00:00Z", "2026-10-02T20:31Z", "2026-10-02T13:45Z")]
    [InlineData(-33.87, 151.21, "2026-03-09T14:00Z", "2026-03-10T11:53Z", "2026-03-10T02:10Z")]
    public void It_rises_and_sets_when_the_almanac_says(double latitude, double longitude, string day, string rise, string set)
    {
        var start = Time(day);
        var (rose, went) = Moon.RiseAndSet(latitude, longitude, start, start.AddDays(1));
        Assert.Equal(Time(rise), rose!.Value, TimeSpan.FromMinutes(3));
        Assert.Equal(Time(set), went!.Value, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Some_days_it_doesnt_rise()
    {
        // In London on 5 October 2026 it set at 15:38 and didn't rise until 00:40 on the 6th.
        var (rise, set) = Moon.RiseAndSet(51.51, -0.13, Time("2026-10-05T00:00Z"), Time("2026-10-06T00:00Z"));
        Assert.Null(rise);
        Assert.Equal(Time("2026-10-05T15:38Z"), set!.Value, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void While_its_up_it_has_when_it_rose_and_when_it_sets_and_while_its_down_when_it_next_does()
    {
        // In London it rose at 13:07 on 16 October 2026 and set at 19:49, then rose at 13:47 on the 17th and set at 20:54.
        var (rose, sets) = Moon.RiseAndSetAround(51.51, -0.13, Time("2026-10-16T17:00Z"));
        Assert.Equal(Time("2026-10-16T13:07Z"), rose!.Value, TimeSpan.FromMinutes(3));
        Assert.Equal(Time("2026-10-16T19:49Z"), sets!.Value, TimeSpan.FromMinutes(3));

        var (rises, then) = Moon.RiseAndSetAround(51.51, -0.13, Time("2026-10-16T21:00Z"));
        Assert.Equal(Time("2026-10-17T13:47Z"), rises!.Value, TimeSpan.FromMinutes(3));
        Assert.Equal(Time("2026-10-17T20:54Z"), then!.Value, TimeSpan.FromMinutes(3));
    }

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}

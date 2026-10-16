using System.Globalization;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class SunPositionTests
{
    [Theory]
    // Solar noon on the equator at the March equinox: overhead.
    [InlineData(0, 0, "2026-03-20T12:07:00Z", 89.88)]
    // London at midsummer, at solar noon there: 90 - 51.5 + 23.4.
    [InlineData(51.5074, -0.1278, "2026-06-21T12:02:00Z", 61.93)]
    // London at midnight on the shortest day, as far below the horizon as it goes.
    [InlineData(51.5074, -0.1278, "2026-12-21T00:00:00Z", -61.93)]
    // A mid-October morning in London, and a January afternoon in Sydney.
    [InlineData(51.5074, -0.1278, "2026-10-12T09:00:00Z", 21.02)]
    [InlineData(-33.87, 151.21, "2026-01-15T03:00:00Z", 72.32)]
    public void The_sun_is_where_it_should_be(double latitude, double longitude, string time, double expected) =>
        Assert.Equal(expected, SunPosition.Elevation(latitude, longitude, DateTimeOffset.Parse(time, CultureInfo.InvariantCulture)), 0.05);

    [Fact]
    public void At_sunrise_the_sun_is_just_below_the_horizon()
    {
        // Open-Meteo has sunrise in London at 07:17 BST on 11 October 2026. The sun's top edge shows then, lifted by
        // the air, while its centre is really about 0.83° below the horizon.
        Assert.InRange(SunPosition.Elevation(51.51, -0.13, new DateTimeOffset(2026, 10, 11, 6, 17, 0, TimeSpan.Zero)), -1.1, -0.6);
    }

    [Theory]
    // Open-Meteo has sunrise in London at 07:17 BST and sunset at 18:13 BST on 11 October 2026.
    [InlineData(51.51, -0.13, "2026-10-11T06:10:00Z", true)]
    [InlineData(51.51, -0.13, "2026-10-11T06:25:00Z", false)]
    [InlineData(51.51, -0.13, "2026-10-11T17:05:00Z", false)]
    [InlineData(51.51, -0.13, "2026-10-11T17:20:00Z", true)]
    // Midnight at midsummer in Tromsø, where the sun doesn't set, and noon at midwinter, when it doesn't rise.
    [InlineData(69.65, 18.96, "2026-06-21T23:00:00Z", false)]
    [InlineData(69.65, 18.96, "2026-12-21T11:00:00Z", true)]
    public void The_sun_is_down_between_sunset_and_sunrise(double latitude, double longitude, string time, bool down) =>
        Assert.Equal(down, SunPosition.IsDown(latitude, longitude, DateTimeOffset.Parse(time, CultureInfo.InvariantCulture)));

    [Theory]
    // The US Naval Observatory's times, in UTC: London on 16 and 26 October 2026, and Sydney on 10 March 2026.
    [InlineData(51.51, -0.13, "2026-10-16T00:00Z", "2026-10-16T06:26Z", "2026-10-16T17:05Z")]
    [InlineData(51.51, -0.13, "2026-10-26T00:00Z", "2026-10-26T06:43Z", "2026-10-26T16:45Z")]
    [InlineData(-33.87, 151.21, "2026-03-09T14:00Z", "2026-03-09T19:50Z", "2026-03-10T08:20Z")]
    public void It_rises_and_sets_when_the_almanac_says(double latitude, double longitude, string day, string rise, string set)
    {
        var start = Time(day);
        var (rose, went) = SunPosition.RiseAndSet(latitude, longitude, start, start.AddDays(1));
        Assert.Equal(Time(rise), rose!.Value, TimeSpan.FromMinutes(2));
        Assert.Equal(Time(set), went!.Value, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void In_the_far_north_it_can_stay_up_or_down_all_day()
    {
        Assert.Equal((null, null), SunPosition.RiseAndSet(69.65, 18.96, Time("2026-06-21T00:00Z"), Time("2026-06-22T00:00Z")));
        Assert.Equal((null, null), SunPosition.RiseAndSet(69.65, 18.96, Time("2026-12-21T00:00Z"), Time("2026-12-22T00:00Z")));
    }

    [Fact]
    public void Only_the_moment_matters_not_the_clock_it_was_read_from()
    {
        var utc = SunPosition.Elevation(51.51, -0.13, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var summerTime = SunPosition.Elevation(51.51, -0.13, new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(1)));
        Assert.Equal(utc, summerTime, 1e-9);
    }

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}

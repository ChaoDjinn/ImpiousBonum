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

    [Fact]
    public void Only_the_moment_matters_not_the_clock_it_was_read_from()
    {
        var utc = SunPosition.Elevation(51.51, -0.13, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var summerTime = SunPosition.Elevation(51.51, -0.13, new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(1)));
        Assert.Equal(utc, summerTime, 1e-9);
    }
}

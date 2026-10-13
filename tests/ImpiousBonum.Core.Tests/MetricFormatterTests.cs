using System.Globalization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class MetricFormatterTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public MetricFormatterTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    private static MetricSample Sample(MetricUnit unit, double? value, string? text = null) =>
        new(new MetricDefinition("x", "x", "x", unit), value, text);

    [Theory]
    [InlineData(MetricUnit.Percent, 11.84, "11.8 %")]
    [InlineData(MetricUnit.Celsius, 60.2, "60.2 °C")]
    [InlineData(MetricUnit.Milliseconds, 9.4, "9 ms")]
    [InlineData(MetricUnit.FramesPerSecond, 58.8, "58.8 FPS")]
    [InlineData(MetricUnit.None, 16, "16")]
    [InlineData(MetricUnit.Hectopascals, 1013.6, "1014 hPa")]
    [InlineData(MetricUnit.InchesOfMercury, 29.921, "29.92 inHg")]
    [InlineData(MetricUnit.Millimetres, 0.42, "0.4 mm")]
    [InlineData(MetricUnit.Inches, 0.017, "0.02 in")]
    [InlineData(MetricUnit.Kilometres, 20.5, "20.5 km")]
    [InlineData(MetricUnit.Miles, 12.74, "12.7 mi")]
    [InlineData(MetricUnit.Degrees, 225, "225°")]
    [InlineData(MetricUnit.Degrees, -31.9, "-32°")]
    [InlineData(MetricUnit.MicrogramsPerCubicMetre, 4.8, "5 µg/m³")]
    [InlineData(MetricUnit.GrainsPerCubicMetre, 61.2, "61 grains/m³")]
    public void Formats_units_with_default_precision(MetricUnit unit, double value, string expected) =>
        Assert.Equal(expected, MetricFormatter.Format(Sample(unit, value)));

    [Theory]
    [InlineData(512d, "512.0 B")]
    [InlineData(81_613d, "79.7 KB")]
    [InlineData(20_079_470_182d, "18.7 GB")]
    [InlineData(1_024_000_000_000d, "953.7 GB")]
    [InlineData(2_000_000_000_000d, "1.8 TB")]
    public void Scales_bytes_to_a_readable_unit(double bytes, string expected) =>
        Assert.Equal(expected, MetricFormatter.Format(Sample(MetricUnit.Bytes, bytes)));

    [Fact]
    public void Rates_get_a_per_second_suffix() =>
        Assert.Equal("79.7 KB/s", MetricFormatter.Format(Sample(MetricUnit.BytesPerSecond, 81_613)));

    [Fact]
    public void Spec_can_force_a_unit_and_number_format() =>
        Assert.Equal("3,551 MB", MetricFormatter.Format(Sample(MetricUnit.Bytes, 3551d * 1024 * 1024), FormatSpec.Parse("N0 MB")));

    [Fact]
    public void Spec_can_drop_the_unit() =>
        Assert.Equal("60.2", MetricFormatter.Format(Sample(MetricUnit.Celsius, 60.2), FormatSpec.Parse("nounit")));

    [Fact]
    public void Missing_values_show_a_dash()
    {
        Assert.Equal(MetricFormatter.Missing, MetricFormatter.Format(Sample(MetricUnit.Celsius, null)));
        Assert.Equal(MetricFormatter.Missing, MetricFormatter.Format(Sample(MetricUnit.Celsius, double.NaN)));
    }

    [Fact]
    public void Readings_that_are_not_metrics_show_as_a_metric_would()
    {
        Assert.Equal("12 km/h", MetricFormatter.Format(11.6, MetricUnit.KilometresPerHour));
        Assert.Equal("29.90 inHg", MetricFormatter.Format(29.896, MetricUnit.InchesOfMercury));
        Assert.Equal("12", MetricFormatter.Format(11.6, MetricUnit.MilesPerHour, FormatSpec.Parse("nounit")));
        Assert.Equal(MetricFormatter.Missing, MetricFormatter.Format(null, MetricUnit.Millimetres));
    }

    [Fact]
    public void Text_metrics_render_their_text() =>
        Assert.Equal("Radeon", MetricFormatter.Format(Sample(MetricUnit.Text, null, "Radeon")));
}

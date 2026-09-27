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
    public void Text_metrics_render_their_text() =>
        Assert.Equal("Radeon", MetricFormatter.Format(Sample(MetricUnit.Text, null, "Radeon")));
}

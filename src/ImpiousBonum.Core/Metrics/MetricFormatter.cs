using System.Globalization;

namespace ImpiousBonum.Core.Metrics;

public static class MetricFormatter
{
    /// <summary>Shown when a metric has no reading (not sampled yet, or the sensor is unavailable).</summary>
    public const string Missing = "—";

    private static readonly string[] ByteSuffixes = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(MetricSample sample, FormatSpec? spec = null)
    {
        spec ??= FormatSpec.Default;

        if (sample.Text is not null)
            return sample.Text;
        if (sample.Value is not double value || double.IsNaN(value))
            return Missing;

        var culture = CultureInfo.CurrentCulture;

        switch (sample.Definition.Unit)
        {
            case MetricUnit.Bytes:
            case MetricUnit.BytesPerSecond:
            {
                var exponent = spec.ByteUnit is { } forced ? (int)forced : AutoByteExponent(value);
                var scaled = value / Math.Pow(1024, exponent);
                var number = scaled.ToString(spec.NumberFormat ?? sample.Definition.Format ?? "0.0", culture);
                if (spec.NoUnit)
                    return number;
                var suffix = ByteSuffixes[exponent];
                return sample.Definition.Unit == MetricUnit.BytesPerSecond ? $"{number} {suffix}/s" : $"{number} {suffix}";
            }

            default:
            {
                var (defaultFormat, suffix) = sample.Definition.Unit switch
                {
                    MetricUnit.Percent => ("0.0", "%"),
                    MetricUnit.Celsius => ("0.0", "°C"),
                    MetricUnit.Milliseconds => ("0", "ms"),
                    MetricUnit.FramesPerSecond => ("0.0", "FPS"),
                    MetricUnit.Megahertz => ("0", "MHz"),
                    MetricUnit.Rpm => ("0", "RPM"),
                    MetricUnit.Volts => ("0.000", "V"),
                    MetricUnit.Watts => ("0.0", "W"),
                    MetricUnit.Fahrenheit => ("0.0", "°F"),
                    MetricUnit.KilometresPerHour => ("0", "km/h"),
                    MetricUnit.MilesPerHour => ("0", "mph"),
                    MetricUnit.Hectopascals => ("0", "hPa"),
                    MetricUnit.InchesOfMercury => ("0.00", "inHg"),
                    MetricUnit.Millimetres => ("0.0", "mm"),
                    MetricUnit.Inches => ("0.00", "in"),
                    MetricUnit.Kilometres => ("0.0", "km"),
                    MetricUnit.Miles => ("0.0", "mi"),
                    MetricUnit.Degrees => ("0", "°"),
                    MetricUnit.MicrogramsPerCubicMetre => ("0", "µg/m³"),
                    MetricUnit.GrainsPerCubicMetre => ("0", "grains/m³"),
                    MetricUnit.KilowattHours => ("0.00", "kWh"),
                    MetricUnit.GramsOfCo2PerKilowattHour => ("0", "gCO₂/kWh"),
                    _ => ("0.##", ""),
                };
                var number = value.ToString(spec.NumberFormat ?? sample.Definition.Format ?? defaultFormat, culture);
                if (spec.NoUnit || suffix.Length == 0)
                    return number;
                // An angle's degree sign goes straight after the number ("225°"); a temperature's unit is spaced ("11 °C").
                return sample.Definition.Unit == MetricUnit.Degrees ? number + suffix : $"{number} {suffix}";
            }
        }
    }

    /// <summary>
    /// A reading that isn't a metric's as a metric in <paramref name="unit"/> would show it, e.g. "12 km/h", so a widget
    /// showing readings of its own (another place's weather, say) shows them just as the metrics do.
    /// </summary>
    public static string Format(double? value, MetricUnit unit, FormatSpec? spec = null) =>
        Format(new MetricSample(new MetricDefinition(string.Empty, string.Empty, string.Empty, unit), value, null), spec);

    private static int AutoByteExponent(double value)
    {
        var exponent = 0;
        value = Math.Abs(value);
        // Switch unit just before 1000 so we show "0.98 TB" rather than "1,003.5 GB".
        while (value >= 1000 && exponent < ByteSuffixes.Length - 1)
        {
            value /= 1024;
            exponent++;
        }
        return exponent;
    }
}

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
                var number = scaled.ToString(spec.NumberFormat ?? "0.0", culture);
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
                    _ => ("0.##", ""),
                };
                var number = value.ToString(spec.NumberFormat ?? defaultFormat, culture);
                return spec.NoUnit || suffix.Length == 0 ? number : $"{number} {suffix}";
            }
        }
    }

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

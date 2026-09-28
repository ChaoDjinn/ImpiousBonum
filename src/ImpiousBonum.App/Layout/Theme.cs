using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Layout;

/// <summary>Resolved, frozen WPF resources for a <see cref="ThemeSettings"/>.</summary>
public sealed class Theme
{
    private Theme(ThemeSettings settings)
    {
        FontFamily = ResolveFontFamily(settings);
        FontWeight = ParseWeight(settings.FontWeight);
        Foreground = ParseBrush(settings.Foreground, Brushes.White);
        Secondary = ParseBrush(settings.Secondary, Foreground);
        Accent = ParseBrush(settings.Accent, Brushes.Orange);
        Background = ParseBrush(settings.Background, Brushes.Black);
        Track = ParseBrush(settings.Track, Brushes.Transparent);
        Warning = ParseBrush(settings.Warning, Brushes.Gold);
        Critical = ParseBrush(settings.Critical, Brushes.Red);
        AccentColor = Accent is SolidColorBrush solid ? solid.Color : Colors.Orange;
    }

    public FontFamily FontFamily { get; }

    public FontWeight FontWeight { get; }

    public Brush Foreground { get; }

    public Brush Secondary { get; }

    public Brush Accent { get; }

    public Color AccentColor { get; }

    public Brush Background { get; }

    public Brush Track { get; }

    /// <summary>Warning colour rules can name, e.g. a CPU running hot.</summary>
    public Brush Warning { get; }

    public Brush Critical { get; }

    public static Theme From(ThemeSettings settings) => new(settings);

    /// <summary>
    /// Resolves a widget colour setting: <c>foreground</c>, <c>secondary</c>, <c>accent</c>, <c>warning</c>, <c>critical</c>
    /// or a <c>#RRGGBB</c>/<c>#AARRGGBB</c> value.
    /// </summary>
    public Brush Resolve(string? value, Brush fallback) => value?.ToLowerInvariant() switch
    {
        null or "" => fallback,
        "foreground" => Foreground,
        "secondary" => Secondary,
        "accent" => Accent,
        "warning" => Warning,
        "critical" => Critical,
        _ => ParseBrush(value, fallback),
    };

    private static FontFamily ResolveFontFamily(ThemeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FontFile) && File.Exists(settings.FontFile))
        {
            try
            {
                var path = Path.GetFullPath(settings.FontFile);
                var typeface = new GlyphTypeface(new Uri(path));
                var family = typeface.Win32FamilyNames.Values.FirstOrDefault() ?? typeface.FamilyNames.Values.First();
                var folder = new Uri(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar);
                return new FontFamily(folder, "./#" + family);
            }
            catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
            {
                // Fall back to the named family below.
            }
        }

        return new FontFamily(settings.FontFamily + ", Segoe UI");
    }

    private static FontWeight ParseWeight(string? weight)
    {
        try
        {
            return string.IsNullOrWhiteSpace(weight) ? FontWeights.Normal : (FontWeight)new FontWeightConverter().ConvertFromInvariantString(weight)!;
        }
        catch (FormatException)
        {
            return FontWeights.Normal;
        }
    }

    private static Brush ParseBrush(string? value, Brush fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}

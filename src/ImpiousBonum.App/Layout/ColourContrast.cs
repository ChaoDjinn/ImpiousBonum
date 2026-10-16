using System.Windows.Media;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// WCAG contrast between a colour and the background it's drawn on: 4.5:1 or more for text to read easily, 3:1 for
/// large text and graphics. The built-in themes keep to these, and so do themes made from a picture.
/// </summary>
public static class ColourContrast
{
    /// <summary>What text needs.</summary>
    public const double Text = 4.5;

    /// <summary>What large text, bars and graphs need.</summary>
    public const double Graphics = 3;

    /// <summary>The contrast ratio, from 1 to 21, of a colour (blended over the background if translucent) against the background.</summary>
    public static double Ratio(Color colour, Color background)
    {
        var alpha = colour.A / 255.0;
        double Blend(byte f, byte b) => f * alpha + b * (1 - alpha);
        var lighter = Luminance(Blend(colour.R, background.R), Blend(colour.G, background.G), Blend(colour.B, background.B));
        var darker = Luminance(background.R, background.G, background.B);
        if (lighter < darker)
            (lighter, darker) = (darker, lighter);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>As above, for colours written as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static double Ratio(string colour, string background) =>
        Ratio((Color)ColorConverter.ConvertFromString(colour), (Color)ColorConverter.ConvertFromString(background));

    /// <summary>How bright a colour looks, from 0 for black to 1 for white, from its 0–255 channels.</summary>
    public static double Luminance(double r, double g, double b)
    {
        static double Linear(double channel)
        {
            channel /= 255;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }
}

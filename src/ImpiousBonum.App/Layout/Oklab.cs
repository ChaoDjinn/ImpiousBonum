using System.Windows.Media;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// A colour in Oklab (Björn Ottosson, 2020): lightness from 0 to 1, and two axes of hue, green to red and blue to yellow,
/// spaced so equal distances look about equally different. Making a theme from a picture groups and adjusts colours in
/// it, where "lighter" and "more vivid" keep the hue as the eye sees it.
/// </summary>
public readonly record struct Oklab(double L, double A, double B)
{
    /// <summary>How vivid it is: 0 for a grey, about 0.3 for the most vivid colours a screen shows.</summary>
    public double Chroma => Math.Sqrt(A * A + B * B);

    /// <summary>The hue in degrees: about 30 for red, 70 for orange, 110 for yellow, 145 for green, 265 for blue.</summary>
    public double Hue => (Math.Atan2(B, A) * 180 / Math.PI + 360) % 360;

    /// <summary>A colour from its lightness, chroma and hue (in degrees).</summary>
    public static Oklab FromLch(double lightness, double chroma, double hue)
    {
        var radians = hue * Math.PI / 180;
        return new Oklab(lightness, chroma * Math.Cos(radians), chroma * Math.Sin(radians));
    }

    /// <summary>This colour with another lightness and chroma, keeping its hue.</summary>
    public Oklab With(double lightness, double chroma) => FromLch(lightness, chroma, Hue);

    public double DistanceTo(Oklab other) =>
        Math.Sqrt((L - other.L) * (L - other.L) + (A - other.A) * (A - other.A) + (B - other.B) * (B - other.B));

    public static Oklab From(Color colour)
    {
        var (r, g, b) = (Linear(colour.R), Linear(colour.G), Linear(colour.B));
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return new Oklab(
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>
    /// The nearest colour a screen can show: one too vivid for its lightness loses chroma, keeping its lightness and hue,
    /// rather than having its channels cut off, which would shift the hue.
    /// </summary>
    public Color ToColor()
    {
        var lightness = Math.Clamp(L, 0, 1);
        if (InGamut(new Oklab(lightness, A, B)))
            return ToRgb(new Oklab(lightness, A, B));
        var (low, high) = (0.0, Chroma);
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2;
            if (InGamut(FromLch(lightness, mid, Hue)))
                low = mid;
            else
                high = mid;
        }
        return ToRgb(FromLch(lightness, low, Hue));
    }

    /// <summary>As <c>#RRGGBB</c>.</summary>
    public string ToHex()
    {
        var colour = ToColor();
        return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    }

    private static bool InGamut(Oklab lab)
    {
        var (r, g, b) = ToLinear(lab);
        const double slack = 1e-4;
        return r is >= -slack and <= 1 + slack && g is >= -slack and <= 1 + slack && b is >= -slack and <= 1 + slack;
    }

    private static Color ToRgb(Oklab lab)
    {
        var (r, g, b) = ToLinear(lab);
        return Color.FromRgb(Gamma(r), Gamma(g), Gamma(b));
    }

    private static (double R, double G, double B) ToLinear(Oklab lab)
    {
        var l = Math.Pow(lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B, 3);
        var m = Math.Pow(lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B, 3);
        var s = Math.Pow(lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B, 3);
        return (
            4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Gamma(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var c = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (byte)Math.Round(c * 255);
    }
}

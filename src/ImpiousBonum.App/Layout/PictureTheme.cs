using System.Windows.Media;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// A theme's colours made from a picture's main colours (see <see cref="Palette"/>), or from one colour such as Windows'
/// accent colour: the background from a dark colour that covers much of the picture (or a light one, for a mostly light
/// picture), the accents from its most vivid colours, and text, warnings and the rest to go with them. Whatever the
/// picture, every colour keeps to the contrast the built-in themes do (see <see cref="ColourContrast"/>).
/// </summary>
public static class PictureTheme
{
    /// <summary>
    /// The theme settings it sets. The background fade and glow colour come back empty (null), since a theme's own
    /// would have been chosen for its old colours: the background is plain, and the glow (if any) is in the new accent.
    /// </summary>
    public static readonly IReadOnlyList<string> Keys =
        ["foreground", "secondary", "accent", "accent2", "background", "track", "warning", "critical", "backgroundGradient", "glowColor"];

    /// <summary>A picture whose average lightness (Oklab, 0 to 1) is at least this makes a light theme.</summary>
    public const double LightPicture = 0.7;

    // Contrast against the background: well over what's needed for text, comfortably over it for the rest.
    private const double ForegroundContrast = 7;
    private const double SecondaryContrast = ColourContrast.Text;
    private const double ColourContrastWanted = 3.5;

    // Warning is amber and critical red, as in the built-in themes, turned towards yellow, orange or pink where the
    // accent is close, so a warning never passes for an ordinary reading. The first of each is the usual one.
    private static readonly double[] WarningHues = [80, 65, 95, 105, 55];
    private static readonly double[] CriticalHues = [27, 15, 5, 355, 40];

    // How far apart in Oklab two colours have to be to tell them apart at a glance (just noticeable is about 0.02).
    private const double DistinctColours = 0.12;

    /// <summary>
    /// The colours for a picture with these main colours. <paramref name="light"/> asks for a light or dark theme;
    /// null decides from the picture.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> FromPicture(IReadOnlyList<Swatch> swatches, bool? light = null)
    {
        if (swatches.Count == 0)
            throw new ArgumentException("There are no colours to make a theme from.", nameof(swatches));
        var colours = swatches.Select(s => (Lab: Oklab.From(s.Colour), s.Share)).ToList();
        var total = colours.Sum(c => c.Share);
        var isLight = light ?? colours.Sum(c => c.Lab.L * c.Share) / total >= LightPicture;

        var background = Background(colours, isLight);
        // Text and the bar track take a touch of the background's hue, unless it's grey.
        var tinted = background.Chroma >= 0.01;
        var foreground = Readable(background.With(isLight ? 0.27 : 0.96, Math.Min(background.Chroma, isLight ? 0.04 : 0.02)), background, ForegroundContrast);
        var secondary = Readable(
            background.With(foreground.L + (background.L - foreground.L) * 0.3, tinted ? Math.Min(background.Chroma + 0.02, 0.05) : 0), background, SecondaryContrast);
        var track = background.With(background.L + (isLight ? -0.05 : 0.07), tinted ? Math.Min(background.Chroma + 0.01, 0.07) : 0);
        var (accent, accent2) = Accents(colours, background, isLight);
        var (warning, critical) = Alerts(background, isLight, accent, accent2);

        return new Dictionary<string, string?>
        {
            ["foreground"] = foreground.ToHex(),
            ["secondary"] = secondary.ToHex(),
            ["accent"] = accent.ToHex(),
            ["accent2"] = accent2.ToHex(),
            ["background"] = background.ToHex(),
            ["track"] = track.ToHex(),
            ["warning"] = warning.ToHex(),
            ["critical"] = critical.ToHex(),
            ["backgroundGradient"] = null,
            ["glowColor"] = null,
        };
    }

    /// <summary>The colours for one colour, such as Windows' accent colour, on a light or dark background.</summary>
    public static IReadOnlyDictionary<string, string?> FromColour(Color colour, bool light) => FromPicture([new Swatch(colour, 1)], light);

    /// <summary>
    /// The colour covering most of the picture, favouring dark ones (light ones for a light theme), made dark (light)
    /// and calm enough to be a background: a blue sky over dark hills gives navy rather than black.
    /// </summary>
    private static Oklab Background(List<(Oklab Lab, double Share)> colours, bool light)
    {
        // 1 for a colour that's dark already, down to a quarter for a light one, the other way round for a light theme.
        double Suits(Oklab c) => light ? Math.Clamp((c.L - 0.4) / 0.4, 0.25, 1) : Math.Clamp((0.8 - c.L) / 0.4, 0.25, 1);
        var basis = colours.MaxBy(c => c.Share * Suits(c.Lab)).Lab;
        return light
            ? basis.With(Math.Clamp(basis.L, 0.95, 0.985), Math.Min(basis.Chroma, 0.03))
            : basis.With(Math.Clamp(basis.L, 0.14, 0.24), Math.Min(basis.Chroma, 0.06));
    }

    /// <summary>
    /// The picture's most vivid colour that covers more than a speck of it, and another of a different hue for the accent
    /// to fade to; or failing that, a lighter (darker) shade of the first. A grey picture gets grey accents.
    /// </summary>
    private static (Oklab Accent, Oklab Accent2) Accents(List<(Oklab Lab, double Share)> colours, Oklab background, bool light)
    {
        // Even a pastel picture's colours count, but not the faint tints of a black and white photo.
        var vivid = colours.Where(c => c.Lab.Chroma >= 0.02).OrderByDescending(c => c.Lab.Chroma * Math.Pow(c.Share, 0.3)).Select(c => c.Lab).ToList();
        if (vivid.Count == 0)
        {
            var grey = Readable(new Oklab(light ? 0.45 : 0.82, 0, 0), background, ColourContrastWanted);
            return (grey, Readable(new Oklab(light ? 0.6 : 0.66, 0, 0), background, ColourContrastWanted));
        }

        var first = Brighter(vivid[0], light);
        var accent = Readable(first, background, ColourContrastWanted);
        var other = vivid.Skip(1).Where(c => HueGap(c.Hue, first.Hue) >= 35 && c.Chroma >= first.Chroma * 0.4).Select(c => (Oklab?)c).FirstOrDefault();
        var second = other is { } hue ? Brighter(hue, light) : accent.With(accent.L + (light ? -0.12 : 0.12), accent.Chroma * 0.85);
        return (accent, Readable(second, background, ColourContrastWanted));
    }

    /// <summary>
    /// More vivid (a pastel or a muted colour much more so, to stand out from the text), and not too dark (light) for
    /// the background.
    /// </summary>
    private static Oklab Brighter(Oklab colour, bool light) =>
        colour.With(light ? Math.Min(colour.L, 0.62) : Math.Max(colour.L, 0.62), Math.Min(Math.Max(colour.Chroma * 1.25, Math.Min(colour.Chroma * 3, 0.12)), 0.25));

    /// <summary>
    /// Amber and red, each turned away from the other and from the accents as far as it takes to look different, and no
    /// further, so they keep their usual hues where they can.
    /// </summary>
    private static (Oklab Warning, Oklab Critical) Alerts(Oklab background, bool light, Oklab accent, Oklab accent2)
    {
        // Compared as they'll be written, since a colour too vivid for the screen comes out less so.
        static Oklab Shown(Oklab colour) => Oklab.From(colour.ToColor());
        Oklab[] accents = [Shown(accent), Shown(accent2)];

        // Darkened enough for a light background, a yellower amber turns olive.
        var warnings = WarningHues.Where(h => !light || h < 100)
            .Select(hue => Shown(Readable(Oklab.FromLch(light ? 0.62 : 0.84, 0.16, hue), background, ColourContrastWanted))).ToList();
        var criticals = CriticalHues
            .Select(hue => Shown(Readable(Oklab.FromLch(light ? 0.55 : 0.66, 0.2, hue), background, ColourContrastWanted))).ToList();

        // The usual pair unless another is further from the accents, up to far enough to tell apart at a glance.
        var (warning, critical, best) = (warnings[0], criticals[0], -1.0);
        foreach (var w in warnings)
        {
            foreach (var c in criticals)
            {
                var apart = Math.Min(DistinctColours, Math.Min(w.DistanceTo(c), accents.Min(a => Math.Min(a.DistanceTo(w), a.DistanceTo(c)))));
                if (apart > best)
                    (warning, critical, best) = (w, c, apart);
            }
        }
        return (warning, critical);
    }

    /// <summary>
    /// The colour, made lighter (on a dark background) or darker (on a light one) as little as it takes to have at least
    /// <paramref name="contrast"/> against the background, keeping its hue. Measured on the colour as it will be written.
    /// </summary>
    private static Oklab Readable(Oklab colour, Oklab background, double contrast)
    {
        var back = background.ToColor();
        bool Enough(Oklab c) => ColourContrast.Ratio(c.ToColor(), back) >= contrast;
        if (Enough(colour))
            return colour;

        var lighter = background.L < 0.6;
        var (near, far) = (colour.L, lighter ? 1.0 : 0.0);
        for (var i = 0; i < 24; i++)
        {
            var mid = (near + far) / 2;
            if (Enough(colour.With(mid, colour.Chroma)))
                far = mid;
            else
                near = mid;
        }
        var readable = colour.With(far, colour.Chroma);
        // White on a dark background, or black on a light one, always has enough.
        return Enough(readable) ? readable : new Oklab(lighter ? 1 : 0, 0, 0);
    }

    /// <summary>How far apart two hues are round the colour wheel, from 0 to 180 degrees.</summary>
    private static double HueGap(double first, double second)
    {
        var gap = Math.Abs(first - second) % 360;
        return gap > 180 ? 360 - gap : gap;
    }
}

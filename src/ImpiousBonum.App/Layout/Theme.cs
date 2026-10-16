using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ImpiousBonum.App.Layout;

/// <summary>Resolved, frozen WPF resources for a <see cref="ThemeSettings"/>.</summary>
public sealed class Theme
{
    private readonly ThemeSettings _settings;

    private Theme(ThemeSettings settings)
    {
        _settings = settings;
        FontFamily = ResolveFontFamily(settings);
        FontWeight = ParseWeight(settings.FontWeight);
        Foreground = ParseBrush(settings.Foreground, Brushes.White);
        Secondary = ParseBrush(settings.Secondary, Foreground);
        Accent = Gradient(ParseBrush(settings.Accent, Brushes.Orange), settings.Accent2, vertical: false);
        Background = Gradient(ParseBrush(settings.Background, Brushes.Black), settings.BackgroundGradient, vertical: true);
        Track = ParseBrush(settings.Track, Brushes.Transparent);
        Warning = ParseBrush(settings.Warning, Brushes.Gold);
        Critical = ParseBrush(settings.Critical, Brushes.Red);
        WarningSymbols = settings.WarningSymbols;
        AccentColor = ParseBrush(settings.Accent, Brushes.Orange) is SolidColorBrush solid ? solid.Color : Colors.Orange;
        Glow = CreateGlow(settings.Glow, ParseBrush(settings.GlowColor, Brushes.Transparent) is SolidColorBrush { Color.A: > 0 } glow ? glow.Color : AccentColor);
    }

    /// <summary>The glow around every widget, shared by all of them, or null for none.</summary>
    public Effect? Glow { get; }

    public FontFamily FontFamily { get; }

    public FontWeight FontWeight { get; }

    public Brush Foreground { get; }

    public Brush Secondary { get; }

    public Brush Accent { get; }

    /// <summary>The accent's colour (its first colour when it's a gradient).</summary>
    public Color AccentColor { get; }

    public Brush Background { get; }

    public Brush Track { get; }

    /// <summary>Warning colour rules can name, e.g. a CPU running hot.</summary>
    public Brush Warning { get; }

    public Brush Critical { get; }

    /// <summary>Whether a value in <see cref="Warning"/> or <see cref="Critical"/> has a <see cref="Widgets.WarningSymbol"/> beside it too.</summary>
    public bool WarningSymbols { get; }

    public static Theme From(ThemeSettings settings) => new(settings);

    /// <summary>
    /// Whether a font setting is a place to fetch a font from rather than a font's name (or a list of them, such as
    /// "Cascadia Mono, Consolas"). WPF reads "file://server/fonts/#Name" or "\\server\fonts\#Name" as a font at that
    /// address, and drawing with it would have Windows reach out to that PC, sending your sign-in. Layouts and themes
    /// from someone else leave such fonts out as they come in, and one that's still there is drawn in another font.
    /// </summary>
    public static bool IsFontAddress(string? font) => font is not null && font.IndexOfAny(FontAddressCharacters) >= 0;

    private static readonly char[] FontAddressCharacters = ['/', '\\', '#', ':'];

    /// <summary>
    /// This theme with a widget's own font and colours (see <see cref="Widgets.Setting.ThemeOverrides"/>) in place of its
    /// values, or this theme itself when the widget has none.
    /// </summary>
    public Theme WithOverrides(System.Text.Json.Nodes.JsonObject widget)
    {
        var changed = false;
        var copy = _settings.Copy();
        string? Get(string key) => widget.GetString(key) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        void Set(string key, Action<string> apply)
        {
            if (Get(key) is { } value)
            {
                apply(value);
                changed = true;
            }
        }

        // A font given by its address (see IsFontAddress) leaves the widget in the theme's font.
        if (Get("fontFamily") is { } font && !IsFontAddress(font))
        {
            copy.FontFamily = font;
            copy.FontFile = null;
            changed = true;
        }
        if (Get("fontWeight") is { } weight && weight != Widgets.Setting.FromTheme)
        {
            copy.FontWeight = weight;
            changed = true;
        }
        Set("foreground", v => copy.Foreground = v);
        Set("secondary", v => copy.Secondary = v);
        Set("accent", v => { copy.Accent = v; copy.Accent2 = null; });
        Set("track", v => copy.Track = v);
        Set("warning", v => copy.Warning = v);
        Set("critical", v => copy.Critical = v);
        return changed ? new Theme(copy) : this;
    }

    /// <summary>The background colour with the theme's background image (if any) over it, for a canvas of this size.</summary>
    public Brush CanvasBackground(double width, double height) =>
        BackgroundImage.CreateBrush(_settings, Background, width, height);

    /// <summary>As above, with an animated GIF or video moving, kept going by <paramref name="player"/>.</summary>
    public Brush CanvasBackground(double width, double height, bool animate, out BackgroundPlayer? player) =>
        BackgroundImage.CreateBrush(_settings, Background, width, height, animate, out player);

    /// <summary>
    /// Resolves a widget colour setting: <c>foreground</c>, <c>secondary</c>, <c>accent</c>, <c>warning</c>, <c>critical</c>,
    /// <c>track</c>, <c>background</c> or a <c>#RRGGBB</c>/<c>#AARRGGBB</c> value.
    /// </summary>
    public Brush Resolve(string? value, Brush fallback) => value?.ToLowerInvariant() switch
    {
        null or "" => fallback,
        "foreground" => Foreground,
        "secondary" => Secondary,
        "accent" => Accent,
        "warning" => Warning,
        "critical" => Critical,
        "track" => Track,
        "background" => Background,
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

        // A layout saved before imports checked font names, or edited by hand, may still have an address here.
        return new FontFamily(IsFontAddress(settings.FontFamily) ? "Segoe UI" : settings.FontFamily + ", Segoe UI");
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

    /// <summary><paramref name="from"/> fading to <paramref name="to"/> (left to right, or top to bottom), or just <paramref name="from"/> when there's no second colour.</summary>
    private static Brush Gradient(Brush from, string? to, bool vertical)
    {
        if (from is not SolidColorBrush start || ParseBrush(to, Brushes.Transparent) is not SolidColorBrush { Color.A: > 0 } end)
            return from;
        var brush = new LinearGradientBrush(start.Color, end.Color, vertical ? 90 : 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// A soft halo: a shadow with no offset, wider and stronger as <paramref name="amount"/> goes from 0 to 1. Effects
    /// cost CPU in software rendering, though the dashboard only redraws once a second.
    /// </summary>
    internal static Effect? CreateGlow(double? amount, Color color)
    {
        if (amount is not { } glow || !double.IsFinite(glow) || glow <= 0)
            return null;
        glow = Math.Min(glow, 1);
        var effect = new DropShadowEffect
        {
            Color = color,
            ShadowDepth = 0,
            BlurRadius = 6 + 18 * glow,
            Opacity = 0.35 + 0.65 * glow,
            RenderingBias = RenderingBias.Performance,
        };
        effect.Freeze();
        return effect;
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

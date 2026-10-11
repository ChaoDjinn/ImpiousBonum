using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Descriptors for the things the editor edits that aren't widget settings: a widget's position and size,
/// the canvas, and the theme. Described the same way so the properties panel treats them all alike.
/// </summary>
public static class EditorDescriptors
{
    public const string PositionGroup = "Position and size";

    public static readonly IReadOnlyList<SettingDescriptor> Geometry =
    [
        Setting.Number("x", "X", 0, -10_000, 10_000, PositionGroup),
        Setting.Number("y", "Y", 0, -10_000, 10_000, PositionGroup),
        Setting.Number("width", "Width", 200, 1, 10_000, PositionGroup),
        Setting.Number("height", "Height", 100, 1, 10_000, PositionGroup),
    ];

    public static readonly IReadOnlyList<SettingDescriptor> Canvas =
    [
        Setting.Number("width", "Width", 1920, 50, 10_000, "Canvas",
            help: "Design size in canvas units. The whole canvas scales to fit the dashboard window."),
        Setting.Number("height", "Height", 480, 50, 10_000, "Canvas"),
    ];

    public static readonly IReadOnlyList<string> FontWeights =
        ["Thin", "ExtraLight", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black"];

    public const string BackgroundGroup = "Background";

    public static IReadOnlyList<SettingDescriptor> Theme { get; } = CreateTheme();

    private static IReadOnlyList<SettingDescriptor> CreateTheme()
    {
        // Defaults come from ThemeSettings itself so there's one source of truth.
        var defaults = new ThemeSettings();
        const string font = "Font";
        const string colours = "Colours";
        const string effects = "Gradients and glow";
        return
        [
            new("fontFamily", "Font", SettingKind.Font, defaults.FontFamily, font),
            Setting.Choice("fontWeight", "Weight", defaults.FontWeight, FontWeights, font),
            new("fontFile", "Font file", SettingKind.FontFile, defaults.FontFile, font,
                "Use a .ttf/.otf file without installing it. Overrides the font above."),
            Setting.Color("foreground", "Text", defaults.Foreground, colours, named: false),
            Setting.Color("secondary", "Secondary text", defaults.Secondary, colours, "Values in widget headers and rows.", named: false),
            Setting.Color("accent", "Accent", defaults.Accent, colours, "Graphs, bars and icons.", named: false),
            Setting.Color("background", "Background", defaults.Background, colours, named: false),
            Setting.Color("track", "Bar track", defaults.Track, colours, "The unfilled part of bars.", named: false),
            Setting.Color("warning", "Warning", defaults.Warning, colours, "Used by warning colour rules that say \"warning\".", named: false),
            Setting.Color("critical", "Critical", defaults.Critical, colours, "Used by warning colour rules that say \"critical\".", named: false),
            new("backgroundImage", "Image", SettingKind.ImageFile, defaults.BackgroundImage, BackgroundGroup,
                "A PNG or JPG drawn behind the widgets, over the background colour."),
            Setting.Choice("backgroundFit", "Fit", defaults.BackgroundFit, BackgroundImage.Fits, BackgroundGroup,
                "fill covers the canvas (cropping), fit shows it all, stretch distorts to fit, center and tile keep its size."),
            Setting.Number("backgroundOpacity", "Opacity", defaults.BackgroundOpacity, 0, 1, BackgroundGroup,
                "Below 1 the background colour shows through, e.g. 0.4 to dim a busy picture on black."),
            new("accent2", "Accent fades to", SettingKind.Color, defaults.Accent2, effects,
                "A second accent colour: graphs, bars, gauges and icons fade from the accent to this, left to right. Empty is a plain accent."),
            new("backgroundGradient", "Background fades to", SettingKind.Color, defaults.BackgroundGradient, effects,
                "The background fades from its colour at the top to this at the bottom. Empty is a plain background."),
            Setting.Number("glow", "Glow", defaults.Glow, 0, 1, effects,
                "A soft neon glow around every widget's text and shapes: 0.3 is subtle, 1 is strong. Empty or 0 is none. Uses a little more CPU."),
            new("glowColor", "Glow colour", SettingKind.Color, defaults.GlowColor, effects, "Empty glows in the accent colour."),
        ];
    }
}

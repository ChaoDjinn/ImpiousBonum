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

    public static IReadOnlyList<SettingDescriptor> Theme { get; } = CreateTheme();

    private static IReadOnlyList<SettingDescriptor> CreateTheme()
    {
        // Defaults come from ThemeSettings itself so there's one source of truth.
        var defaults = new ThemeSettings();
        const string font = "Font";
        const string colours = "Colours";
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
        ];
    }
}

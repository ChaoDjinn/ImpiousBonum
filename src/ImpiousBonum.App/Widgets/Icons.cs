using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImpiousBonum.App.Widgets;

/// <summary>Stroked line icons on a 24×24 grid, drawn in the accent colour.</summary>
public static class Icons
{
    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chip with pins on every side.
        ["cpu"] = Parse("M7,5 H17 A2,2 0 0 1 19,7 V17 A2,2 0 0 1 17,19 H7 A2,2 0 0 1 5,17 V7 A2,2 0 0 1 7,5 Z " +
                        "M9.5,9.5 H14.5 V14.5 H9.5 Z " +
                        "M9,2 V5 M12,2 V5 M15,2 V5 M9,19 V22 M12,19 V22 M15,19 V22 " +
                        "M2,9 H5 M2,12 H5 M2,15 H5 M19,9 H22 M19,12 H22 M19,15 H22"),
        // Graphics card: bracket, board, fan, edge connector.
        ["gpu"] = Parse("M1.5,5 H4 V21 " +
                        "M4,7 H21 A1.5,1.5 0 0 1 22.5,8.5 V15.5 A1.5,1.5 0 0 1 21,17 H4 " +
                        "M16,9.5 A2.5,2.5 0 1 1 15.99,9.5 Z " +
                        "M7,17 V19.5 H13 V17"),
        // Memory stick.
        ["ram"] = Parse("M2,7 H22 V16 H2 Z M6,10 V13 M10,10 V13 M14,10 V13 M18,10 V13 M5,16 V19 M9,16 V19 M15,16 V19 M19,16 V19"),
        // Hard drive.
        ["disk"] = Parse("M3,14 L6,5 H18 L21,14 V19 H3 Z M3,14 H21 M17,16.5 H17.01"),
        // Up/down arrows.
        ["network"] = Parse("M8,20 V4 M4,8 L8,4 L12,8 M16,4 V20 M12,16 L16,20 L20,16"),
        // Gauge.
        ["fps"] = Parse("M4,17 A9,9 0 1 1 20,17 M12,14 L16,8"),

        // Media controls.
        ["play"] = Parse("M7,4.5 L19,12 L7,19.5 Z"),
        ["pause"] = Parse("M7,5 H10 V19 H7 Z M14,5 H17 V19 H14 Z"),
        ["play-pause"] = Parse("M3,5 L11,12 L3,19 Z M15,5 V19 M20,5 V19"),
        ["next"] = Parse("M5,5 L14,12 L5,19 Z M18,5 V19"),
        ["previous"] = Parse("M19,5 L10,12 L19,19 Z M6,5 V19"),
        ["stop"] = Parse("M6,6 H18 V18 H6 Z"),
        ["volume-down"] = Parse(Speaker + " M16,9.5 A3,3 0 0 1 16,14.5"),
        ["volume-up"] = Parse(Speaker + " M16,9.5 A3,3 0 0 1 16,14.5 M18.5,6.5 A7.5,7.5 0 0 1 18.5,17.5"),
        ["mute"] = Parse(Speaker + " M16,9.5 L21,14.5 M21,9.5 L16,14.5"),
        // Two beamed notes.
        ["music"] = Parse("M9,17.5 V5.5 L20,3.5 V15.5 M9,17.5 A2.5,2.5 0 1 1 4,17.5 A2.5,2.5 0 1 1 9,17.5 Z " +
                          "M20,15.5 A2.5,2.5 0 1 1 15,15.5 A2.5,2.5 0 1 1 20,15.5 Z"),

        // For buttons.
        ["folder"] = Parse("M3,6.5 A1.5,1.5 0 0 1 4.5,5 H9 L11,7.5 H19.5 A1.5,1.5 0 0 1 21,9 V17.5 A1.5,1.5 0 0 1 19.5,19 H4.5 A1.5,1.5 0 0 1 3,17.5 Z"),
        ["globe"] = Parse("M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 C8.5,6.5 8.5,17.5 12,21 M12,3 C15.5,6.5 15.5,17.5 12,21"),
        ["layout"] = Parse("M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z"),
        ["keyboard"] = Parse("M2.5,6 H21.5 V18 H2.5 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M7,14.5 H17"),
        ["terminal"] = Parse("M3,4.5 H21 V19.5 H3 Z M7,9.5 L10,12 L7,14.5 M12.5,14.5 H17"),
        ["gamepad"] = Parse("M7.5,7.5 H16.5 A4.5,4.5 0 0 1 21,12 V14.5 A3,3 0 0 1 15.8,16.6 L14.5,15.5 H9.5 L8.2,16.6 A3,3 0 0 1 3,14.5 V12 A4.5,4.5 0 0 1 7.5,7.5 Z " +
                            "M7.5,10.5 V13.5 M6,12 H9 M15,11 H15.01 M17,13 H17.01"),
        ["sliders"] = Parse("M4,7 H20 M4,17 H20 M9,4.5 V9.5 M15,14.5 V19.5"),
        ["chat"] = Parse("M4,5 H20 V16 H10 L5.5,20 V16 H4 Z"),
        ["camera"] = Parse("M3,8 H7 L9,5.5 H15 L17,8 H21 V19 H3 Z M12,10 A3.5,3.5 0 1 1 11.99,10 Z"),
        ["home"] = Parse("M3,11 L12,3.5 L21,11 M5.5,9 V20 H18.5 V9 M10,20 V14.5 H14 V20"),
        ["star"] = Parse("M12,3 L14.7,8.8 L21,9.5 L16.3,13.8 L17.6,20 L12,16.8 L6.4,20 L7.7,13.8 L3,9.5 L9.3,8.8 Z"),
        ["lock"] = Parse("M5.5,11 H18.5 V20.5 H5.5 Z M8,11 V8 A4,4 0 0 1 16,8 V11"),
        ["power"] = Parse("M12,3 V11 M7,5.8 A8,8 0 1 0 17,5.8"),
        ["moon"] = Parse("M20,14.5 A8.5,8.5 0 1 1 9.5,4 A6.5,6.5 0 0 0 20,14.5 Z"),
        ["link"] = Parse("M10,14 L14,10 M8.5,11 L6.5,13 A3.5,3.5 0 0 0 11,17.5 L13,15.5 M15.5,13 L17.5,11 A3.5,3.5 0 0 0 13,6.5 L11,8.5"),

        // Weather, named as WeatherCodes.Icon returns them.
        ["clear-day"] = Parse("M12,7.5 A4.5,4.5 0 1 1 11.99,7.5 Z M12,1.5 V4 M12,20 V22.5 M1.5,12 H4 M20,12 H22.5 " +
                              "M4.6,4.6 L6.3,6.3 M17.7,17.7 L19.4,19.4 M4.6,19.4 L6.3,17.7 M17.7,6.3 L19.4,4.6"),
        ["clear-night"] = Parse("M19,15 A8,8 0 1 1 9,4.5 A6.2,6.2 0 0 0 19,15 Z M17,3.5 V6.5 M15.5,5 H18.5"),
        ["partly-day"] = Parse("M4.6,11.6 A4,4 0 0 1 12.2,7.4 M8,2 V3.5 M2,8 H3.5 M3.8,3.8 L4.8,4.8 M12.2,3.8 L11.2,4.8 " + Cloud),
        ["partly-night"] = Parse("M10.8,7.2 A4.2,4.2 0 0 1 4.6,11.9 A5,5 0 0 1 7.6,3 A3.6,3.6 0 0 0 10.8,7.2 Z " + Cloud),
        ["cloud"] = Parse("M6.5,19 H17.5 A4.5,4.5 0 0 0 17.4,10 A6,6 0 0 0 6.1,11.6 A3.75,3.75 0 0 0 6.5,19 Z"),
        ["fog"] = Parse("M6.5,13 A3.25,3.25 0 0 1 6.9,7.3 A5.25,5.25 0 0 1 16.9,6.5 A3.25,3.25 0 0 1 18,13 M3,16.5 H21 M5,20 H19"),
        ["drizzle"] = Parse(RainCloud + " M8,18.5 V19 M12,20 V20.5 M16,18.5 V19"),
        ["rain"] = Parse(RainCloud + " M8.5,17.5 L7,21.5 M12.5,17.5 L11,21.5 M16.5,17.5 L15,21.5"),
        ["sleet"] = Parse(RainCloud + " M8.5,17.5 L7.5,20 M16.5,17.5 L15.5,20 M12,19.5 V19.51 M12,22 V22.01"),
        ["snow"] = Parse(RainCloud + " M8,18.5 V18.51 M12,18.5 V18.51 M16,18.5 V18.51 M10,21.5 V21.51 M14,21.5 V21.51"),
        ["storm"] = Parse(RainCloud + " M13,15.5 L10.5,19 H13.5 L11,22.5"),
        ["drop"] = Parse("M12,3 C12,3 5.5,10.5 5.5,14.5 A6.5,6.5 0 0 0 18.5,14.5 C18.5,10.5 12,3 12,3 Z"),
        ["wind"] = Parse("M3,9 H14.5 A2.75,2.75 0 1 0 11.75,6.25 M3,13 H18.5 A2.75,2.75 0 1 1 15.75,15.75 M3,17 H10"),
    };

    /// <summary>A cloud in the lower right, in front of a sun or moon.</summary>
    private const string Cloud = "M9,20 H18 A3.75,3.75 0 0 0 18,12.5 A5,5 0 0 0 8.6,13.6 A3.2,3.2 0 0 0 9,20 Z";

    /// <summary>A cloud in the upper part, with room underneath for rain or snow.</summary>
    private const string RainCloud = "M6.5,15 H17.5 A4,4 0 0 0 17.4,7 A5.5,5.5 0 0 0 6.9,8.5 A3.25,3.25 0 0 0 6.5,15 Z";

    /// <summary>A loudspeaker, for the volume icons to add waves or a cross to.</summary>
    private const string Speaker = "M3.5,9.5 H7.5 L12.5,5.5 V18.5 L7.5,14.5 H3.5 Z";

    /// <summary>The value for "no icon" where a widget lets you leave it out, e.g. a text-only button.</summary>
    public const string None = "none";

    public static IReadOnlyList<string> Names { get; } = [.. Geometries.Keys];

    public static Geometry? Get(string? name) => name is not null && Geometries.TryGetValue(name, out var geometry) ? geometry : null;

    /// <summary>
    /// An icon drawn at <paramref name="size"/> (DIPs square). <paramref name="strokeWidth"/> is on the 24×24 grid, so it
    /// scales with the icon. <paramref name="filled"/> fills closed shapes too, as media controls usually are.
    /// </summary>
    public static FrameworkElement Create(string? name, Brush brush, double size, double strokeWidth = 2, bool filled = false) => new Viewbox
    {
        Width = size,
        Height = size,
        Child = new Path
        {
            Data = Get(name),
            Stroke = brush,
            Fill = filled ? brush : null,
            StrokeThickness = strokeWidth,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 24,
            Height = 24,
        },
    };

    /// <summary>One coloured layer of a full-colour weather icon: a shape, its fill (or none) and its outline.</summary>
    private sealed record Layer(Geometry Shape, Color? Fill, Color Stroke, double StrokeScale = 1);

    private static readonly Color SunFill = Color.FromRgb(0xFF, 0xC8, 0x3D);
    private static readonly Color SunEdge = Color.FromRgb(0xF5, 0xA6, 0x23);
    private static readonly Color MoonFill = Color.FromRgb(0xF4, 0xE7, 0xB0);
    private static readonly Color MoonEdge = Color.FromRgb(0xD9, 0xC5, 0x7A);
    private static readonly Color WhiteCloud = Color.FromRgb(0xF4, 0xF6, 0xF9);
    private static readonly Color WhiteCloudEdge = Color.FromRgb(0xB9, 0xC2, 0xCE);
    private static readonly Color GreyCloud = Color.FromRgb(0xC9, 0xD0, 0xD9);
    private static readonly Color GreyCloudEdge = Color.FromRgb(0x95, 0x9F, 0xAC);
    private static readonly Color RainCloudFill = Color.FromRgb(0x9A, 0xA4, 0xB1);
    private static readonly Color RainCloudEdge = Color.FromRgb(0x6E, 0x78, 0x86);
    private static readonly Color StormCloudFill = Color.FromRgb(0x5B, 0x64, 0x70);
    private static readonly Color StormCloudEdge = Color.FromRgb(0x3E, 0x45, 0x4F);
    private static readonly Color Rain = Color.FromRgb(0x4D, 0xA3, 0xFF);
    private static readonly Color Snow = Color.FromRgb(0xE6, 0xF2, 0xFF);
    private static readonly Color Bolt = Colors.White;
    private static readonly Color Mist = Color.FromRgb(0xB4, 0xBC, 0xC7);

    private const string SunRays = "M12,1.5 V4 M12,20 V22.5 M1.5,12 H4 M20,12 H22.5 M4.6,4.6 L6.3,6.3 M17.7,17.7 L19.4,19.4 M4.6,19.4 L6.3,17.7 M17.7,6.3 L19.4,4.6";
    private const string SmallSun = "M4,8 A4,4 0 1 1 12,8 A4,4 0 1 1 4,8 Z";
    private const string SmallSunRays = "M8,1.5 V2.8 M1.5,8 H2.8 M3.4,3.4 L4.3,4.3 M12.6,3.4 L11.7,4.3";

    /// <summary>The full-colour weather icons, drawn back to front: a yellow sun, white clouds, grey rain clouds with blue rain, and so on.</summary>
    private static readonly Dictionary<string, Layer[]> ColourWeather = new()
    {
        ["clear-day"] = [new(Parse(SunRays), null, SunEdge), new(Parse("M12,7.5 A4.5,4.5 0 1 1 11.99,7.5 Z"), SunFill, SunEdge)],
        ["clear-night"] = [new(Parse("M19,15 A8,8 0 1 1 9,4.5 A6.2,6.2 0 0 0 19,15 Z"), MoonFill, MoonEdge), new(Parse("M17,3.5 V6.5 M15.5,5 H18.5"), null, MoonFill)],
        ["partly-day"] = [new(Parse(SmallSunRays), null, SunEdge), new(Parse(SmallSun), SunFill, SunEdge), new(Parse(Cloud), WhiteCloud, WhiteCloudEdge)],
        ["partly-night"] = [new(Parse("M10.8,7.2 A4.2,4.2 0 0 1 4.6,11.9 A5,5 0 0 1 7.6,3 A3.6,3.6 0 0 0 10.8,7.2 Z"), MoonFill, MoonEdge), new(Parse(Cloud), WhiteCloud, WhiteCloudEdge)],
        ["cloud"] = [new(Parse("M6.5,19 H17.5 A4.5,4.5 0 0 0 17.4,10 A6,6 0 0 0 6.1,11.6 A3.75,3.75 0 0 0 6.5,19 Z"), GreyCloud, GreyCloudEdge)],
        ["fog"] = [new(Parse("M6.5,13 A3.25,3.25 0 0 1 6.9,7.3 A5.25,5.25 0 0 1 16.9,6.5 A3.25,3.25 0 0 1 18,13 Z"), GreyCloud, GreyCloudEdge), new(Parse("M3,16.5 H21 M5,20 H19"), null, Mist)],
        ["drizzle"] = [new(Parse(RainCloud), RainCloudFill, RainCloudEdge), new(Parse("M8,18.5 V19 M12,20 V20.5 M16,18.5 V19"), null, Rain, 1.2)],
        ["rain"] = [new(Parse(RainCloud), RainCloudFill, RainCloudEdge), new(Parse("M8.5,17.5 L7,21.5 M12.5,17.5 L11,21.5 M16.5,17.5 L15,21.5"), null, Rain, 1.2)],
        ["sleet"] = [new(Parse(RainCloud), RainCloudFill, RainCloudEdge), new(Parse("M8.5,17.5 L7.5,20 M16.5,17.5 L15.5,20"), null, Rain, 1.2), new(Parse("M12,19.5 V19.51 M12,22 V22.01"), null, Snow, 1.4)],
        ["snow"] = [new(Parse(RainCloud), GreyCloud, GreyCloudEdge), new(Parse("M8,18.5 V18.51 M12,18.5 V18.51 M16,18.5 V18.51 M10,21.5 V21.51 M14,21.5 V21.51"), null, Snow, 1.6)],
        ["storm"] = [new(Parse(RainCloud), StormCloudFill, StormCloudEdge), new(Parse("M13,15.5 L10.5,19 H13.5 L11,22.5"), null, Bolt, 1.2)],
    };

    /// <summary>Whether a weather icon has a full-colour version (every name <see cref="WeatherCodes"/> gives does).</summary>
    public static bool HasColour(string? name) => name is not null && ColourWeather.ContainsKey(name);

    /// <summary>
    /// A weather icon in its natural colours, the same size and shape as <see cref="Create"/> draws it, falling back to the
    /// plain icon in <paramref name="fallback"/> for a name with no colour version.
    /// </summary>
    public static FrameworkElement CreateColour(string? name, double size, double strokeWidth, Brush fallback)
    {
        if (name is null || !ColourWeather.TryGetValue(name, out var layers))
            return Create(name, fallback, size, strokeWidth);
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var layer in layers)
        {
            canvas.Children.Add(new Path
            {
                Data = layer.Shape,
                Fill = layer.Fill is { } fill ? Frozen(fill) : null,
                Stroke = Frozen(layer.Stroke),
                StrokeThickness = strokeWidth * layer.StrokeScale,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static readonly Dictionary<Color, Brush> ColourBrushes = new();

    private static Brush Frozen(Color color)
    {
        lock (ColourBrushes)
        {
            if (!ColourBrushes.TryGetValue(color, out var brush))
            {
                var solid = new SolidColorBrush(color);
                solid.Freeze();
                ColourBrushes[color] = brush = solid;
            }
            return brush;
        }
    }

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}

using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class PictureThemeTests : IDisposable
{
    private static readonly Color Navy = Color.FromRgb(0x10, 0x30, 0x60);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0xA5, 0x00);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ib-picture-" + Guid.NewGuid().ToString("N"));

    public PictureThemeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Two_colours_come_back_exactly_with_their_shares() => Sta.Run(() =>
    {
        var swatches = Palette.FromPicture(Picture(400, 300, (x, _) => x < 300 ? Navy : Orange));

        Assert.Equal(2, swatches.Count);
        Assert.Equal((Navy, 0.75), (swatches[0].Colour, Math.Round(swatches[0].Share, 2)));
        Assert.Equal((Orange, 0.25), (swatches[1].Colour, Math.Round(swatches[1].Share, 2)));

        // The accent is the vivid orange, the background the navy made darker.
        var theme = PictureTheme.FromPicture(swatches);
        Assert.InRange(Oklab.From(Parse(theme["accent"])).Hue, 55, 85);
        Assert.InRange(Oklab.From(Parse(theme["background"])).Hue, 240, 270);
        Assert.True(Oklab.From(Parse(theme["background"])).L < Oklab.From(Navy).L);
    });

    [Fact]
    public void A_single_colour_is_the_whole_palette() => Sta.Run(() =>
    {
        var purple = Color.FromRgb(0x8E, 0x24, 0xAA);
        var swatch = Assert.Single(Palette.FromPicture(Picture(64, 64, (_, _) => purple)));

        Assert.Equal(purple, swatch.Colour);
        Assert.Equal(1, swatch.Share);
    });

    [Fact]
    public void A_gradient_is_spread_along_its_length() => Sta.Run(() =>
    {
        var swatches = Palette.FromPicture(Picture(256, 32, (x, _) => Color.FromRgb((byte)x, (byte)x, (byte)x)));
        var lightness = swatches.Select(s => Oklab.From(s.Colour).L).Order().ToList();

        Assert.Equal(Palette.MaxColours, swatches.Count);
        Assert.Equal(1, swatches.Sum(s => s.Share), 6);
        Assert.All(swatches, s => Assert.InRange(s.Share, 0.08, 0.17));
        Assert.True(lightness[0] < 0.25 && lightness[^1] > 0.9, $"From {lightness[0]:0.00} to {lightness[^1]:0.00}.");
    });

    [Fact]
    public void A_mostly_white_picture_makes_a_light_theme() => Sta.Run(() =>
    {
        var green = Color.FromRgb(0x2E, 0x7D, 0x32);
        var swatches = Palette.FromPicture(Picture(200, 100, (x, _) => x < 180 ? Colors.White : green));
        var theme = PictureTheme.FromPicture(swatches);

        Assert.Equal((Colors.White, 0.9), (swatches[0].Colour, Math.Round(swatches[0].Share, 2)));
        Assert.True(Oklab.From(Parse(theme["background"])).L > 0.9);
        Assert.True(Oklab.From(Parse(theme["foreground"])).L < 0.4);
        // Its one colour is the accent, dark enough to show up on white.
        Assert.InRange(Oklab.From(Parse(theme["accent"])).Hue, 130, 155);

        // The same picture mostly navy makes a dark theme.
        var dark = PictureTheme.FromPicture(Palette.FromPicture(Picture(200, 100, (x, _) => x < 180 ? Navy : green)));
        Assert.True(Oklab.From(Parse(dark["background"])).L < 0.3);
        Assert.True(Oklab.From(Parse(dark["foreground"])).L > 0.9);
    });

    [Fact]
    public void A_grey_picture_makes_a_grey_theme_with_coloured_warnings() => Sta.Run(() =>
    {
        var random = new Random(3);
        var swatches = Palette.FromPicture(Picture(120, 80, (_, _) =>
        {
            var v = (byte)random.Next(256);
            return Color.FromRgb(v, v, v);
        }));
        var theme = PictureTheme.FromPicture(swatches);

        Assert.All(swatches, s => Assert.True(Oklab.From(s.Colour).Chroma < 0.01));
        foreach (var key in new[] { "foreground", "secondary", "accent", "accent2", "background", "track" })
            Assert.True(Oklab.From(Parse(theme[key])).Chroma < 0.01, $"{key} {theme[key]} isn't grey.");
        Assert.True(Oklab.From(Parse(theme["warning"])).Chroma > 0.1);
        Assert.True(Oklab.From(Parse(theme["critical"])).Chroma > 0.1);
    });

    [Fact]
    public void Transparent_parts_of_a_picture_dont_count() => Sta.Run(() =>
    {
        var swatch = Assert.Single(Palette.FromPicture(Picture(100, 100, (x, _) => x < 70 ? Colors.Transparent : Orange)));
        Assert.Equal((Orange, 1.0), (swatch.Colour, swatch.Share));
    });

    [Fact]
    public void A_picture_file_is_read_whatever_its_name_and_a_broken_one_isnt() => Sta.Run(() =>
    {
        // Windows' copy of the wallpaper showing has no extension.
        var copy = Path.Combine(_folder, "TranscodedWallpaper");
        Save(Picture(100, 50, (x, _) => x < 60 ? Navy : Orange), copy);
        var broken = Path.Combine(_folder, "broken.png");
        File.WriteAllText(broken, "not a picture");

        Assert.Equal([Navy, Orange], Palette.FromFile(copy)!.Select(s => s.Colour));
        Assert.Null(Palette.FromFile(broken));
        Assert.Null(Palette.FromFile(Path.Combine(_folder, "missing.png")));
    });

    /// <summary>
    /// However dark, light, dull or garish the picture, the theme keeps to the contrast the built-in themes do, its
    /// warnings look different from each other and from the accent, and it's a valid theme.
    /// </summary>
    [Fact]
    public void Every_theme_made_is_readable_and_tells_warnings_apart() => Sta.Run(() =>
    {
        var themes = new List<(string What, IReadOnlyDictionary<string, string?> Colours)>
        {
            ("the album artwork", PictureTheme.FromPicture(Palette.FromPicture(BitmapFrame.Create(new MemoryStream(DemoArtwork.Png()))))),
            ("the landscape", PictureTheme.FromPicture(Palette.FromPicture(DemoArtwork.Landscape()))),
            ("black", PictureTheme.FromColour(Colors.Black, light: false)),
            ("white", PictureTheme.FromColour(Colors.White, light: true)),
            ("white made dark", PictureTheme.FromColour(Colors.White, light: false)),
            ("black made light", PictureTheme.FromColour(Colors.Black, light: true)),
        };
        // Colours round the wheel, dull to vivid, dark to light, as pictures and as Windows' accent in both modes.
        for (var hue = 0; hue < 360; hue += 15)
        {
            foreach (var chroma in new[] { 0.03, 0.1, 0.2, 0.3 })
            {
                foreach (var lightness in new[] { 0.2, 0.45, 0.7, 0.9 })
                {
                    var colour = Oklab.FromLch(lightness, chroma, hue).ToColor();
                    var name = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
                    themes.Add((name, PictureTheme.FromPicture([new Swatch(colour, 0.7), new Swatch(Oklab.FromLch(1 - lightness, chroma, hue + 150).ToColor(), 0.3)])));
                    themes.Add(($"{name} dark", PictureTheme.FromColour(colour, light: false)));
                    themes.Add(($"{name} light", PictureTheme.FromColour(colour, light: true)));
                }
            }
        }

        foreach (var (what, colours) in themes)
        {
            Assert.Equal(PictureTheme.Keys.Order(), colours.Keys.Order());
            var background = colours["background"]!;
            Assert.True(ColourContrast.Ratio(colours["foreground"]!, background) >= ColourContrast.Text, $"From {what}: the text is hard to read.");
            foreach (var key in new[] { "secondary", "accent", "accent2", "warning", "critical" })
                Assert.True(ColourContrast.Ratio(colours[key]!, background) >= ColourContrast.Graphics, $"From {what}: {key} {colours[key]} is hard to see on {background}.");

            var (warning, critical, accent) = (Oklab.From(Parse(colours["warning"])), Oklab.From(Parse(colours["critical"])), Oklab.From(Parse(colours["accent"])));
            Assert.True(warning.DistanceTo(critical) >= 0.1, $"From {what}: warning {colours["warning"]} and critical {colours["critical"]} look alike.");
            Assert.True(warning.DistanceTo(accent) >= 0.05, $"From {what}: warning {colours["warning"]} looks like the accent {colours["accent"]}.");
            Assert.True(critical.DistanceTo(accent) >= 0.05, $"From {what}: critical {colours["critical"]} looks like the accent {colours["accent"]}.");

            var theme = new JsonObject();
            foreach (var (key, value) in colours)
                theme[key] = value;
            Assert.Empty(LayoutValidator.Validate(new LayoutDocument { Theme = theme }));
        }
    });

    [Fact]
    public void Colours_go_on_over_the_theme_as_one_undo_step_and_can_be_saved_as_a_theme()
    {
        var themes = new ThemeLibrary(_folder);
        var session = new LayoutSession(new LayoutDocument { Theme = new JsonObject { ["base"] = "Outrun", ["fontFamily"] = "Bahnschrift" } }, themes);
        var before = session.Document.Theme.ToJsonString();
        var colours = PictureTheme.FromColour(Color.FromRgb(0x00, 0x78, 0xD4), light: false);

        session.SetThemeValues(colours);

        // Every colour is the layout's own now; Outrun's background fade is cleared rather than left to clash.
        Assert.Equal("Outrun", session.ThemeName);
        Assert.Equal(colours["accent"], session.ResolvedTheme.Accent);
        Assert.Equal(colours["background"], session.ResolvedTheme.Background);
        Assert.Null(session.ResolvedTheme.BackgroundGradient);
        Assert.Equal("Bahnschrift", session.ResolvedTheme.FontFamily);
        Assert.False(session.Document.Theme.ContainsKey("glowColor"));

        session.SaveThemeAs("Blue");
        Assert.Equal(colours["accent"], themes.Resolve(new JsonObject { ["base"] = "Blue" }).Accent);
        Assert.Null(themes.Resolve(new JsonObject { ["base"] = "Blue" }).BackgroundGradient);

        session.Undo();
        session.Undo();
        Assert.Equal(before, session.Document.Theme.ToJsonString());
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Colours_from_the_background_image_are_one_undo_step_in_the_editor() => Sta.Run(() =>
    {
        var image = Path.Combine(_folder, "background.png");
        Save(Picture(400, 300, (x, _) => x < 300 ? Navy : Orange), image);
        var session = new LayoutSession(new LayoutDocument { Theme = new JsonObject { ["backgroundImage"] = image } });
        var panel = new PropertyPanel(session, () => (1920, 480), () => new MetricStore());
        var window = new Window { Content = panel, Width = 440, Height = 3000, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        Pump();

        var button = Descendants<Button>(panel).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Colours from");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var items = button.ContextMenu.Items.OfType<MenuItem>().ToList();
        var background = items.Single(i => (string)i.Header == "Background image");
        var enabled = background.IsEnabled;
        background.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        button.ContextMenu.IsOpen = false;
        Pump();
        var accent = session.ResolvedTheme.Accent;
        session.Undo();
        window.Close();

        Assert.Equal(["Windows wallpaper", "Background image", "A picture…", "Windows accent colour"], items.Select(i => (string)i.Header));
        Assert.True(enabled);
        Assert.Equal(PictureTheme.FromPicture(Palette.FromFile(image)!)["accent"], accent);
        Assert.Equal(new JsonObject { ["backgroundImage"] = image }.ToJsonString(), session.Document.Theme.ToJsonString());
        Assert.False(session.CanUndo);
    });

    [Fact]
    public void The_wallpaper_is_the_picture_windows_names_or_the_copy_it_keeps_or_its_colour()
    {
        var picture = Path.Combine(_folder, "wallpaper.jpg");
        var copy = Path.Combine(_folder, "TranscodedWallpaper");
        var gone = Path.Combine(_folder, "moved.jpg");
        File.WriteAllText(picture, "");
        File.WriteAllText(copy, "");

        Assert.Equal(new Wallpaper(picture, Navy), WindowsWallpaper.Choose(picture, copy, 0, Navy));
        // A slideshow, Spotlight, or a picture since moved: the copy is the one showing.
        Assert.Equal(new Wallpaper(copy, Navy), WindowsWallpaper.Choose(gone, copy, 0, Navy));
        Assert.Equal(new Wallpaper(copy, Navy), WindowsWallpaper.Choose("", copy, 2, Navy));
        // A solid colour, even with a copy left over from a picture before.
        Assert.Equal(new Wallpaper(null, Navy), WindowsWallpaper.Choose("", copy, 1, Navy));
        Assert.Equal(new Wallpaper(null, Navy), WindowsWallpaper.Choose(picture, copy, 1, Navy));
        Assert.Equal(new Wallpaper(null, Navy), WindowsWallpaper.Choose("", copy, null, Navy));
    }

    private static Color Parse(string? colour) => (Color)ColorConverter.ConvertFromString(colour!);

    /// <summary>A picture with each pixel's colour from <paramref name="colour"/>.</summary>
    private static BitmapSource Picture(int width, int height, Func<int, int, Color> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = colour(x, y);
                var i = (y * width + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (c.B, c.G, c.R, c.A);
            }
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static void Save(BitmapSource picture, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }
}

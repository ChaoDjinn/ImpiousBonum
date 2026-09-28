using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class BackgroundImageTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ib-bg-" + Guid.NewGuid().ToString("N"));

    public BackgroundImageTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("fill", 3840, 2160, 1920, 480, 0.5, 0.5)] // width decides: 1920/3840 > 480/2160
    [InlineData("fill", 1000, 1000, 1920, 480, 1, 1)] // smaller than the canvas: never upscaled
    [InlineData("fit", 3840, 2160, 1920, 480, 480.0 / 2160, 480.0 / 2160)]
    [InlineData("stretch", 3840, 2160, 1920, 480, 0.5, 480.0 / 2160)]
    [InlineData("center", 3840, 2160, 1920, 480, 1, 1)]
    [InlineData("tile", 64, 64, 1920, 480, 1, 1)]
    public void Shrinks_no_further_than_the_canvas_needs(string fit, int sourceWidth, int sourceHeight, double width, double height, double scaleX, double scaleY)
    {
        var (x, y) = BackgroundImage.DecodeScale(fit, sourceWidth, sourceHeight, width, height);
        Assert.Equal(scaleX, x, 6);
        Assert.Equal(scaleY, y, 6);
    }

    [Fact]
    public void Fill_and_center_keep_only_the_visible_middle()
    {
        Assert.Equal(new Int32Rect(960, 840, 1920, 480), BackgroundImage.VisibleCrop("center", 3840, 2160, 1920, 480));
        Assert.Equal(new Int32Rect(0, 300, 1920, 480), BackgroundImage.VisibleCrop("fill", 1920, 1080, 1920, 480));
        Assert.Null(BackgroundImage.VisibleCrop("center", 800, 200, 1920, 480));
        Assert.Null(BackgroundImage.VisibleCrop("fit", 3840, 2160, 1920, 480));
    }

    [Fact]
    public void Missing_image_falls_back_to_the_colour() => Sta.Run(() =>
    {
        var theme = new ThemeSettings { Background = "#102030", BackgroundImage = System.IO.Path.Combine(_folder, "nope.png") };
        Assert.Equal(Color.FromRgb(0x10, 0x20, 0x30), Assert.IsType<SolidColorBrush>(Theme.From(theme).CanvasBackground(200, 50)).Color);
    });

    [Fact]
    public void Opacity_lets_the_colour_show_through() => Sta.Run(() =>
    {
        var path = WritePng(400, 100, Colors.Red);
        var theme = new ThemeSettings { Background = "#000000", BackgroundImage = path, BackgroundOpacity = 0.5 };

        var pixel = RenderCentre(Theme.From(theme).CanvasBackground(200, 50), 200, 50);

        Assert.InRange(pixel.R, 120, 136);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    });

    [Fact]
    public void Large_image_is_kept_at_canvas_size() => Sta.Run(() =>
    {
        var path = WritePng(3840, 2160, Colors.Blue);
        var theme = new ThemeSettings { BackgroundImage = path };

        var brush = Assert.IsType<DrawingBrush>(Theme.From(theme).CanvasBackground(1920, 480));
        var picture = (DrawingGroup)((DrawingGroup)brush.Drawing).Children[1];
        var bitmap = (BitmapSource)((ImageBrush)((GeometryDrawing)picture.Children[0]).Brush).ImageSource;

        Assert.Equal(1920, bitmap.PixelWidth);
        Assert.Equal(480, bitmap.PixelHeight);
        Assert.Equal(Colors.Blue, RenderCentre(brush, 1920, 480));
    });

    [Fact]
    public void Validator_reports_missing_and_unsupported_images_and_bad_settings()
    {
        var text = System.IO.Path.Combine(_folder, "notes.txt");
        File.WriteAllText(text, "hi");

        var missing = LayoutValidator.Validate(new LayoutDocument { Theme = new JsonObject { ["backgroundImage"] = System.IO.Path.Combine(_folder, "gone.jpg") } });
        Assert.Contains(missing, issue => issue.Contains("background image not found"));

        var unsupported = LayoutValidator.Validate(new LayoutDocument { Theme = new JsonObject { ["backgroundImage"] = text } });
        Assert.Contains(unsupported, issue => issue.Contains("background image should be one of"));

        var settings = LayoutValidator.Validate(new LayoutDocument { Theme = new JsonObject { ["backgroundFit"] = "zoom", ["backgroundOpacity"] = 2 } });
        Assert.Contains(settings, issue => issue.Contains("'backgroundFit'"));
        Assert.Contains(settings, issue => issue.Contains("'backgroundOpacity'"));
    }

    private string WritePng(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = System.IO.Path.Combine(_folder, "image.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static Color RenderCentre(Brush brush, int width, int height)
    {
        var rectangle = new Rectangle { Width = width, Height = height, Fill = brush };
        rectangle.Measure(new Size(width, height));
        rectangle.Arrange(new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(rectangle);
        var pixel = new byte[4];
        target.CopyPixels(new Int32Rect(width / 2, height / 2, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}

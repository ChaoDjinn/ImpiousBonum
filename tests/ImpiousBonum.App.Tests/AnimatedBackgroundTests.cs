using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class AnimatedBackgroundTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public AnimatedBackgroundTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(10, 100, 100, 1_000_000, 1, 1)]
    [InlineData(100, 1920, 480, 48L * 1024 * 1024, 0.5, 2)]
    [InlineData(40, 1000, 500, 48L * 1024 * 1024, 0.79, 1)]
    public void Frames_are_shrunk_then_thinned_to_fit_the_budget(int frames, int width, int height, long max, double scale, int step)
    {
        var (s, k) = AnimatedBackground.Budget(frames, width, height, max);
        Assert.Equal(scale, s, 2);
        Assert.Equal(step, k);
        Assert.True(Math.Ceiling(frames / (double)k) * width * s * height * s * 4 <= max * 1.01);
    }

    [Fact]
    public void A_gif_becomes_its_frames_at_the_canvas_size()
    {
        Sta.Run(() =>
        {
            var path = Gif([Colors.Red, Colors.Lime, Colors.Blue], 80, 40);
            var gif = AnimatedBackground.LoadGif(path, "fill", 40, 20);

            Assert.NotNull(gif);
            Assert.Equal(3, gif.Frames.Count);
            Assert.All(gif.Frames, f => Assert.Equal((40, 20), (f.PixelWidth, f.PixelHeight)));
            Assert.All(gif.Delays, d => Assert.True(d >= AnimatedBackground.ShortestDelay));
            Assert.Equal(Colors.Lime, Pixel(gif.Frames[1], 20, 10));
            Assert.Same(gif, AnimatedBackground.LoadGif(path, "fill", 40, 20));
        });
    }

    [Fact]
    public void A_gif_too_big_for_the_budget_keeps_its_loop_length()
    {
        Sta.Run(() =>
        {
            using var stream = File.OpenRead(Gif([Colors.Red, Colors.Lime, Colors.Blue, Colors.White], 100, 100));
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
            var full = AnimatedBackground.Compose(decoder, "stretch", 100, 100, long.MaxValue);
            // Room for half a frame: half size (a quarter of the bytes each), then every other frame.
            var squeezed = AnimatedBackground.Compose(decoder, "stretch", 100, 100, 100 * 100 * 4 / 2);

            Assert.Equal(4, full.Frames.Count);
            Assert.Equal(2, squeezed.Frames.Count);
            Assert.Equal((50, 50), (squeezed.Frames[0].PixelWidth, squeezed.Frames[0].PixelHeight));
            Assert.True(squeezed.Bytes <= 100 * 100 * 4 / 2);
            Assert.Equal(full.Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b), squeezed.Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b));
        });
    }

    [Fact]
    public void Only_a_dashboard_that_animates_gets_a_moving_brush()
    {
        Sta.Run(() =>
        {
            var settings = new ThemeSettings { BackgroundImage = Gif([Colors.Red, Colors.Blue], 40, 20) };

            var moving = BackgroundImage.CreateBrush(settings, Brushes.Black, 40, 20, animate: true, out var player);
            Assert.NotNull(player);
            Assert.False(moving.IsFrozen);
            player.Play();
            player.Dispose();

            var still = BackgroundImage.CreateBrush(settings, Brushes.Black, 40, 20, animate: false, out var none);
            Assert.Null(none);
            Assert.True(still.IsFrozen);

            var view = new DashboardView { AnimateBackground = false };
            view.Build(new LayoutDocument { Width = 40, Height = 20 }, settings);
            Assert.True(view.Background.IsFrozen);
        });
    }

    [Fact]
    public void A_video_background_gets_a_player_and_passes_the_validator()
    {
        var video = Path.Combine(_directory, "loop.mp4");
        File.WriteAllBytes(video, [0, 0, 0, 0]);
        Sta.Run(() =>
        {
            var brush = BackgroundImage.CreateBrush(new ThemeSettings { BackgroundImage = video }, Brushes.Black, 400, 200, animate: true, out var player);
            Assert.NotNull(player);
            player.Dispose();
            Assert.Same(Brushes.Black, BackgroundImage.CreateBrush(new ThemeSettings { BackgroundImage = video }, Brushes.Black, 400, 200));
        });

        var ok = JsonSerializer.Deserialize<LayoutDocument>($$"""{ "theme": { "backgroundImage": {{JsonSerializer.Serialize(video)}} } }""", JsonDefaults.Options)!;
        Assert.Empty(LayoutValidator.Validate(ok));
        var text = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(text, "x");
        var bad = JsonSerializer.Deserialize<LayoutDocument>($$"""{ "theme": { "backgroundImage": {{JsonSerializer.Serialize(text)}} } }""", JsonDefaults.Options)!;
        Assert.Single(LayoutValidator.Validate(bad));
    }

    /// <summary>A GIF with one solid frame per colour.</summary>
    private string Gif(IReadOnlyList<Color> colors, int width, int height)
    {
        var encoder = new GifBitmapEncoder();
        foreach (var color in colors)
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (color.B, color.G, color.R, 255);
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        }
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.gif");
        using (var file = File.Create(path))
            encoder.Save(file);
        return path;
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }
}

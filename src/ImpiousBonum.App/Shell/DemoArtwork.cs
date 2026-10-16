using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.Core.Games;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// The album cover for the demo readings (<see cref="Core.Demo.DemoReadings"/>): a retro sunset over a skyline, drawn in
/// code, so screenshots don't show anyone's real album art.
/// </summary>
public static class DemoArtwork
{
    public static byte[] Png(int size = 400)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var s = (double)size;
            dc.DrawRectangle(new LinearGradientBrush(
                [new GradientStop(Color.FromRgb(0x1B, 0x10, 0x3D), 0), new GradientStop(Color.FromRgb(0x7B, 0x2F, 0xBE), 0.55), new GradientStop(Color.FromRgb(0xFF, 0x6B, 0x35), 1)],
                90), null, new Rect(0, 0, s, s));

            // The sun, sliced by the horizon lines of an 80s cover.
            var sun = new EllipseGeometry(new Point(s / 2, s * 0.62), s * 0.27, s * 0.27);
            var slices = new GeometryGroup();
            for (var i = 0; i < 6; i++)
                slices.Children.Add(new RectangleGeometry(new Rect(0, s * (0.6 + i * 0.045), s, s * 0.008 * (i + 1))));
            var sliced = Geometry.Combine(sun, slices, GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(0xFF, 0xE2, 0x6B), Color.FromRgb(0xFF, 0x4D, 0x7A), 90), null, sliced);

            // A skyline across the bottom.
            var skyline = new StreamGeometry();
            using (var g = skyline.Open())
            {
                double[] heights = [0.12, 0.2, 0.15, 0.27, 0.18, 0.1, 0.22, 0.31, 0.16, 0.24, 0.13, 0.19];
                g.BeginFigure(new Point(0, s), true, true);
                var x = 0.0;
                var width = s / heights.Length;
                foreach (var h in heights)
                {
                    g.LineTo(new Point(x, s * (1 - h)), true, false);
                    g.LineTo(new Point(x + width, s * (1 - h)), true, false);
                    x += width;
                }
                g.LineTo(new Point(s, s), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x29)), null, skyline);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The photo for a photo frame with no folder in the demo readings: a lake at dusk below the hills, drawn in code,
    /// so screenshots don't show anyone's own pictures.
    /// </summary>
    public static BitmapSource Landscape(int width = 1200, int height = 800)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var (w, h) = ((double)width, (double)height);
            var horizon = h * 0.6;
            dc.DrawRectangle(new LinearGradientBrush(
                [
                    new GradientStop(Color.FromRgb(0x16, 0x20, 0x4A), 0), new GradientStop(Color.FromRgb(0x3B, 0x31, 0x70), 0.35),
                    new GradientStop(Color.FromRgb(0x9A, 0x4A, 0x7E), 0.65), new GradientStop(Color.FromRgb(0xF0, 0x8A, 0x63), 0.88),
                    new GradientStop(Color.FromRgb(0xFF, 0xC2, 0x7A), 1),
                ], 90), null, new Rect(0, 0, w, horizon));

            // The first stars, always in the same places.
            var random = new Random(7);
            var star = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            for (var i = 0; i < 40; i++)
                dc.DrawEllipse(star, null, new Point(random.NextDouble() * w, random.NextDouble() * horizon * 0.4), h * 0.0025, h * 0.0025);

            // The sun setting behind the hills, with its glow.
            var sun = new Point(w * 0.66, horizon - h * 0.1);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0x90, 0xFF, 0xD5, 0x9A), Color.FromArgb(0, 0xFF, 0xB0, 0x70)), null, sun, w * 0.3, w * 0.3);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xE7, 0xB5)), null, sun, h * 0.045, h * 0.045);

            // The lake: the sky's colours again, deepening towards the shore.
            dc.DrawRectangle(new LinearGradientBrush(
                [
                    new GradientStop(Color.FromRgb(0xE0, 0x8C, 0x6C), 0), new GradientStop(Color.FromRgb(0x7A, 0x46, 0x78), 0.3),
                    new GradientStop(Color.FromRgb(0x24, 0x22, 0x4E), 1),
                ], 90), null, new Rect(0, horizon, w, h - horizon));

            // Hazy far hills and darker near ones, each reflected in the lake.
            var far = Ridge(w, horizon, [.. new[] { 0.1, 0.17, 0.12, 0.21, 0.13, 0.05, 0.14, 0.11 }.Select(r => r * h)]);
            var near = Ridge(w, horizon, [.. new[] { 0.07, 0.03, 0.1, 0.05, 0.02, 0.04, 0.08, 0.05 }.Select(r => r * h)]);
            var farColour = new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x7E));
            var nearColour = new SolidColorBrush(Color.FromRgb(0x3A, 0x28, 0x56));
            dc.DrawGeometry(farColour, null, far);
            dc.DrawGeometry(nearColour, null, near);
            dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * horizon));
            dc.PushOpacity(0.45);
            dc.DrawGeometry(farColour, null, far);
            dc.DrawGeometry(nearColour, null, near);
            dc.Pop();
            dc.Pop();

            // The sun's path across the water, narrowing towards the shore.
            for (var i = 0; i < 9; i++)
            {
                var length = w * (0.14 - i * 0.012);
                var glint = new SolidColorBrush(Color.FromArgb((byte)(0xC0 - i * 0x12), 0xFF, 0xE2, 0xA8));
                dc.DrawRectangle(glint, null, new Rect(sun.X - length / 2, horizon + h * (0.035 + i * 0.03), length, h * 0.006));
            }

            // A shore in the corner with a few pines on it.
            var land = new SolidColorBrush(Color.FromRgb(0x14, 0x0F, 0x26));
            var shore = new StreamGeometry();
            using (var g = shore.Open())
            {
                g.BeginFigure(new Point(0, h), true, true);
                g.LineTo(new Point(0, h * 0.84), false, false);
                g.BezierTo(new Point(w * 0.14, h * 0.82), new Point(w * 0.3, h * 0.9), new Point(w * 0.42, h), false, true);
            }
            dc.DrawGeometry(land, null, shore);
            foreach (var (x, size) in new[] { (0.04, 0.2), (0.1, 0.27), (0.16, 0.17), (0.22, 0.12) })
                Pine(dc, land, new Point(w * x, h * (0.85 + x * 0.12)), h * size);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A line of hills along <paramref name="baseline"/>, rising <paramref name="heights"/> above it, evenly spread and smoothly curved.</summary>
    private static Geometry Ridge(double width, double baseline, IReadOnlyList<double> heights)
    {
        var ridge = new StreamGeometry();
        using (var g = ridge.Open())
        {
            var step = width / (heights.Count - 1);
            Point At(int i) => new(i * step, baseline - heights[i]);
            g.BeginFigure(new Point(0, baseline), true, true);
            g.LineTo(At(0), false, false);
            for (var i = 1; i < heights.Count; i++)
            {
                // Curving through each point to the middle of the next stretch keeps the hills smooth.
                var (from, to) = (At(i - 1), At(i));
                g.QuadraticBezierTo(from, new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), false, true);
            }
            g.LineTo(At(heights.Count - 1), false, true);
            g.LineTo(new Point(width, baseline), false, false);
        }
        ridge.Freeze();
        return ridge;
    }

    /// <summary>A pine tree <paramref name="height"/> tall standing at <paramref name="foot"/>: three tiers and a trunk.</summary>
    private static void Pine(DrawingContext dc, Brush brush, Point foot, double height)
    {
        var tree = new StreamGeometry();
        using (var g = tree.Open())
        {
            for (var tier = 0; tier < 3; tier++)
            {
                var bottom = foot.Y - height * (0.12 + tier * 0.26);
                var top = bottom - height * 0.44;
                var half = height * (0.22 - tier * 0.05);
                g.BeginFigure(new Point(foot.X - half, bottom), true, true);
                g.LineTo(new Point(foot.X, top), false, false);
                g.LineTo(new Point(foot.X + half, bottom), false, false);
            }
            g.BeginFigure(new Point(foot.X - height * 0.025, foot.Y), true, true);
            g.LineTo(new Point(foot.X - height * 0.025, foot.Y - height * 0.14), false, false);
            g.LineTo(new Point(foot.X + height * 0.025, foot.Y - height * 0.14), false, false);
            g.LineTo(new Point(foot.X + height * 0.025, foot.Y), false, false);
        }
        dc.DrawGeometry(brush, null, tree);
    }

    /// <summary>
    /// The pictures of the demo readings' game, Starfall Odyssey: a ringed planet under a shower of falling stars, as Steam
    /// would have them. The cover and header have its title on; the hero, like Steam's, doesn't.
    /// </summary>
    public static GameArt Game() => new(
        Jpeg(400, 600, (dc, w, h) => Starfall(dc, w, h, new Point(w * 0.5, h * 0.37), w * 0.27, new Rect(w * 0.06, h * 0.7, w * 0.88, h * 0.22), TextAlignment.Center)),
        Jpeg(920, 430, (dc, w, h) => Starfall(dc, w, h, new Point(w * 0.76, h * 0.5), h * 0.3, new Rect(w * 0.05, h * 0.3, w * 0.5, h * 0.4), TextAlignment.Left)),
        Jpeg(1920, 620, (dc, w, h) => Starfall(dc, w, h, new Point(w * 0.7, h * 0.48), h * 0.3, null, TextAlignment.Left)),
        null);

    private static void Starfall(DrawingContext dc, double w, double h, Point planet, double radius, Rect? title, TextAlignment alignment)
    {
        // Deep space, warming to a nebula's glow at the bottom.
        dc.DrawRectangle(new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0x06, 0x0A, 0x24), 0), new GradientStop(Color.FromRgb(0x18, 0x1A, 0x58), 0.45),
                new GradientStop(Color.FromRgb(0x55, 0x27, 0x80), 0.8), new GradientStop(Color.FromRgb(0xD8, 0x62, 0x7C), 1),
            ], 90), null, new Rect(0, 0, w, h));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0x60, 0x3F, 0xC8, 0xE0), Color.FromArgb(0, 0x3F, 0xC8, 0xE0)), null,
            new Point(w * 0.22, h * 0.3), w * 0.45, h * 0.45);

        // The stars, always in the same places.
        var random = new Random(23);
        var unit = Math.Min(w, h);
        var star = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < w * h / 2600; i++)
            dc.DrawEllipse(star, null, new Point(random.NextDouble() * w, random.NextDouble() * h), unit * (0.002 + random.NextDouble() * 0.004), unit * (0.002 + random.NextDouble() * 0.004));

        // The starfall: streaks sloping down to the left, bright at the head and fading along the tail.
        var slope = new Vector(Math.Cos(Math.PI / 7), -Math.Sin(Math.PI / 7));
        foreach (var (x, y, length) in new[] { (0.86, 0.12, 0.42), (0.58, 0.2, 0.26), (0.97, 0.42, 0.34), (0.34, 0.08, 0.18), (0.42, 0.56, 0.2), (0.16, 0.38, 0.14) })
        {
            var head = new Point(w * x, h * y);
            var tail = head + slope * Math.Max(w, h) * length;
            var fade = new LinearGradientBrush(Color.FromArgb(0xF0, 0xFF, 0xF4, 0xD8), Color.FromArgb(0, 0xFF, 0xB8, 0xE8), 0)
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = head, EndPoint = tail,
            };
            dc.DrawLine(new Pen(fade, unit * 0.008) { StartLineCap = PenLineCap.Round }, head, tail);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xE8, 0xC0)), null, head, unit * 0.022, unit * 0.022);
        }

        // The planet, lit from the upper left, with its rings: the far side behind it, the near side across it.
        var tilt = new RotateTransform(-16, planet.X, planet.Y);
        var ring = new Pen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xD6, 0x8A), Color.FromRgb(0xE8, 0x6A, 0xB0), 0), radius * 0.09);
        var outer = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xE2, 0xB0)), radius * 0.03);
        void Rings()
        {
            dc.DrawEllipse(null, ring, planet, radius * 1.7, radius * 0.42);
            dc.DrawEllipse(null, outer, planet, radius * 2.0, radius * 0.52);
        }
        dc.PushTransform(tilt);
        Rings();
        dc.Pop();
        dc.DrawEllipse(new RadialGradientBrush(
            [new GradientStop(Color.FromRgb(0x9C, 0xF6, 0xE8), 0), new GradientStop(Color.FromRgb(0x2C, 0x8E, 0xC9), 0.55), new GradientStop(Color.FromRgb(0x1B, 0x1D, 0x5C), 1)])
        {
            GradientOrigin = new Point(0.3, 0.28), Center = new Point(0.36, 0.34), RadiusX = 0.75, RadiusY = 0.75,
        }, null, planet, radius, radius);
        dc.PushTransform(tilt);
        dc.PushClip(new RectangleGeometry(new Rect(planet.X - radius * 2.2, planet.Y, radius * 4.4, radius)));
        Rings();
        dc.Pop();
        dc.Pop();

        if (title is not { } area)
            return;

        // The title: STARFALL large, ODYSSEY spaced out under it, with a shadow so it stands off the stars.
        var name = Title("STARFALL", area.Height * 0.62, area.Width, alignment, FontWeights.Black,
            new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xC9, 0x7A), 90));
        var subtitle = Title(string.Join(' ', "ODYSSEY".ToCharArray()), area.Height * 0.26, area.Width, alignment, FontWeights.SemiBold,
            new SolidColorBrush(Color.FromRgb(0xB8, 0xF0, 0xFF)));
        var top = area.Top + (area.Height - name.Height - subtitle.Height) / 2;
        var shadow = new SolidColorBrush(Color.FromArgb(0xA0, 0x08, 0x06, 0x20));
        foreach (var (text, y) in new[] { (name, top), (subtitle, top + name.Height) })
        {
            dc.DrawGeometry(shadow, null, text.BuildGeometry(new Point(area.Left + unit * 0.006, y + unit * 0.008)));
            dc.DrawText(text, new Point(area.Left, y));
        }
    }

    /// <summary>A line of the title at <paramref name="size"/>, made smaller if it's wider than <paramref name="width"/>.</summary>
    private static FormattedText Title(string text, double size, double width, TextAlignment alignment, FontWeight weight, Brush brush)
    {
        FormattedText At(double em) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), em, brush, 1);
        var natural = At(size).WidthIncludingTrailingWhitespace;
        var line = At(natural > width ? size * width / natural * 0.98 : size);
        line.MaxTextWidth = width;
        line.TextAlignment = alignment;
        return line;
    }

    private static byte[] Jpeg(int width, int height, Action<DrawingContext, double, double> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            draw(dc, width, height);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
}

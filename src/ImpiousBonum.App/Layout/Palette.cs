using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImpiousBonum.App.Layout;

/// <summary>One of a picture's main colours, and how much of the picture it covers, from 0 to 1.</summary>
public sealed record Swatch(Color Colour, double Share);

/// <summary>
/// A picture's main colours, for making a theme from it (see <see cref="PictureTheme"/>). The picture is shrunk to a
/// hundred or so pixels across first, which is plenty to tell its colours and quick even for a 4K wallpaper.
/// </summary>
public static class Palette
{
    /// <summary>How many pixels across (or down) the picture is shrunk to.</summary>
    public const int SampleSize = 100;

    /// <summary>How many colours a picture is boiled down to, before ones that look the same are put together.</summary>
    public const int MaxColours = 8;

    /// <summary>A picture file's main colours, or null if it can't be read. Animated GIFs give their first frame's.</summary>
    public static IReadOnlyList<Swatch>? FromFile(string path)
    {
        try
        {
            // Read from the stream rather than by name, so a picture without an extension (Windows' copy of a
            // slideshow's wallpaper) works too.
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            return FromPicture(frame);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>A picture's main colours, largest share first. Transparent parts don't count.</summary>
    public static IReadOnlyList<Swatch> FromPicture(BitmapSource picture)
    {
        var scale = Math.Min(1, (double)SampleSize / Math.Max(picture.PixelWidth, picture.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(picture, new ScaleTransform(scale, scale)) : picture;
        small = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

        var stride = small.PixelWidth * 4;
        var bytes = new byte[stride * small.PixelHeight];
        small.CopyPixels(bytes, stride, 0);
        var pixels = new List<Color>(bytes.Length / 4);
        for (var i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i + 3] >= 128)
                pixels.Add(Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i]));
        }
        return FromColours(pixels);
    }

    /// <summary>
    /// The main colours of some pixels, largest share first. Median cut finds a starting set, splitting the group of
    /// colours that varies most down the middle until there are <see cref="MaxColours"/>; a few rounds of k-means then
    /// settle each on the middle of the pixels nearest it. Colours that look the same are put together, so a picture of
    /// two colours gives two.
    /// </summary>
    public static IReadOnlyList<Swatch> FromColours(IReadOnlyList<Color> pixels)
    {
        if (pixels.Count == 0)
            return [];
        var points = pixels.Select(Oklab.From).ToArray();

        // Median cut.
        var groups = new List<Oklab[]> { points };
        while (groups.Count < MaxColours)
        {
            var widest = groups.MaxBy(Spread)!;
            if (Spread(widest) < 1e-6)
                break;
            var axis = WidestAxis(widest);
            var sorted = widest.OrderBy(p => Axis(p, axis)).ToArray();
            groups.Remove(widest);
            groups.Add(sorted[..(sorted.Length / 2)]);
            groups.Add(sorted[(sorted.Length / 2)..]);
        }
        var centres = groups.Select(Mean).ToArray();

        // K-means.
        var nearest = new int[points.Length];
        for (var round = 0; round < 10; round++)
        {
            var moved = false;
            for (var i = 0; i < points.Length; i++)
            {
                var closest = Nearest(points[i], centres);
                moved |= closest != nearest[i];
                nearest[i] = closest;
            }
            for (var c = 0; c < centres.Length; c++)
            {
                var members = points.Where((_, i) => nearest[i] == c).ToArray();
                if (members.Length > 0)
                    centres[c] = Mean(members);
            }
            if (!moved && round > 0)
                break;
        }

        // Each colour's share, with ones too close to tell apart put together.
        var swatches = new List<(Oklab Colour, double Share)>();
        for (var c = 0; c < centres.Length; c++)
        {
            var share = (double)nearest.Count(n => n == c) / points.Length;
            if (share == 0)
                continue;
            var same = swatches.FindIndex(s => s.Colour.DistanceTo(centres[c]) < 0.02);
            if (same < 0)
            {
                swatches.Add((centres[c], share));
                continue;
            }
            var (colour, total) = swatches[same];
            var weight = share / (total + share);
            swatches[same] = (new Oklab(
                colour.L + (centres[c].L - colour.L) * weight,
                colour.A + (centres[c].A - colour.A) * weight,
                colour.B + (centres[c].B - colour.B) * weight), total + share);
        }
        return [.. swatches.OrderByDescending(s => s.Share).Select(s => new Swatch(s.Colour.ToColor(), s.Share))];
    }

    private static double Axis(Oklab point, int axis) => axis switch { 0 => point.L, 1 => point.A, _ => point.B };

    /// <summary>How much a group's colours differ from its middle, all told: the group most worth splitting next.</summary>
    private static double Spread(Oklab[] group)
    {
        if (group.Length < 2)
            return 0;
        var mean = Mean(group);
        return group.Sum(p => (p.L - mean.L) * (p.L - mean.L) + (p.A - mean.A) * (p.A - mean.A) + (p.B - mean.B) * (p.B - mean.B));
    }

    private static int WidestAxis(Oklab[] group)
    {
        var ranges = Enumerable.Range(0, 3).Select(axis => group.Max(p => Axis(p, axis)) - group.Min(p => Axis(p, axis))).ToArray();
        return Array.IndexOf(ranges, ranges.Max());
    }

    private static Oklab Mean(Oklab[] group) =>
        new(group.Average(p => p.L), group.Average(p => p.A), group.Average(p => p.B));

    private static int Nearest(Oklab point, Oklab[] centres)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var c = 0; c < centres.Length; c++)
        {
            var distance = point.DistanceTo(centres[c]);
            if (distance < bestDistance)
                (best, bestDistance) = (c, distance);
        }
        return best;
    }
}

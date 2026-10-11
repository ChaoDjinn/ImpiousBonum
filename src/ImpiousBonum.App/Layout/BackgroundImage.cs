using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// Builds the canvas background: the theme's colour with an optional image drawn over it.
/// Images are decoded no larger than the canvas needs and cached, so a 4K wallpaper on a small strip stays cheap.
/// </summary>
public static class BackgroundImage
{
    public static readonly IReadOnlyList<string> Fits = ["fill", "fit", "stretch", "center", "tile"];

    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    // The canvas background plus a few image widgets, each decoded at the size it's shown.
    private const int CacheSize = 8;

    private static readonly List<(string Key, BitmapSource Bitmap)> Cache = [];

    /// <summary>A brush for a canvas of the given size. Falls back to the plain colour if there's no usable image.</summary>
    public static Brush CreateBrush(ThemeSettings theme, Brush color, double width, double height) =>
        CreateBrush(theme, color, width, height, animate: false, out _);

    /// <summary>
    /// As above; with <paramref name="animate"/>, an animated GIF or a video moves, kept going by
    /// <paramref name="player"/> (which the caller plays, pauses and disposes). Without it, a GIF shows its first frame
    /// and a video just the colour.
    /// </summary>
    public static Brush CreateBrush(ThemeSettings theme, Brush color, double width, double height, bool animate, out BackgroundPlayer? player)
    {
        player = null;
        if (string.IsNullOrWhiteSpace(theme.BackgroundImage) || width <= 0 || height <= 0)
            return color;
        var opacity = Math.Clamp(theme.BackgroundOpacity, 0, 1);
        if (opacity == 0)
            return color;
        var fit = NormalizeFit(theme.BackgroundFit);
        var path = theme.BackgroundImage;

        if (animate && AnimatedBackground.IsVideo(path) && File.Exists(path))
        {
            var drawing = new VideoDrawing();
            var video = new DrawingBrush(drawing) { Stretch = StretchFor(fit), AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            player = new BackgroundPlayer(drawing, path);
            return Layer(color, video, opacity, width, height, freeze: false);
        }
        if (animate && AnimatedBackground.IsGif(path) && AnimatedBackground.LoadGif(path, fit, width, height) is { } gif)
        {
            var frames = new ImageBrush(gif.Frames[0]);
            Place(frames, fit, gif.Frames[0], width, height);
            player = new BackgroundPlayer(frames, gif);
            return Layer(color, frames, opacity, width, height, freeze: false);
        }

        var bitmap = Load(path, fit, width, height);
        if (bitmap is null)
            return color;
        var image = new ImageBrush(bitmap);
        Place(image, fit, bitmap, width, height);
        return Layer(color, image, opacity, width, height, freeze: true);
    }

    private static Stretch StretchFor(string fit) => fit switch
    {
        "fill" => Stretch.UniformToFill,
        "fit" => Stretch.Uniform,
        _ => Stretch.Fill,
    };

    private static void Place(ImageBrush image, string fit, BitmapSource bitmap, double width, double height)
    {
        image.Stretch = StretchFor(fit);
        image.AlignmentX = AlignmentX.Center;
        image.AlignmentY = AlignmentY.Center;
        if (fit is "center" or "tile")
        {
            // One image pixel per canvas unit, whatever DPI the file claims.
            image.ViewportUnits = BrushMappingMode.Absolute;
            image.Viewport = fit == "tile"
                ? new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight)
                : new Rect((width - bitmap.PixelWidth) / 2, (height - bitmap.PixelHeight) / 2, bitmap.PixelWidth, bitmap.PixelHeight);
            image.TileMode = fit == "tile" ? TileMode.Tile : TileMode.None;
        }
    }

    /// <summary>
    /// Colour first, then the picture over it, as one brush so the canvas needs no extra elements. Frozen for a still
    /// image; a moving one has to stay changeable.
    /// </summary>
    private static Brush Layer(Brush color, Brush picture, double opacity, double width, double height, bool freeze)
    {
        var bounds = new RectangleGeometry(new Rect(0, 0, width, height));
        var layers = new DrawingGroup();
        layers.Children.Add(new GeometryDrawing(color, null, bounds));
        var group = new DrawingGroup { Opacity = opacity, ClipGeometry = bounds };
        group.Children.Add(new GeometryDrawing(picture, null, bounds));
        layers.Children.Add(group);

        var brush = new DrawingBrush(layers) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds.Rect };
        if (freeze)
            brush.Freeze();
        return brush;
    }

    /// <summary>What a theme's background can be: a picture, an animated GIF or a video.</summary>
    public static readonly IReadOnlyList<string> BackgroundExtensions = [.. Extensions, .. AnimatedBackground.VideoExtensions];

    public static bool IsSupportedBackground(string path) => BackgroundExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string NormalizeFit(string? fit) =>
        fit is not null && Fits.Contains(fit.ToLowerInvariant()) ? fit.ToLowerInvariant() : "fill";

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The decoded image, no bigger than <paramref name="width"/> × <paramref name="height"/> needs, or null if the file is missing or can't be read.</summary>
    public static BitmapSource? Load(string path, string fit, double width, double height)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || !IsSupported(path))
                return null;

            var key = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}|{fit}|{width}x{height}";
            lock (Cache)
            {
                var hit = Cache.FindIndex(entry => entry.Key == key);
                if (hit >= 0)
                    return Cache[hit].Bitmap;
            }

            var bitmap = Decode(file.FullName, fit, width, height);
            lock (Cache)
            {
                Cache.Insert(0, (key, bitmap));
                if (Cache.Count > CacheSize)
                    Cache.RemoveAt(Cache.Count - 1);
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or ExternalException)
        {
            return null;
        }
    }

    private static BitmapSource Decode(string path, string fit, double width, double height)
    {
        // Without caching, WIC pulls pixels through the scaler and crop on demand, so the full-size image is never
        // held in memory; only the result is, at the size the canvas shows.
        using var stream = File.OpenRead(path);
        BitmapSource source = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];

        var (scaleX, scaleY) = DecodeScale(fit, source.PixelWidth, source.PixelHeight, width, height);
        if (scaleX < 1 || scaleY < 1)
            source = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
        if (VisibleCrop(fit, source.PixelWidth, source.PixelHeight, width, height) is { } crop)
            source = new CroppedBitmap(source, crop);

        var bitmap = new CachedBitmap(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// How much to shrink the image before use (1 = keep its size). Never upscales; the canvas is scaled to the
    /// window afterwards, so an image at canvas size looks the same as a bigger one.
    /// </summary>
    public static (double X, double Y) DecodeScale(string fit, int sourceWidth, int sourceHeight, double width, double height)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return (1, 1);

        switch (NormalizeFit(fit))
        {
            case "stretch":
                return (Math.Min(1, width / sourceWidth), Math.Min(1, height / sourceHeight));
            case "fit":
                var fitScale = Math.Min(1, Math.Min(width / sourceWidth, height / sourceHeight));
                return (fitScale, fitScale);
            case "fill":
                var fillScale = Math.Min(1, Math.Max(width / sourceWidth, height / sourceHeight));
                return (fillScale, fillScale);
            default:
                // center and tile show the image at its own pixel size.
                return (1, 1);
        }
    }

    /// <summary>For <c>fill</c> and <c>center</c>, the middle part of an image bigger than the canvas, which is all that shows; null to keep it all.</summary>
    public static Int32Rect? VisibleCrop(string fit, int imageWidth, int imageHeight, double width, double height)
    {
        if (NormalizeFit(fit) is not ("fill" or "center") || (imageWidth <= width && imageHeight <= height))
            return null;
        var w = (int)Math.Min(imageWidth, Math.Ceiling(width));
        var h = (int)Math.Min(imageHeight, Math.Ceiling(height));
        return new Int32Rect((imageWidth - w) / 2, (imageHeight - h) / 2, w, h);
    }
}

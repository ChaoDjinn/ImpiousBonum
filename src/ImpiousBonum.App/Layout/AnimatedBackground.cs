using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImpiousBonum.App.Layout;

/// <summary>An animated GIF's frames, composed and scaled for the canvas, and how long each shows.</summary>
public sealed record GifFrames(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays)
{
    public long Bytes => Frames.Sum(f => (long)f.PixelWidth * f.PixelHeight * 4);
}

/// <summary>
/// Moving backgrounds: animated GIFs and videos. GIF frames are composed once, at no more than the size the canvas
/// shows, and shared by every dashboard showing them; when they'd take more than <see cref="MaxBytes"/> they're made
/// smaller, then every other frame is dropped, so a long GIF can't eat the PC's memory. Videos play muted and on a
/// loop through Windows' media player.
/// </summary>
public static class AnimatedBackground
{
    public static readonly IReadOnlyList<string> VideoExtensions = [".mp4", ".m4v", ".wmv"];

    /// <summary>The most a GIF's frames may take, all together.</summary>
    public const long MaxBytes = 48L * 1024 * 1024;

    /// <summary>GIFs that ask for faster frames are slowed to this, which looks the same on a status display and costs less.</summary>
    public static readonly TimeSpan ShortestDelay = TimeSpan.FromMilliseconds(50);

    private static readonly List<(string Key, GifFrames Frames)> Cache = [];

    public static bool IsVideo(string? path) => path is not null && VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsGif(string? path) => path is not null && Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>A GIF's frames for a canvas of this size, or null if it isn't animated or can't be read.</summary>
    public static GifFrames? LoadGif(string path, string fit, double width, double height)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                return null;
            var key = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}|{fit}|{width}x{height}";
            lock (Cache)
            {
                if (Cache.FirstOrDefault(c => c.Key == key) is { Frames: { } hit })
                    return hit;
            }

            using var stream = File.OpenRead(file.FullName);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames.Count < 2)
                return null;
            var frames = Compose(decoder, fit, width, height, MaxBytes);
            lock (Cache)
            {
                Cache.Insert(0, (key, frames));
                // Two: the dashboard's and the editor's, when they differ.
                if (Cache.Count > 2)
                    Cache.RemoveAt(Cache.Count - 1);
            }
            return frames;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException or ExternalException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// How to fit <paramref name="frames"/> frames of <paramref name="width"/> × <paramref name="height"/> into
    /// <paramref name="maxBytes"/>: a scale (down to half size), then keeping every <c>Step</c>th frame if that's not enough.
    /// </summary>
    internal static (double Scale, int Step) Budget(int frames, int width, int height, long maxBytes)
    {
        var total = (double)frames * width * height * 4;
        if (total <= maxBytes)
            return (1, 1);
        var scale = Math.Max(0.5, Math.Sqrt(maxBytes / total));
        var scaled = total * scale * scale;
        // Less a hair, so a size exactly at the limit (after rounding) doesn't drop frames.
        return (scale, Math.Max(1, (int)Math.Ceiling(scaled / maxBytes - 1e-9)));
    }

    /// <summary>The GIF's frames as they appear (each drawn over the last, as the GIF says), scaled and cropped like a still image.</summary>
    internal static GifFrames Compose(BitmapDecoder decoder, string fit, double width, double height, long maxBytes)
    {
        var first = decoder.Frames[0];
        var screenWidth = Query<ushort>(decoder.Metadata, "/logscrdesc/Width") is > 0 and var w ? w : first.PixelWidth;
        var screenHeight = Query<ushort>(decoder.Metadata, "/logscrdesc/Height") is > 0 and var h ? h : first.PixelHeight;

        // The size each frame is kept at: as a still image would be, then smaller still if they'd take too much memory.
        var (scaleX, scaleY) = BackgroundImage.DecodeScale(fit, screenWidth, screenHeight, width, height);
        var shownWidth = Math.Max(1, (int)Math.Round(screenWidth * scaleX));
        var shownHeight = Math.Max(1, (int)Math.Round(screenHeight * scaleY));
        var crop = BackgroundImage.VisibleCrop(fit, shownWidth, shownHeight, width, height);
        var (budget, step) = Budget(decoder.Frames.Count, crop?.Width ?? shownWidth, crop?.Height ?? shownHeight, maxBytes);

        var stride = screenWidth * 4;
        var canvas = new byte[stride * screenHeight];
        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        var pending = TimeSpan.Zero;

        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            var frame = decoder.Frames[i];
            var metadata = frame.Metadata as BitmapMetadata;
            var left = Query<ushort>(metadata, "/imgdesc/Left");
            var top = Query<ushort>(metadata, "/imgdesc/Top");
            var disposal = Query<byte>(metadata, "/grctlext/Disposal");
            var delay = TimeSpan.FromMilliseconds(Query<ushort>(metadata, "/grctlext/Delay") * 10.0);
            // Browsers treat a delay this short as "no delay given".
            if (delay < TimeSpan.FromMilliseconds(20))
                delay = TimeSpan.FromMilliseconds(100);
            delay = delay < ShortestDelay ? ShortestDelay : delay;

            var saved = disposal == 3 ? (byte[])canvas.Clone() : null;
            var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var frameStride = pixels.PixelWidth * 4;
            var buffer = new byte[frameStride * pixels.PixelHeight];
            pixels.CopyPixels(buffer, frameStride, 0);
            Draw(canvas, screenWidth, screenHeight, buffer, pixels.PixelWidth, pixels.PixelHeight, left, top);

            pending += delay;
            if (i % step == 0)
            {
                frames.Add(Snapshot(canvas, screenWidth, screenHeight, scaleX * budget, scaleY * budget, fit, width * budget, height * budget));
                delays.Add(pending);
                pending = TimeSpan.Zero;
            }
            else if (delays.Count > 0)
            {
                // A dropped frame's time goes to the one before it, so the loop keeps its length.
                delays[^1] += pending;
                pending = TimeSpan.Zero;
            }

            if (disposal == 2)
                Clear(canvas, screenWidth, screenHeight, left, top, pixels.PixelWidth, pixels.PixelHeight);
            else if (saved is not null)
                canvas = saved;
        }
        return new GifFrames(frames, delays);
    }

    /// <summary>Draws a frame's opaque pixels onto the canvas (GIF transparency is all or nothing).</summary>
    private static void Draw(byte[] canvas, int canvasWidth, int canvasHeight, byte[] frame, int frameWidth, int frameHeight, int left, int top)
    {
        for (var y = 0; y < frameHeight && top + y < canvasHeight; y++)
        {
            for (var x = 0; x < frameWidth && left + x < canvasWidth; x++)
            {
                var from = (y * frameWidth + x) * 4;
                if (frame[from + 3] == 0)
                    continue;
                Buffer.BlockCopy(frame, from, canvas, ((top + y) * canvasWidth + left + x) * 4, 4);
            }
        }
    }

    private static void Clear(byte[] canvas, int canvasWidth, int canvasHeight, int left, int top, int width, int height)
    {
        for (var y = top; y < Math.Min(canvasHeight, top + height); y++)
            Array.Clear(canvas, (y * canvasWidth + left) * 4, Math.Max(0, Math.Min(width, canvasWidth - left)) * 4);
    }

    private static BitmapSource Snapshot(byte[] canvas, int width, int height, double scaleX, double scaleY, string fit, double canvasWidth, double canvasHeight)
    {
        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, width * 4);
        if (scaleX < 1 || scaleY < 1)
            source = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
        if (BackgroundImage.VisibleCrop(fit, source.PixelWidth, source.PixelHeight, canvasWidth, canvasHeight) is { } crop)
            source = new CroppedBitmap(source, crop);
        var bitmap = new CachedBitmap(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    private static T Query<T>(BitmapMetadata? metadata, string query) where T : struct
    {
        try
        {
            return metadata?.GetQuery(query) is T value ? value : default;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or COMException)
        {
            return default;
        }
    }
}

/// <summary>
/// Keeps a moving background moving: steps through GIF frames on a timer, or loops a muted video. Pauses while the
/// dashboard isn't showing, so nothing is spent on a hidden window.
/// </summary>
public sealed class BackgroundPlayer : IDisposable
{
    private readonly DispatcherTimer? _timer;
    private readonly MediaPlayer? _video;
    private bool _playing;

    /// <summary>Steps <paramref name="brush"/> through the frames.</summary>
    public BackgroundPlayer(ImageBrush brush, GifFrames gif)
    {
        var index = 0;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = gif.Delays[0] };
        _timer.Tick += (_, _) =>
        {
            index = (index + 1) % gif.Frames.Count;
            brush.ImageSource = gif.Frames[index];
            _timer.Interval = gif.Delays[index];
        };
    }

    /// <summary>Plays <paramref name="path"/> muted and on a loop into <paramref name="drawing"/>.</summary>
    public BackgroundPlayer(VideoDrawing drawing, string path)
    {
        _video = new MediaPlayer { IsMuted = true, Volume = 0 };
        _video.MediaOpened += (_, _) =>
        {
            if (_video.NaturalVideoWidth > 0)
                drawing.Rect = new Rect(0, 0, _video.NaturalVideoWidth, _video.NaturalVideoHeight);
        };
        _video.MediaEnded += (_, _) =>
        {
            _video.Position = TimeSpan.Zero;
            _video.Play();
        };
        drawing.Player = _video;
        _video.Open(new Uri(Path.GetFullPath(path)));
    }

    public void Play()
    {
        if (_playing)
            return;
        _playing = true;
        _timer?.Start();
        _video?.Play();
    }

    public void Pause()
    {
        if (!_playing)
            return;
        _playing = false;
        _timer?.Stop();
        _video?.Pause();
    }

    public void Dispose()
    {
        Pause();
        _video?.Close();
    }
}

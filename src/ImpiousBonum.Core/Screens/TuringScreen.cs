namespace ImpiousBonum.Core.Screens;

/// <summary>A rectangle of the screen, in its own pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>Which way up the screen is mounted.</summary>
public enum ScreenOrientation
{
    Portrait = 0,
    ReversePortrait = 1,
    Landscape = 2,
    ReverseLandscape = 3,
}

/// <summary>
/// The "revision A" protocol of the small USB screens sold as Turing Smart Screen 3.5" and many unbranded clones
/// (USB serial, 320 × 480): 6-byte commands with packed coordinates, and pictures as raw RGB565. As documented by
/// the open-source turing-smart-screen-python project. Builds the bytes only; <see cref="UsbScreen"/> sends them.
/// </summary>
public static class TuringProtocol
{
    public const int NativeWidth = 320;
    public const int NativeHeight = 480;

    public const byte Hello = 69;
    public const byte Clear = 102;
    public const byte ScreenOff = 108;
    public const byte ScreenOn = 109;
    public const byte SetBrightness = 110;
    public const byte SetOrientation = 121;
    public const byte DisplayBitmap = 197;

    /// <summary>A command with a rectangle (or just x, for brightness), coordinates packed into 5 bytes.</summary>
    public static byte[] Command(byte command, int x = 0, int y = 0, int ex = 0, int ey = 0) =>
    [
        (byte)(x >> 2),
        (byte)(((x & 3) << 6) + (y >> 4)),
        (byte)(((y & 15) << 4) + (ex >> 6)),
        (byte)(((ex & 63) << 2) + (ey >> 8)),
        (byte)(ey & 255),
        command,
    ];

    /// <summary>Brightness as a percentage. The screen takes it the other way round: 0 is brightest.</summary>
    public static byte[] Brightness(int percent) =>
        Command(SetBrightness, 255 - (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * 255));

    /// <summary>Sets the orientation; the screen then takes pictures in that orientation's width and height.</summary>
    public static byte[] Orientation(ScreenOrientation orientation)
    {
        var bytes = new byte[16];
        Command(SetOrientation).CopyTo(bytes, 0);
        bytes[6] = (byte)((int)orientation + 100);
        bytes[7] = NativeWidth >> 8;
        bytes[8] = NativeWidth & 255;
        bytes[9] = NativeHeight >> 8;
        bytes[10] = NativeHeight & 255;
        return bytes;
    }

    /// <summary>The picture size in an orientation: 480 × 320 on its side, 320 × 480 upright.</summary>
    public static (int Width, int Height) Size(ScreenOrientation orientation) =>
        orientation is ScreenOrientation.Landscape or ScreenOrientation.ReverseLandscape ? (NativeHeight, NativeWidth) : (NativeWidth, NativeHeight);

    /// <summary>The command that announces a picture for <paramref name="rect"/>, followed by its RGB565 pixels.</summary>
    public static byte[] BitmapHeader(ScreenRect rect) =>
        Command(DisplayBitmap, rect.X, rect.Y, rect.X + rect.Width - 1, rect.Y + rect.Height - 1);

    /// <summary>
    /// <paramref name="rect"/> of a 32-bit BGRA picture (premultiplied alpha, as WPF renders; the dashboard is opaque)
    /// as RGB565, two bytes a pixel, little-endian, row by row.
    /// </summary>
    public static byte[] Rgb565(ReadOnlySpan<byte> bgra, int stride, ScreenRect rect)
    {
        var result = new byte[rect.Width * rect.Height * 2];
        var o = 0;
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            var row = y * stride;
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                var p = row + x * 4;
                int b = bgra[p], g = bgra[p + 1], r = bgra[p + 2];
                var pixel = (r >> 3) << 11 | (g >> 2) << 5 | b >> 3;
                result[o++] = (byte)(pixel & 255);
                result[o++] = (byte)(pixel >> 8);
            }
        }
        return result;
    }

    /// <summary>
    /// What changed between two frames, as bands of <paramref name="bandHeight"/> rows trimmed to the changed columns,
    /// so a ticking clock costs a sliver, not the whole screen. With no previous frame, the whole picture.
    /// </summary>
    public static IReadOnlyList<ScreenRect> Changes(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current, int width, int height, int bandHeight = 16)
    {
        if (previous.Length != current.Length)
            return [new ScreenRect(0, 0, width, height)];
        var stride = width * 4;
        var changes = new List<ScreenRect>();
        for (var top = 0; top < height; top += bandHeight)
        {
            var rows = Math.Min(bandHeight, height - top);
            var left = int.MaxValue;
            var right = -1;
            for (var y = top; y < top + rows; y++)
            {
                var a = previous.Slice(y * stride, stride);
                var b = current.Slice(y * stride, stride);
                if (a.SequenceEqual(b))
                    continue;
                for (var x = 0; x < width && x < left; x++)
                {
                    if (!a.Slice(x * 4, 4).SequenceEqual(b.Slice(x * 4, 4)))
                        left = x;
                }
                for (var x = width - 1; x > right; x--)
                {
                    if (!a.Slice(x * 4, 4).SequenceEqual(b.Slice(x * 4, 4)))
                        right = x;
                }
            }
            if (right >= 0)
                changes.Add(new ScreenRect(left, top, right - left + 1, rows));
        }
        return changes;
    }
}

using ImpiousBonum.Core.Screens;

namespace ImpiousBonum.Core.Tests;

public sealed class UsbScreenTests
{
    [Fact]
    public void Commands_pack_their_coordinates_into_five_bytes()
    {
        // The whole landscape screen: x 0..479, y 0..319.
        Assert.Equal(new byte[] { 0, 0, 7, 125, 63, TuringProtocol.DisplayBitmap }, TuringProtocol.BitmapHeader(new ScreenRect(0, 0, 480, 320)));
        Assert.Equal(new byte[] { 25, 64 | 3, 2 << 4 | 1, 63 << 2, 50, TuringProtocol.DisplayBitmap },
            TuringProtocol.Command(TuringProtocol.DisplayBitmap, 101, 50, 127, 50));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(0, 255)]
    [InlineData(50, 127)]
    [InlineData(150, 0)]
    public void Brightness_is_sent_upside_down(int percent, int sent)
    {
        var expected = TuringProtocol.Command(TuringProtocol.SetBrightness, sent);
        Assert.Equal(expected, TuringProtocol.Brightness(percent));
    }

    [Fact]
    public void Orientation_sends_the_native_size()
    {
        var bytes = TuringProtocol.Orientation(ScreenOrientation.Landscape);
        Assert.Equal(16, bytes.Length);
        Assert.Equal(TuringProtocol.SetOrientation, bytes[5]);
        Assert.Equal(new byte[] { 102, 1, 64, 1, 224 }, bytes[6..11]);
        Assert.Equal((480, 320), TuringProtocol.Size(ScreenOrientation.Landscape));
        Assert.Equal((320, 480), TuringProtocol.Size(ScreenOrientation.ReversePortrait));
    }

    [Fact]
    public void Pixels_become_little_endian_rgb565()
    {
        // 2 × 1 BGRA: pure red, then white.
        byte[] bgra = [0, 0, 255, 255, 255, 255, 255, 255];
        Assert.Equal(new byte[] { 0x00, 0xF8, 0xFF, 0xFF }, TuringProtocol.Rgb565(bgra, 8, new ScreenRect(0, 0, 2, 1)));
        Assert.Equal(new byte[] { 0xFF, 0xFF }, TuringProtocol.Rgb565(bgra, 8, new ScreenRect(1, 0, 1, 1)));
    }

    [Fact]
    public void Only_the_bands_that_changed_are_sent()
    {
        const int width = 40, height = 40;
        var before = new byte[width * height * 4];
        var after = before.ToArray();
        // One pixel at (10, 5) and a run on rows 20–21 from x 3 to 30.
        after[(5 * width + 10) * 4] = 1;
        after[(20 * width + 3) * 4 + 1] = 1;
        after[(21 * width + 30) * 4 + 2] = 1;

        Assert.Equal([new ScreenRect(10, 0, 1, 16), new ScreenRect(3, 16, 28, 16)], TuringProtocol.Changes(before, after, width, height));
        Assert.Empty(TuringProtocol.Changes(after, after, width, height));
        Assert.Equal([new ScreenRect(0, 0, width, height)], TuringProtocol.Changes([], after, width, height));
    }

    [Fact]
    public async Task The_screen_is_set_up_then_sent_the_whole_picture_then_only_changes()
    {
        var written = new MemoryStream();
        using var screen = new UsbScreen(() => new KeepOpen(written));
        screen.Configure(ScreenOrientation.Landscape, 80);
        var frame = new byte[480 * 320 * 4];

        Assert.True(screen.TrySend(frame, 480, 320));
        await screen.Sending;
        var setup = 16 + 6 + 6;
        Assert.Equal(setup + 6 + 480 * 320 * 2, written.Length);
        Assert.Equal("Connected", screen.Status);

        var changed = frame.ToArray();
        changed[0] = 255;
        written.SetLength(0);
        Assert.True(screen.TrySend(changed, 480, 320));
        await screen.Sending;
        Assert.Equal(6 + 1 * 16 * 2, written.Length);
    }

    [Fact]
    public async Task A_screen_that_cant_be_opened_says_why()
    {
        using var screen = new UsbScreen(() => throw new IOException("The port 'COM9' does not exist."));
        screen.TrySend(new byte[480 * 320 * 4], 480, 320);
        await screen.Sending;
        Assert.Contains("COM9", screen.Status);
    }

    /// <summary>The screen disposes its stream when it closes; the test still wants to read it.</summary>
    private sealed class KeepOpen(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}

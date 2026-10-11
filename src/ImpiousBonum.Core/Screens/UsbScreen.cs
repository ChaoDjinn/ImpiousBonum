using System.IO.Ports;
using Microsoft.Win32;

namespace ImpiousBonum.Core.Screens;

/// <summary>
/// A small USB screen (see <see cref="TuringProtocol"/>): opens its serial port, sets it up, and sends frames,
/// only the parts that changed, on a background task so a slow screen never holds up the dashboard. If the screen
/// is unplugged it keeps trying to reconnect, then sends the whole picture again.
/// </summary>
/// <param name="open">Opens the connection; a serial port unless a test swaps it.</param>
public sealed class UsbScreen(Func<Stream> open) : IDisposable
{
    /// <summary>The USB id of the CH552-based 3.5" screens (Turing Smart Screen and its clones).</summary>
    public const string UsbId = @"VID_1A86&PID_5722";

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private Stream? _stream;
    private Task _sending = Task.CompletedTask;
    private byte[]? _sent;
    private ScreenOrientation _orientation = ScreenOrientation.Landscape;
    private int _brightness = 50;
    private bool _setupNeeded = true;
    private DateTime _nextTry;
    private bool _disposed;

    /// <summary>"Connected", or what went wrong, for the tray.</summary>
    public string Status { get; private set; } = "Connecting…";

    /// <summary>Opens a screen on a serial port such as COM5.</summary>
    public static UsbScreen OnPort(string port) => new(() =>
    {
        var serial = new SerialPort(port, 115_200) { WriteTimeout = 3000, ReadTimeout = 1000, RtsEnable = true, DtrEnable = true };
        serial.Open();
        return new SerialStream(serial);
    });

    /// <summary>The serial port a 3.5" screen is plugged into, from Windows' record of USB devices, or null.</summary>
    public static string? FindPort()
    {
        using var devices = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\{UsbId}");
        if (devices is null)
            return null;
        var present = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in devices.GetSubKeyNames())
        {
            using var parameters = devices.OpenSubKey($@"{instance}\Device Parameters");
            if (parameters?.GetValue("PortName") is string port && present.Contains(port))
                return port;
        }
        return null;
    }

    /// <summary>Which way up and how bright; applied before the next frame.</summary>
    public void Configure(ScreenOrientation orientation, int brightness)
    {
        lock (_gate)
        {
            if (orientation == _orientation && brightness == _brightness)
                return;
            if (orientation != _orientation)
                _sent = null;
            (_orientation, _brightness) = (orientation, brightness);
            _setupNeeded = true;
        }
    }

    /// <summary>
    /// Queues a frame (32-bit BGRA, the screen's size for its orientation). Returns false, sending nothing, while the
    /// last one is still on its way: the next frame will catch up.
    /// </summary>
    public bool TrySend(byte[] bgra, int width, int height)
    {
        lock (_gate)
        {
            if (_disposed || !_sending.IsCompleted)
                return false;
            var frame = bgra.ToArray();
            _sending = Task.Run(() => Send(frame, width, height));
            return true;
        }
    }

    /// <summary>Waits for the frame on its way, for tests.</summary>
    internal Task Sending
    {
        get
        {
            lock (_gate)
                return _sending;
        }
    }

    private void Send(byte[] frame, int width, int height)
    {
        try
        {
            if (!Connect())
                return;
            byte[]? previous;
            lock (_gate)
            {
                if (_setupNeeded)
                {
                    Write(TuringProtocol.Orientation(_orientation));
                    Write(TuringProtocol.Brightness(_brightness));
                    Write(TuringProtocol.Command(TuringProtocol.ScreenOn));
                    _setupNeeded = false;
                }
                previous = _sent;
            }

            foreach (var rect in TuringProtocol.Changes(previous ?? [], frame, width, height))
            {
                Write(TuringProtocol.BitmapHeader(rect));
                var pixels = TuringProtocol.Rgb565(frame, width * 4, rect);
                // Several rows at a time, as the screen's own software does, rather than one huge write.
                var chunk = Math.Max(2, width * 8);
                for (var offset = 0; offset < pixels.Length; offset += chunk)
                    Write(pixels.AsSpan(offset, Math.Min(chunk, pixels.Length - offset)));
            }
            lock (_gate)
                _sent = frame;
            Status = "Connected";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            Disconnect($"Lost the screen: {ex.Message}");
        }
    }

    private bool Connect()
    {
        if (_stream is not null)
            return true;
        if (DateTime.UtcNow < _nextTry)
            return false;
        try
        {
            _stream = open();
            lock (_gate)
            {
                _setupNeeded = true;
                _sent = null;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Disconnect(ex is UnauthorizedAccessException
                ? "The screen's port is in use by another program (close the screen's own software)."
                : $"Couldn't open the screen: {ex.Message}");
            return false;
        }
    }

    private void Write(ReadOnlySpan<byte> bytes) => _stream!.Write(bytes);

    private void Disconnect(string status)
    {
        Status = status;
        try
        {
            _stream?.Dispose();
        }
        catch (IOException)
        {
            // Already gone.
        }
        _stream = null;
        _nextTry = DateTime.UtcNow + RetryAfter;
    }

    public void Dispose()
    {
        Task sending;
        lock (_gate)
        {
            _disposed = true;
            sending = _sending;
        }
        sending.Wait(TimeSpan.FromSeconds(3));
        try
        {
            // Leave it dark rather than frozen on the last frame.
            if (_stream is not null)
                Write(TuringProtocol.Command(TuringProtocol.ScreenOff));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            // Unplugged already.
        }
        _stream?.Dispose();
        _stream = null;
    }

    /// <summary>A serial port as a stream that closes the port with it.</summary>
    private sealed class SerialStream(SerialPort port) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => port.BaseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                port.Dispose();
            base.Dispose(disposing);
        }
    }
}

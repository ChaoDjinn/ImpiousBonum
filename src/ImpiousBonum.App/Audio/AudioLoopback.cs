using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ImpiousBonum.App.Audio;

/// <summary>
/// What the PC is playing, as captured from the default output device (WASAPI loopback: no extra drivers, nothing
/// changes for the listener). Shared by every visualiser and only running while at least one is on screen, so it costs
/// nothing otherwise. Keeps the newest few thousand samples, mixed down to mono.
/// </summary>
public sealed class AudioLoopback
{
    public static AudioLoopback Shared { get; } = new();

    /// <summary>After this long with no audio arriving (Windows sends none while nothing plays), it reads as silence.</summary>
    private static readonly TimeSpan SilentAfter = TimeSpan.FromMilliseconds(150);

    /// <summary>How long to wait before trying again after the device went away or capture couldn't start.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly float[] _ring = new float[8192];
    private int _write;
    private int _filled;
    private DateTime _lastData;
    private DateTime _nextTry;
    private int _users;
    private WasapiLoopbackCapture? _capture;

    public int SampleRate { get; private set; } = 48_000;

    /// <summary>Starts capturing if this is the first user. Never throws: with no output device the visualiser just stays flat.</summary>
    public void Acquire()
    {
        lock (_gate)
        {
            _users++;
            EnsureRunning();
        }
    }

    /// <summary>Stops capturing once the last user has gone.</summary>
    public void Release()
    {
        WasapiLoopbackCapture? stop = null;
        lock (_gate)
        {
            _users = Math.Max(0, _users - 1);
            if (_users == 0)
            {
                stop = _capture;
                _capture = null;
                _filled = 0;
            }
        }
        Stop(stop);
    }

    /// <summary>
    /// Copies the newest samples into <paramref name="destination"/> (oldest first) and returns how many; 0 while
    /// nothing is playing. Also restarts capture if the device went away and it's time to try again.
    /// </summary>
    public int Read(Span<float> destination)
    {
        lock (_gate)
        {
            if (_users > 0 && _capture is null)
                EnsureRunning();
            if (_filled == 0 || DateTime.UtcNow - _lastData > SilentAfter)
                return 0;
            var count = Math.Min(Math.Min(destination.Length, _filled), _ring.Length);
            var start = (_write - count + _ring.Length) % _ring.Length;
            for (var i = 0; i < count; i++)
                destination[i] = _ring[(start + i) % _ring.Length];
            return count;
        }
    }

    private void EnsureRunning()
    {
        if (_capture is not null || _users == 0 || DateTime.UtcNow < _nextTry)
            return;
        try
        {
            var capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            SampleRate = format.SampleRate;
            capture.DataAvailable += (_, e) => Append(e.Buffer, e.BytesRecorded, format);
            capture.RecordingStopped += (_, e) =>
            {
                lock (_gate)
                {
                    if (_capture != capture)
                        return;
                    // The device was unplugged or changed: try again in a moment, perhaps on the new default.
                    _capture = null;
                    _filled = 0;
                    _nextTry = DateTime.UtcNow + RetryAfter;
                }
                capture.Dispose();
            };
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _nextTry = DateTime.UtcNow + RetryAfter;
        }
    }

    private void Append(byte[] buffer, int bytes, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var frameBytes = format.BlockAlign;
        if (frameBytes <= 0)
            return;
        lock (_gate)
        {
            for (var offset = 0; offset + frameBytes <= bytes; offset += frameBytes)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                    sum += Sample(buffer, offset + c * (frameBytes / channels), format);
                _ring[_write] = sum / channels;
                _write = (_write + 1) % _ring.Length;
                _filled = Math.Min(_filled + 1, _ring.Length);
            }
            if (bytes > 0)
                _lastData = DateTime.UtcNow;
        }
    }

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: an extensible format's way of saying 32-bit float.</summary>
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>One sample as −1 to 1, from 32-bit float or 16, 24 or 32-bit PCM (what shared-mode devices use).</summary>
    internal static float Sample(byte[] buffer, int offset, WaveFormat format)
    {
        var encoding = format.Encoding == WaveFormatEncoding.Extensible && format is WaveFormatExtensible extensible
            ? extensible.SubFormat == IeeeFloatSubtype ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Pcm
            : format.Encoding;
        return (encoding, format.BitsPerSample) switch
        {
            (WaveFormatEncoding.IeeeFloat, 32) => BitConverter.ToSingle(buffer, offset),
            (WaveFormatEncoding.Pcm, 16) => BitConverter.ToInt16(buffer, offset) / 32768f,
            (WaveFormatEncoding.Pcm, 24) => ((buffer[offset] << 8 | buffer[offset + 1] << 16 | buffer[offset + 2] << 24) >> 8) / 8388608f,
            (WaveFormatEncoding.Pcm, 32) => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0,
        };
    }

    private static void Stop(WasapiLoopbackCapture? capture)
    {
        if (capture is null)
            return;
        try
        {
            capture.StopRecording();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Already stopped (the device went away); disposing is all that's left.
        }
        capture.Dispose();
    }
}

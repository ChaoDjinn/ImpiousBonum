using System.Runtime.InteropServices;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// How bright the dashboard should be: dimmed by a schedule rule (e.g. 30% at night), and dimmed further when you've
/// been away. Taps on a dark screen "peek" at full brightness for a moment. Nothing dims while the layout is edited.
/// </summary>
public static class Dimming
{
    /// <summary>How long a tap on a dark screen shows it at full brightness.</summary>
    public static readonly TimeSpan PeekFor = TimeSpan.FromSeconds(30);

    /// <summary>At or below this, the screen is too dark to see what a tap would press, so a tap peeks instead.</summary>
    public const double TapToPeekBelow = 0.2;

    /// <param name="scheduled">The schedule's brightness now, in percent, or null.</param>
    /// <param name="idle">How long since the last keyboard, mouse or touch input anywhere.</param>
    /// <param name="awayMinutes">Dim after this long away; 0 never does.</param>
    /// <param name="awayBrightness">How far, in percent.</param>
    /// <param name="gameDrawing">A game (or video) is drawing frames in front: someone is there, even if only a gamepad is moving.</param>
    /// <returns>0 (black) to 1 (full).</returns>
    public static double Brightness(int? scheduled, TimeSpan idle, int awayMinutes, int awayBrightness, bool gameDrawing, bool peeking, bool editing)
    {
        if (editing || peeking)
            return 1;
        var percent = Math.Clamp(scheduled ?? 100, 0, 100);
        if (awayMinutes > 0 && !gameDrawing && idle >= TimeSpan.FromMinutes(awayMinutes))
            percent = Math.Min(percent, Math.Clamp(awayBrightness, 0, 100));
        return percent / 100.0;
    }

    /// <summary>How long since the last keyboard, mouse or touch input in this session (any app).</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;
        // Both are milliseconds since boot in 32 bits, so the subtraction wraps correctly every 49.7 days.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

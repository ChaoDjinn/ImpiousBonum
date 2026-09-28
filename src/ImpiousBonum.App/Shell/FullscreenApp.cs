using System.Runtime.InteropServices;

namespace ImpiousBonum.App.Shell;

/// <summary>Spots a fullscreen app (a game, a video) in front on the same monitor as the dashboard.</summary>
public static class FullscreenApp
{
    private const uint MonitorDefaultToNull = 0;
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];
    private static readonly int OwnProcessId = Environment.ProcessId;

    /// <summary>True when the foreground window belongs to another app and covers the whole of <paramref name="dashboard"/>'s monitor.</summary>
    public static bool IsInFrontOf(nint dashboard)
    {
        var window = GetForegroundWindow();
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window))
            return false;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == OwnProcessId)
            return false;

        // The desktop covers the monitor too.
        var className = new char[64];
        var length = GetClassNameW(window, className, className.Length);
        if (ShellClasses.Contains(new string(className, 0, length)))
            return false;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNull);
        if (monitor == 0 || monitor != MonitorFromWindow(dashboard, MonitorDefaultToNull))
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info) || !GetWindowRect(window, out var bounds))
            return false;

        var m = info.Monitor;
        return bounds.Left <= m.Left && bounds.Top <= m.Top && bounds.Right >= m.Right && bounds.Bottom >= m.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);
}

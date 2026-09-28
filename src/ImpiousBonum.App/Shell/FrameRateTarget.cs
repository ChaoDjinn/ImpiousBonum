using System.Diagnostics;
using System.Runtime.InteropServices;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Decides which app the FPS reading follows: the foreground app, or the topmost visible app on a chosen monitor
/// (useful when a game runs on one screen while you type on another). Called once a second from the sampler's
/// background thread (FPS) and from the UI thread (game layouts), so calls are serialised.
/// </summary>
public sealed class FrameRateTarget(Func<string?> monitorId)
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint MonitorDefaultToNull = 0;
    private const int DwmwaCloaked = 14;
    private static readonly TimeSpan MonitorCacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Dictionary<int, string?> _names = [];
    private readonly Lock _lock = new();
    private (string? Id, string? Device, DateTime Expires) _monitor;

    public (int ProcessId, string? Name) Get()
    {
        lock (_lock)
        {
            var id = monitorId();
            if (id is null)
                return ForegroundApp.Get();

            var device = DeviceFor(id);
            return device is null ? (0, null) : TopAppOn(device);
        }
    }

    private string? DeviceFor(string id)
    {
        if (_monitor.Id != id || DateTime.UtcNow > _monitor.Expires)
        {
            var device = DisplayMonitor.GetAll().FirstOrDefault(m => m.Id == id)?.Device;
            _monitor = (id, device, DateTime.UtcNow + MonitorCacheLifetime);
        }
        return _monitor.Device;
    }

    private (int ProcessId, string? Name) TopAppOn(string device)
    {
        (int, string?) result = (0, null);
        var className = new char[64];

        // EnumWindows walks top-level windows front to back, so the first match is what's on top.
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window))
                return true;
            if ((GetWindowLongPtrW(window, GwlExStyle) & WsExToolWindow) != 0)
                return true;
            if (DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var length = GetClassNameW(window, className, className.Length);
            if (ShellClasses.Contains(new string(className, 0, length)))
                return true;

            var monitor = MonitorFromWindow(window, MonitorDefaultToNull);
            if (monitor == 0 || !MonitorDevice(monitor).Equals(device, StringComparison.OrdinalIgnoreCase))
                return true;

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == _ownProcessId)
                return true;

            result = ((int)processId, NameOf((int)processId));
            return false;
        }, 0);

        return result;
    }

    private string? NameOf(int processId)
    {
        if (_names.TryGetValue(processId, out var name))
            return name;

        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            name = null;
        }

        if (_names.Count > 256)
            _names.Clear();
        _names[processId] = name;
        return name;
    }

    private static string MonitorDevice(nint monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfoW(monitor, ref info) ? info.Device : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint window, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}

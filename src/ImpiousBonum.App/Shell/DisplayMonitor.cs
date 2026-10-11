using System.Runtime.InteropServices;
using Screen = System.Windows.Forms.Screen;

namespace ImpiousBonum.App.Shell;

/// <summary>A connected monitor with its bounds in physical pixels and a stable id for remembering it.</summary>
public sealed record DisplayMonitor(string Id, string Device, string Description, PixelRect Bounds, bool IsPrimary)
{
    /// <summary>The monitor's own name, such as "DELL U2720Q", when Windows knows it.</summary>
    public string? Name { get; init; }

    /// <summary>"DISPLAY7", as Windows numbers it.</summary>
    public string ShortDevice => ShortName(Device);

    /// <summary>"DELL U2720Q · 3840×2160 (main)" for menus; the device name when the monitor's own name isn't known.</summary>
    public string DisplayName => $"{Name ?? ShortDevice} · {Bounds.Width}×{Bounds.Height}{(IsPrimary ? " (main)" : string.Empty)}";

    /// <summary>"DISPLAY7" for the GDI device name \\.\DISPLAY7.</summary>
    public static string ShortName(string device) => device.Replace(@"\\.\", string.Empty);

    public static IReadOnlyList<DisplayMonitor> GetAll()
    {
        var names = MonitorNames.Get();
        var monitors = Screen.AllScreens.Select(screen =>
        {
            var (id, description) = GetMonitorIdentity(screen.DeviceName);
            var b = screen.Bounds;
            return new DisplayMonitor(id ?? screen.DeviceName, screen.DeviceName, description ?? string.Empty,
                new PixelRect(b.X, b.Y, b.Width, b.Height), screen.Primary)
            {
                Name = names.GetValueOrDefault(id ?? screen.DeviceName) ?? names.GetValueOrDefault(screen.DeviceName),
            };
        }).ToList();
        return Distinguished(monitors);
    }

    /// <summary>Monitors of the same model get their device names too, so the menus can tell them apart.</summary>
    internal static IReadOnlyList<DisplayMonitor> Distinguished(IReadOnlyList<DisplayMonitor> monitors) =>
        monitors.Select(m => m.Name is { } name && monitors.Count(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)) > 1
            ? m with { Name = $"{name} ({m.ShortDevice})" }
            : m).ToList();

    /// <summary>
    /// Finds the saved monitor, or on first run picks the smallest secondary monitor (a little touchscreen is the typical home).
    /// Returns null when a saved monitor is disconnected, so the dashboard hides instead of landing on the main screen.
    /// </summary>
    public static DisplayMonitor? Resolve(AppSettings settings, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (settings.MonitorId is not null || settings.MonitorDevice is not null)
        {
            return monitors.FirstOrDefault(m => m.Id == settings.MonitorId)
                ?? monitors.FirstOrDefault(m => settings.MonitorId is null && m.Device == settings.MonitorDevice);
        }

        return monitors.Where(m => !m.IsPrimary).MinBy(m => (long)m.Bounds.Width * m.Bounds.Height)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors.FirstOrDefault();
    }

    private static (string? Id, string? Description) GetMonitorIdentity(string adapterDevice)
    {
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        // With EDD_GET_DEVICE_INTERFACE_NAME, DeviceID is the monitor's interface path, e.g. \\?\DISPLAY#GSM5B09#...
        return EnumDisplayDevicesW(adapterDevice, 0, ref device, EddGetDeviceInterfaceName)
            ? (string.IsNullOrEmpty(device.DeviceId) ? null : device.DeviceId, device.DeviceString)
            : (null, null);
    }

    private const uint EddGetDeviceInterfaceName = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(string device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);
}

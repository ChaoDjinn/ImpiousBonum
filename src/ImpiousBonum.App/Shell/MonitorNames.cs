using System.Runtime.InteropServices;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Monitors' own names, such as "DELL U2720Q", as Windows' display settings show them (from each monitor's EDID),
/// keyed by the monitor's interface path (<see cref="DisplayMonitor.Id"/>) and by its GDI device name.
/// </summary>
internal static class MonitorNames
{
    /// <summary>Friendly names of the active monitors; empty if Windows won't say (a remote session, say).</summary>
    public static IReadOnlyDictionary<string, string> Get()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
                return names;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
                return names;

            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new TargetDeviceName
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GetTargetName,
                        Size = Marshal.SizeOf<TargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0 || string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName))
                    continue;
                var name = target.MonitorFriendlyDeviceName.Trim();
                if (!string.IsNullOrEmpty(target.MonitorDevicePath))
                    names.TryAdd(target.MonitorDevicePath, name);

                var source = new SourceDeviceName
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GetSourceName,
                        Size = Marshal.SizeOf<SourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) == 0 && !string.IsNullOrEmpty(source.ViewGdiDeviceName))
                    names.TryAdd(source.ViewGdiDeviceName, name);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            // Not worth failing over: the menus fall back to DISPLAY1 and so on.
        }
        return names;
    }

    private const uint QdcOnlyActivePaths = 0x2;
    private const int GetSourceName = 1;
    private const int GetTargetName = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO: the header, then a 48-byte union this code never reads.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public int Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);
}

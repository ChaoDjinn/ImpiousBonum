using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public static MemoryStatusEx Create() => new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemPowerStatus
    {
        /// <summary>0 offline (on battery), 1 online (plugged in), 255 unknown.</summary>
        public byte AcLineStatus;

        /// <summary>Flags: 8 charging, 128 no system battery, 255 unknown.</summary>
        public byte BatteryFlag;

        /// <summary>0–100, or 255 unknown.</summary>
        public byte BatteryLifePercent;

        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}

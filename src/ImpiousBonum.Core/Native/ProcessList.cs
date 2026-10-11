using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>One process's totals at the moment of reading.</summary>
/// <param name="CpuTime">User plus kernel time, in 100 ns ticks, since it started.</param>
/// <param name="PrivateWorkingSet">Memory only it uses that's in RAM, in bytes: what Task Manager shows as Memory.</param>
public readonly record struct ProcessReading(int ProcessId, string Name, long CpuTime, long PrivateWorkingSet);

/// <summary>
/// Every process in one call (NtQuerySystemInformation, as Task Manager does), without opening any of them: cheaper
/// than a handle per process, and it covers protected system processes too.
/// </summary>
internal static unsafe class ProcessList
{
    private const int SystemProcessInformationClass = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    private static byte[] _buffer = new byte[512 * 1024];

    public static List<ProcessReading> Read()
    {
        var readings = new List<ProcessReading>(400);
        while (true)
        {
            fixed (byte* buffer = _buffer)
            {
                var status = NtQuerySystemInformation(SystemProcessInformationClass, buffer, _buffer.Length, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    // Processes started since the last read: grow, with room for a few more.
                    _buffer = new byte[Math.Max(needed, _buffer.Length) + 64 * 1024];
                    continue;
                }
                if (status < 0)
                    return readings;

                var offset = 0;
                while (true)
                {
                    var info = (SystemProcessInformation*)(buffer + offset);
                    var id = (int)info->UniqueProcessId;
                    // Process 0 is the idle process: its "CPU time" is the time nothing else ran.
                    if (id != 0)
                    {
                        var name = info->ImageName.Buffer == 0 ? "System" : new string((char*)info->ImageName.Buffer, 0, info->ImageName.Length / 2);
                        readings.Add(new ProcessReading(id, name, info->UserTime + info->KernelTime, info->WorkingSetPrivateSize));
                    }
                    if (info->NextEntryOffset == 0)
                        break;
                    offset += (int)info->NextEntryOffset;
                }
                return readings;
            }
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, byte* information, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    /// <summary>SYSTEM_PROCESS_INFORMATION, up to the fields used; sequential layout gives the native padding on x64 and x86.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public nint UniqueProcessId;
    }
}

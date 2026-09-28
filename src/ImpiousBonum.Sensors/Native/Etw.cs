using System.Runtime.InteropServices;

namespace ImpiousBonum.Sensors.Native;

/// <summary>Event Tracing for Windows: just enough to run a real-time session and receive raw event headers.</summary>
internal static unsafe partial class Etw
{
    public const uint ErrorSuccess = 0;
    public const uint ErrorAlreadyExists = 183;
    public const uint WnodeFlagTracedGuid = 0x00020000;
    public const uint EventTraceRealTimeMode = 0x00000100;
    public const uint EventTraceControlStop = 1;
    public const uint EventControlCodeEnableProvider = 1;
    public const uint ProcessTraceModeRealTime = 0x00000100;
    public const uint ProcessTraceModeRawTimestamp = 0x00001000;
    public const uint ProcessTraceModeEventRecord = 0x10000000;
    public const uint EventFilterTypeEventId = 0x80000200;
    public const uint EnableTraceParametersVersion2 = 2;
    public const byte TraceLevelInformation = 4;
    public const byte TraceLevelVerbose = 5;

    /// <summary>ClientContext value selecting QueryPerformanceCounter timestamps (same clock as <see cref="System.Diagnostics.Stopwatch"/>).</summary>
    public const uint ClientContextQpc = 1;

    public static readonly ulong InvalidProcessTraceHandle = ulong.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    public struct WnodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public nint LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventFilterDescriptor
    {
        public ulong Ptr;
        public uint Size;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EnableTraceParameters
    {
        public uint Version;
        public uint EnableProperty;
        public uint ControlFlags;
        public Guid SourceId;
        public EventFilterDescriptor* EnableFilterDesc;
        public uint FilterDescCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceHeader
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTrace
    {
        public EventTraceHeader Header;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public nint MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeZoneInformation
    {
        public int Bias;
        public fixed char StandardName[32];
        public SystemTime StandardDate;
        public int StandardBias;
        public fixed char DaylightName[32];
        public SystemTime DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TraceLogfileHeader
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public nint LoggerName;
        public nint LogFileName;
        public TimeZoneInformation TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceLogfile
    {
        public nint LogFileName;
        public nint LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EventTrace CurrentEvent;
        public TraceLogfileHeader LogfileHeader;
        public nint BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public delegate* unmanaged<EventRecord*, void> EventRecordCallback;
        public uint IsKernelTrace;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventDescriptor
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventHeader
    {
        public ushort Size;
        public ushort HeaderType;
        public ushort Flags;
        public ushort EventProperty;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public EventDescriptor EventDescriptor;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventRecord
    {
        public EventHeader EventHeader;
        public uint BufferContext;
        public ushort ExtendedDataCount;
        public ushort UserDataLength;
        public nint ExtendedData;
        public nint UserData;
        public nint UserContext;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTraceW(out ulong sessionHandle, string sessionName, EventTraceProperties* properties);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTraceW(ulong sessionHandle, string? sessionName, EventTraceProperties* properties, uint controlCode);

    [LibraryImport("advapi32.dll")]
    public static partial uint EnableTraceEx2(ulong sessionHandle, Guid* providerId, uint controlCode, byte level,
        ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, EnableTraceParameters* enableParameters);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial ulong OpenTraceW(EventTraceLogfile* logfile);

    [LibraryImport("advapi32.dll")]
    public static partial uint ProcessTrace(ulong* handleArray, uint handleCount, nint startTime, nint endTime);

    [LibraryImport("advapi32.dll")]
    public static partial uint CloseTrace(ulong traceHandle);
}

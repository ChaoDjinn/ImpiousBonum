using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImpiousBonum.Core.Sensors;
using ImpiousBonum.Sensors.Native;

namespace ImpiousBonum.Sensors;

/// <summary>
/// Counts frames per process the way PresentMon (and HWiNFO's "Framerate Presented") does: a real-time ETW session
/// on the Direct3D runtime's Present events. Works for windowed and fullscreen apps on any GPU vendor.
/// Covers DXGI (D3D10/11/12, and most Chromium/Electron apps) and D3D9. Vulkan and OpenGL aren't counted yet.
/// Needs admin (or Performance Log Users) to create the session.
/// </summary>
internal sealed unsafe class FrameRateMonitor : IDisposable
{
    private const string SessionName = "ImpiousBonum-FrameRate";

    // Microsoft-Windows-DXGI: Present_Start (42) and PresentMultiplaneOverlay_Start (55).
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly ushort[] DxgiPresentEvents = [42, 55];

    // Microsoft-Windows-D3D9: Present_Start (1).
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly ushort[] D3D9PresentEvents = [1];

    private readonly PresentRateTracker _tracker = new(Stopwatch.Frequency, window: TimeSpan.FromSeconds(2), staleAfter: TimeSpan.FromSeconds(5));
    private readonly Dictionary<int, string> _names = [];
    private ulong _session;
    private ulong _consumer = Etw.InvalidProcessTraceHandle;
    private GCHandle _self;
    private nint _loggerName;
    private Thread? _thread;

    public bool IsRunning => _thread is not null;

    /// <summary>Why frame counting isn't available, or null when it's running.</summary>
    public string? Problem { get; private set; }

    public bool Start()
    {
        if (IsRunning)
            return true;

        var status = StartSession();
        if (status == Etw.ErrorAlreadyExists)
        {
            // Left over from a crash: sessions outlive their process. Stop it and start fresh.
            StopSession(0, SessionName);
            status = StartSession();
        }

        if (status != Etw.ErrorSuccess)
        {
            Problem = status == 5 ? "FPS needs the sensor service (admin rights)" : $"Couldn't start frame counting (error {status})";
            return false;
        }

        Enable(DxgiProvider, DxgiPresentEvents);
        Enable(D3D9Provider, D3D9PresentEvents);

        _self = GCHandle.Alloc(this);
        _loggerName = Marshal.StringToHGlobalUni(SessionName);
        var logfile = new Etw.EventTraceLogfile
        {
            LoggerName = _loggerName,
            ProcessTraceMode = Etw.ProcessTraceModeRealTime | Etw.ProcessTraceModeEventRecord | Etw.ProcessTraceModeRawTimestamp,
            EventRecordCallback = &OnEvent,
            Context = GCHandle.ToIntPtr(_self),
        };
        _consumer = Etw.OpenTraceW(&logfile);
        if (_consumer == Etw.InvalidProcessTraceHandle)
        {
            Problem = $"Couldn't open frame counting session (error {Marshal.GetLastPInvokeError()})";
            Stop();
            return false;
        }

        // ProcessTrace blocks, delivering events on this thread until the session stops.
        _thread = new Thread(() =>
        {
            var handle = _consumer;
            Etw.ProcessTrace(&handle, 1, 0, 0);
        })
        {
            IsBackground = true,
            Name = "ETW frame counter",
        };
        _thread.Start();

        Problem = null;
        Log.Info("Frame counting started.");
        return true;
    }

    public void Stop()
    {
        if (_session != 0)
        {
            StopSession(_session, null);
            _session = 0;
        }

        if (_consumer != Etw.InvalidProcessTraceHandle)
        {
            Etw.CloseTrace(_consumer);
            _consumer = Etw.InvalidProcessTraceHandle;
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        if (_self.IsAllocated)
            _self.Free();
        if (_loggerName != 0)
        {
            Marshal.FreeHGlobal(_loggerName);
            _loggerName = 0;
        }

        _tracker.Clear();
        _names.Clear();
    }

    /// <summary>Current rate for every process presenting frames, with process names.</summary>
    public List<PresenterInfo> Snapshot()
    {
        var rates = _tracker.Snapshot(Stopwatch.GetTimestamp());

        // Forget names of processes that have stopped presenting; PIDs get reused.
        foreach (var gone in _names.Keys.Except(rates.Select(r => r.ProcessId)).ToList())
            _names.Remove(gone);

        return rates
            .Select(r => new PresenterInfo(r.ProcessId, NameOf(r.ProcessId), Math.Round(r.Fps, 1)))
            .OrderByDescending(p => p.Fps)
            .ToList();
    }

    private string NameOf(int processId)
    {
        if (_names.TryGetValue(processId, out var name))
            return name;

        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            name = $"pid {processId}";
        }

        _names[processId] = name;
        return name;
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(Etw.EventRecord* record)
    {
        // Filtering happens in the kernel (by event id), so everything arriving here is a present.
        if (GCHandle.FromIntPtr(record->UserContext).Target is FrameRateMonitor monitor)
            monitor._tracker.Record((int)record->EventHeader.ProcessId, record->EventHeader.TimeStamp);
    }

    private uint StartSession()
    {
        var buffer = AllocateProperties(out var properties);
        try
        {
            properties->Wnode.ClientContext = Etw.ClientContextQpc;
            properties->Wnode.Flags = Etw.WnodeFlagTracedGuid;
            properties->LogFileMode = Etw.EventTraceRealTimeMode;
            // Hand events over at least once a second rather than only when a buffer fills.
            properties->FlushTimer = 1;
            return Etw.StartTraceW(out _session, SessionName, properties);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void StopSession(ulong session, string? name)
    {
        var buffer = AllocateProperties(out var properties);
        try
        {
            Etw.ControlTraceW(session, name, properties, Etw.EventTraceControlStop);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Enable(Guid provider, ushort[] eventIds)
    {
        // EVENT_FILTER_EVENT_ID: BOOLEAN FilterIn; UCHAR Reserved; USHORT Count; USHORT Events[Count].
        var filterSize = 4 + 2 * eventIds.Length;
        var filter = stackalloc byte[filterSize];
        filter[0] = 1;
        filter[1] = 0;
        *(ushort*)(filter + 2) = (ushort)eventIds.Length;
        for (var i = 0; i < eventIds.Length; i++)
            ((ushort*)(filter + 4))[i] = eventIds[i];

        var descriptor = new Etw.EventFilterDescriptor { Ptr = (ulong)filter, Size = (uint)filterSize, Type = Etw.EventFilterTypeEventId };
        var parameters = new Etw.EnableTraceParameters
        {
            Version = Etw.EnableTraceParametersVersion2,
            EnableFilterDesc = &descriptor,
            FilterDescCount = 1,
        };

        var status = Etw.EnableTraceEx2(_session, &provider, Etw.EventControlCodeEnableProvider, Etw.TraceLevelVerbose,
            ulong.MaxValue, 0, 0, &parameters);
        if (status != Etw.ErrorSuccess)
            Log.Error($"Couldn't enable present events for provider {provider} (error {status}).");
    }

    /// <summary>EVENT_TRACE_PROPERTIES followed by room for the session name, zeroed.</summary>
    private static nint AllocateProperties(out Etw.EventTraceProperties* properties)
    {
        var size = sizeof(Etw.EventTraceProperties) + 1024 * sizeof(char);
        var buffer = Marshal.AllocHGlobal(size);
        Unsafe.InitBlockUnaligned((void*)buffer, 0, (uint)size);
        properties = (Etw.EventTraceProperties*)buffer;
        properties->Wnode.BufferSize = (uint)size;
        properties->LoggerNameOffset = (uint)sizeof(Etw.EventTraceProperties);
        return buffer;
    }

    public void Dispose() => Stop();
}

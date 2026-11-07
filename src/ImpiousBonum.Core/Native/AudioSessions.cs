using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>Which way an app's sound goes: out to the speakers or headphones, or in from a microphone.</summary>
public enum SoundFlow
{
    Out,
    In,
}

/// <summary>
/// One stream of an app's sound, as Windows meters it: whose (its process and program name), which way, whether it's
/// going now, and how loud it is at the moment, from 0 (silence) to 1 (full scale).
/// </summary>
public sealed record AppSound(int ProcessId, string App, SoundFlow Flow, bool Active, double Peak);

/// <summary>
/// The sound streams of some apps (by program name) or of all of them, from Core Audio's sessions on every audio device
/// that's on, and the meter Windows keeps on each (the green bar in the Volume mixer). Reading a meter opens nothing: an
/// app's microphone stream is metered only while the app itself has it open, and Windows doesn't count the app reading it
/// as using the microphone. The streams are looked for again only when asked, as finding them means asking every device;
/// their meters are cheap to read. Not thread-safe: use one reader from one loop.
/// </summary>
internal sealed class AudioSessionReader : IDisposable
{
    private const int Render = 0;
    private const int Capture = 1;
    private const int ActiveDevices = 1;
    private const int ClsctxAll = 0x17;
    private const int SessionActive = 1;

    private readonly Dictionary<int, string?> _names = [];
    private readonly List<Session> _sessions = [];
    private IDeviceEnumerator? _enumerator;

    /// <summary>
    /// The streams of the apps named in <paramref name="apps"/> (program names without .exe, ignoring case), or of every
    /// app when it's null, looked for again first with <paramref name="lookAgain"/>: on every device, or only on those
    /// one way (<paramref name="flow"/>). None when the audio service isn't running.
    /// </summary>
    public IReadOnlyList<AppSound> Read(IReadOnlySet<string>? apps, bool lookAgain, SoundFlow? flow = null)
    {
        try
        {
            if (lookAgain)
                Find(apps, flow);
            var sounds = new List<AppSound>(_sessions.Count);
            foreach (var session in _sessions)
            {
                var active = session.Control.GetState(out var state) >= 0 && state == SessionActive;
                var peak = session.Meter.GetPeakValue(out var level) >= 0 && float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
                sounds.Add(new AppSound(session.ProcessId, session.App, session.Flow, active, peak));
            }
            return sounds;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            // The audio service stopped or restarted, or a device went away mid-read: start again next time.
            Dispose();
            return [];
        }
    }

    private void Find(IReadOnlySet<string>? apps, SoundFlow? only)
    {
        ReleaseSessions();
        _enumerator ??= (IDeviceEnumerator)new Microphone.MMDeviceEnumerator();
        foreach (var (flow, dataFlow) in new[] { (SoundFlow.Out, Render), (SoundFlow.In, Capture) })
        {
            if (only is { } wanted && flow != wanted)
                continue;
            if (_enumerator.EnumAudioEndpoints(dataFlow, ActiveDevices, out var devices) < 0 || devices is null)
                continue;
            try
            {
                if (devices.GetCount(out var count) < 0)
                    continue;
                for (var i = 0; i < count; i++)
                {
                    if (devices.Item(i, out var device) < 0 || device is null)
                        continue;
                    try
                    {
                        FindOn(device, flow, apps);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
        }
        // Programs whose process has gone are forgotten, as a new process can reuse the id.
        var live = _sessions.Select(s => s.ProcessId).ToHashSet();
        foreach (var gone in _names.Keys.Where(id => !live.Contains(id)).ToList())
            _names.Remove(gone);
    }

    private void FindOn(Microphone.IMMDevice device, SoundFlow flow, IReadOnlySet<string>? apps)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, ClsctxAll, 0, out var activated) < 0 || activated is not IAudioSessionManager2 manager)
            return;
        try
        {
            if (manager.GetSessionEnumerator(out var enumerator) < 0 || enumerator is null)
                return;
            try
            {
                if (enumerator.GetCount(out var count) < 0)
                    return;
                for (var i = 0; i < count; i++)
                {
                    if (enumerator.GetSession(i, out var found) < 0 || found is not IAudioSessionControl2 control)
                        continue;
                    if (control.GetProcessId(out var processId) >= 0 && processId != 0 && NameOf(processId) is { } app && (apps is null || apps.Contains(app))
                        && control is IAudioMeterInformation meter)
                    {
                        _sessions.Add(new Session(control, meter, processId, app, flow));
                        continue;
                    }
                    Marshal.ReleaseComObject(control);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    /// <summary>A process's program name, without .exe, or null if it has gone or can't be asked.</summary>
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
        _names[processId] = name;
        return name;
    }

    private void ReleaseSessions()
    {
        // The meter and the control are the same COM object, so one release frees both.
        foreach (var session in _sessions)
            Marshal.ReleaseComObject(session.Control);
        _sessions.Clear();
    }

    public void Dispose()
    {
        ReleaseSessions();
        if (_enumerator is not null)
            Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
        _names.Clear();
    }

    private sealed record Session(IAudioSessionControl2 Control, IAudioMeterInformation Meter, int ProcessId, string App, SoundFlow Flow);

    // Vtable order matters: IUnknown is implied. Methods never called are declared parameterless purely to reserve their slots.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out Microphone.IMMDevice? device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out Microphone.IMMDevice? device);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        void GetAudioSessionControl();
        void GetSimpleAudioVolume();

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator? sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object? session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig]
        int GetState(out int state);

        void GetDisplayName();
        void SetDisplayName();
        void GetIconPath();
        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();

        // IAudioSessionControl2
        void GetSessionIdentifier();
        void GetSessionInstanceIdentifier();

        [PreserveSig]
        int GetProcessId(out int processId);

        /// <summary>S_OK (0) for the session of Windows' own sounds, S_FALSE (1) for any other.</summary>
        [PreserveSig]
        int IsSystemSoundsSession();
    }

    /// <summary>A session's own volume and mute: the app's slider in Windows' Volume mixer.</summary>
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float level, nint eventContext);

        [PreserveSig]
        int GetMasterVolume(out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>A device's own volume and mute: the volume in Windows' sound settings and its volume keys.</summary>
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEndpointVolume
    {
        void RegisterControlChangeNotify();
        void UnregisterControlChangeNotify();
        void GetChannelCount();
        void SetMasterVolumeLevel();

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, nint eventContext);

        void GetMasterVolumeLevel();

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        void SetChannelVolumeLevel();
        void SetChannelVolumeLevelScalar();
        void GetChannelVolumeLevel();
        void GetChannelVolumeLevelScalar();

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);
    }
}

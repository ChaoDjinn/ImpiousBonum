using System.Diagnostics;
using System.Runtime.InteropServices;
using static ImpiousBonum.Core.Native.AudioSessionReader;

namespace ImpiousBonum.Core.Native;

/// <summary>
/// One app in the volume mixer, all its sound streams together, as Windows' Volume mixer shows it: its volume from 0 to
/// 1, whether it's muted, and whether sound is coming out of it now.
/// </summary>
/// <param name="Key">Its program's name in lower case (<c>spotify</c>, <c>chrome</c>), or <see cref="SystemSounds"/>.</param>
/// <param name="Name">Its name to show: its program's description (<c>Google Chrome</c>), or its program's name.</param>
/// <param name="Exe">Its program, for its icon; null when Windows won't say, and for Windows' own sounds.</param>
public sealed record AppVolume(string Key, string Name, string? Exe, double Volume, bool Muted, bool Playing)
{
    /// <summary>The <see cref="Key"/> of Windows' own sounds: notifications, the ding of an error.</summary>
    public const string SystemSounds = "system";
}

/// <summary>The device Windows plays to (speakers or headphones), its name, and its own volume and mute.</summary>
public sealed record OutputVolume(string? Name, double Volume, bool Muted);

/// <summary>The device Windows plays to, if any, and the apps with sound streams on any device, in name order.</summary>
public sealed record MixerState(OutputVolume? Output, IReadOnlyList<AppVolume> Apps);

/// <summary>One sound stream as read: whose, and its volume, mute and whether sound is coming out of it now.</summary>
internal sealed record StreamVolume(string Key, string Name, string? Exe, double Volume, bool Muted, bool Playing);

/// <summary>Who a stream belongs to: its key and name to show, and its program.</summary>
internal sealed record StreamOwner(string Key, string Name, string? Exe)
{
    private const int MaxNameLength = 40;

    /// <summary>
    /// Windows' own audio engine, which some sound software (equalisers, virtual surround) has play a stream of its own:
    /// not an app, and Windows' Volume mixer leaves it out too.
    /// </summary>
    private const string AudioEngine = "audiodg";

    /// <summary>
    /// The owner of a process's streams; null when the process has gone or can't be asked, or is this app or Windows'
    /// audio engine.
    /// </summary>
    public static StreamOwner? Of(int processId)
    {
        if (processId == Environment.ProcessId)
            return null;
        string program;
        try
        {
            using var process = Process.GetProcessById(processId);
            program = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            return null;
        }
        if (program.Equals(AudioEngine, StringComparison.OrdinalIgnoreCase))
            return null;
        var exe = ExeFile.PathOf(processId);
        // Programs describe themselves well (Google Chrome, Spotify), but a game's description can be its copyright line.
        var description = exe is null ? null : ExeFile.Description(exe)?.Trim();
        var name = description is { Length: > 0 and <= MaxNameLength } ? description : program;
        return new StreamOwner(program.ToLowerInvariant(), name, exe);
    }

    public static readonly StreamOwner System = new(AppVolume.SystemSounds, "System sounds", null);
}

/// <summary>
/// The apps' sound streams on every audio device that's on, through Core Audio's sessions, with each one's volume and
/// mute (<see cref="ISimpleAudioVolume"/>) and meter; and the volume of the device Windows plays to. The streams are
/// looked for again only when asked, as that means asking every device. Not thread-safe: use one reader from one loop.
/// </summary>
internal sealed class AppVolumeReader : IDisposable
{
    private const int SessionExpired = 2;
    private const int SessionActive = 1;

    /// <summary>A peak under this is silence: a stream playing nothing.</summary>
    private const double Audible = 0.0005;

    private readonly Dictionary<int, StreamOwner?> _owners = [];
    private readonly HashSet<int> _seen = [];
    private readonly List<Session> _sessions = [];
    private IDeviceEnumerator? _enumerator;

    /// <summary>The apps' streams, looked for again first with <paramref name="lookAgain"/>, grouped by app.</summary>
    public IReadOnlyList<AppVolume> ReadApps(bool lookAgain)
    {
        try
        {
            if (lookAgain)
                Find();
            var streams = new List<StreamVolume>(_sessions.Count);
            foreach (var session in _sessions)
            {
                if (session.Control.GetState(out var state) < 0 || state == SessionExpired)
                    continue;
                var volume = session.Volume.GetMasterVolume(out var level) >= 0 && float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 1;
                var muted = session.Volume.GetMute(out var mute) >= 0 && mute;
                var playing = state == SessionActive && session.Meter.GetPeakValue(out var peak) >= 0 && peak > Audible;
                streams.Add(new StreamVolume(session.Owner.Key, session.Owner.Name, session.Owner.Exe, volume, muted, playing));
            }
            return Group(streams);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            // The audio service stopped or restarted, or a device went away mid-read: start again next time.
            Dispose();
            return [];
        }
    }

    /// <summary>The device Windows plays to and its volume; null when there's none, or no audio service.</summary>
    public OutputVolume? ReadOutput()
    {
        try
        {
            _enumerator ??= (IDeviceEnumerator)new Microphone.MMDeviceEnumerator();
            return AppVolumeControl.WithOutput(_enumerator, (device, volume) =>
                volume.GetMasterVolumeLevelScalar(out var level) >= 0
                    ? new OutputVolume(Microphone.FriendlyName(device), Math.Clamp(level, 0, 1), volume.GetMute(out var muted) >= 0 && muted)
                    : null);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            Dispose();
            return null;
        }
    }

    /// <summary>
    /// Each app's streams as one: the loudest volume (setting one sets them all), muted only when all are, and playing
    /// when any is. Apps in name order, Windows' own sounds last.
    /// </summary>
    internal static IReadOnlyList<AppVolume> Group(IEnumerable<StreamVolume> streams) =>
        [.. streams
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(app => new AppVolume(app.First().Key, app.First().Name, app.Select(s => s.Exe).FirstOrDefault(e => e is not null),
                app.Max(s => s.Volume), app.All(s => s.Muted), app.Any(s => s.Playing)))
            .OrderBy(app => app.Key == AppVolume.SystemSounds)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];

    private void Find()
    {
        ReleaseSessions();
        _enumerator ??= (IDeviceEnumerator)new Microphone.MMDeviceEnumerator();
        foreach (var (control, processId, system) in AppVolumeControl.Sessions(_enumerator))
        {
            var owner = system ? StreamOwner.System : OwnerOf(processId);
            if (owner is not null && control is ISimpleAudioVolume volume && control is IAudioMeterInformation meter)
                _sessions.Add(new Session(control, volume, meter, owner));
            else
                Marshal.ReleaseComObject(control);
        }
        // Processes with no streams now are forgotten, as one that's gone can have its id reused.
        foreach (var gone in _owners.Keys.Where(id => !_seen.Contains(id)).ToList())
            _owners.Remove(gone);
        _seen.Clear();
    }

    private StreamOwner? OwnerOf(int processId)
    {
        _seen.Add(processId);
        if (!_owners.TryGetValue(processId, out var owner))
            _owners[processId] = owner = StreamOwner.Of(processId);
        return owner;
    }

    private void ReleaseSessions()
    {
        // The volume and meter are the same COM object as the control, so one release frees all three.
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
        _owners.Clear();
    }

    private sealed record Session(IAudioSessionControl2 Control, ISimpleAudioVolume Volume, IAudioMeterInformation Meter, StreamOwner Owner);
}

/// <summary>
/// Sets an app's volume or mute, as its slider in Windows' Volume mixer does, or the device Windows plays to's. Each call
/// finds the streams afresh, so it can be called from any thread-pool thread, but not the UI's: COM objects made on an
/// STA thread would need its message loop.
/// </summary>
public static class AppVolumeControl
{
    /// <summary>The app name that means the device Windows plays to rather than an app.</summary>
    public const string Output = "@output";

    private const int Render = 0;
    private const int Multimedia = 1;
    private const int ActiveDevices = 1;
    private const int ClsctxAll = 0x17;

    /// <summary>
    /// Sets <paramref name="app"/>'s volume, from 0 to 1: every stream of the app with that program name or name (as
    /// <see cref="AppVolume"/> has them, ignoring case), or the device's for <see cref="Output"/>. False when there's none.
    /// </summary>
    public static bool SetVolume(string app, double volume)
    {
        var level = (float)Math.Clamp(volume, 0, 1);
        return Change(app,
            stream => stream.SetMasterVolume(level, 0) >= 0,
            output => output.SetMasterVolumeLevelScalar(level, 0) >= 0) ?? false;
    }

    /// <summary>Mutes <paramref name="app"/>, or unmutes it if it's muted; whether it's muted now, or null when there's no such app.</summary>
    public static bool? ToggleMute(string app)
    {
        bool? muted = null;
        var done = Change(app,
            stream =>
            {
                // All the app's streams follow its first, so they end up together.
                muted ??= !(stream.GetMute(out var was) >= 0 && was);
                return stream.SetMute(muted.Value, 0) >= 0;
            },
            output =>
            {
                muted = !(output.GetMute(out var was) >= 0 && was);
                return output.SetMute(muted.Value, 0) >= 0;
            });
        return done == true ? muted : null;
    }

    /// <summary>Whether <paramref name="typed"/> names the app: its program's name, or the name it shows.</summary>
    internal static bool Names(string typed, string key, string name) =>
        typed.Trim().Equals(key, StringComparison.OrdinalIgnoreCase) || typed.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)
        || typed.Trim().Equals(key + ".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs <paramref name="stream"/> on each of the app's streams, or <paramref name="output"/> on the device; null when there's none.</summary>
    private static bool? Change(string app, Func<ISimpleAudioVolume, bool> stream, Func<IEndpointVolume, bool> output)
    {
        IDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IDeviceEnumerator)new Microphone.MMDeviceEnumerator();
            if (app == Output)
                return WithOutput(enumerator, (_, volume) => (bool?)output(volume));
            bool? done = null;
            var owners = new Dictionary<int, StreamOwner?>();
            foreach (var (control, processId, system) in Sessions(enumerator))
            {
                try
                {
                    if (!owners.TryGetValue(processId, out var owner))
                        owners[processId] = owner = system ? StreamOwner.System : StreamOwner.Of(processId);
                    if (owner is not null && Names(app, owner.Key, owner.Name) && control is ISimpleAudioVolume volume)
                        done = stream(volume) && done != false;
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
            return done;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            return null;
        }
        finally
        {
            if (enumerator is not null)
                Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>Runs <paramref name="use"/> on the device Windows plays to and its volume; null when there's none.</summary>
    internal static T? WithOutput<T>(IDeviceEnumerator enumerator, Func<Microphone.IMMDevice, IEndpointVolume, T?> use)
    {
        if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device) < 0 || device is null)
            return default;
        try
        {
            var iid = typeof(IEndpointVolume).GUID;
            if (device.Activate(ref iid, ClsctxAll, 0, out var activated) < 0 || activated is not IEndpointVolume volume)
                return default;
            try
            {
                return use(device, volume);
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>
    /// Every sound stream on every playback device that's on: its control (which the caller releases), its process, and
    /// whether it's Windows' own sounds.
    /// </summary>
    internal static IEnumerable<(IAudioSessionControl2 Control, int ProcessId, bool System)> Sessions(IDeviceEnumerator enumerator)
    {
        if (enumerator.EnumAudioEndpoints(Render, ActiveDevices, out var devices) < 0 || devices is null)
            yield break;
        try
        {
            if (devices.GetCount(out var count) < 0)
                yield break;
            for (var i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) < 0 || device is null)
                    continue;
                try
                {
                    var iid = typeof(IAudioSessionManager2).GUID;
                    if (device.Activate(ref iid, ClsctxAll, 0, out var activated) < 0 || activated is not IAudioSessionManager2 manager)
                        continue;
                    try
                    {
                        if (manager.GetSessionEnumerator(out var sessions) < 0 || sessions is null)
                            continue;
                        try
                        {
                            if (sessions.GetCount(out var sessionCount) < 0)
                                continue;
                            for (var j = 0; j < sessionCount; j++)
                            {
                                if (sessions.GetSession(j, out var found) < 0 || found is not IAudioSessionControl2 control)
                                    continue;
                                var system = control.IsSystemSoundsSession() == 0;
                                if (control.GetProcessId(out var processId) < 0 || (processId == 0 && !system))
                                {
                                    Marshal.ReleaseComObject(control);
                                    continue;
                                }
                                yield return (control, processId, system);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(sessions);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(manager);
                    }
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
}

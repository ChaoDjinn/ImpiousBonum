using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Whether the microphone is muted, and which apps are using it or the camera: for a light that goes red while you're
/// live, or a reminder that you're muted. The microphone's metrics appear once there's a microphone, the camera's once an
/// app has used a camera. Nothing here uses either, so Windows never asks permission or shows its microphone or camera
/// icon for it. The apps using the microphone are the ones with a recording stream going, from the sessions Windows keeps
/// for every app's sound (see <see cref="AudioSessionReader"/>); those using the camera come from Windows' record of them
/// (see <see cref="ConsentStoreReader"/>).
/// </summary>
/// <param name="microphone">The microphone's name and mute (null when there's none); Windows unless a test swaps it.</param>
/// <param name="recording">Every app's recording streams, on every microphone; Windows' unless a test swaps it.</param>
/// <param name="camera">
/// The apps' records of using a camera (null when there are none); Windows' unless a test swaps it.
/// </param>
public sealed class PrivacyProvider(
    Func<MicrophoneState?>? microphone = null,
    Func<IReadOnlyList<AppSound>>? recording = null,
    Func<IReadOnlyList<ConsentEntry>?>? camera = null) : IMetricProvider
{
    public const string MicMuted = "mic.muted";
    public const string MicName = "mic.name";
    public const string MicInUse = "mic.inuse";
    public const string MicApps = "mic.apps";
    public const string CameraInUse = "camera.inuse";
    public const string CameraApps = "camera.apps";

    /// <summary>The camera's key in Windows' record, as Windows names it.</summary>
    public const string CameraKey = "webcam";

    private const string Category = "Microphone and camera";

    private readonly MicrophoneReader? _microphone = microphone is null ? new MicrophoneReader() : null;
    private readonly AudioSessionReader? _recording = recording is null ? new AudioSessionReader() : null;
    private readonly ConsentStoreReader? _cameraApps = camera is null ? new ConsentStoreReader($@"{ConsentStoreReader.Root}\{CameraKey}") : null;
    private bool _microphoneRegistered;
    private bool _cameraRegistered;

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        // The recording streams are looked for afresh every time, as that's how a newly started one is found: on the
        // microphones only, which takes a few milliseconds.
        Publish(store,
            microphone is null ? _microphone!.Read() : microphone(),
            recording is null ? _recording!.Read(null, lookAgain: true, SoundFlow.In) : recording(),
            camera is null ? _cameraApps!.Read() : camera());
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes one reading of the microphone, its streams and the camera's records, registering the metrics the first time there's something to show.</summary>
    internal void Publish(MetricStore store, MicrophoneState? mic, IReadOnlyList<AppSound> recording, IReadOnlyList<ConsentEntry>? cameraApps)
    {
        if (!_microphoneRegistered && (mic is not null || recording.Count > 0))
        {
            store.Register(new MetricDefinition(MicMuted, "Microphone muted (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(MicName, "Microphone", Category, MetricUnit.Text));
            store.Register(new MetricDefinition(MicInUse, "Microphone in use (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(MicApps, "Apps using the microphone", Category, MetricUnit.Text));
            _microphoneRegistered = true;
        }
        if (!_cameraRegistered && HasHistory(cameraApps))
        {
            store.Register(new MetricDefinition(CameraInUse, "Camera in use (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(CameraApps, "Apps using the camera", Category, MetricUnit.Text));
            _cameraRegistered = true;
        }

        // Setting a metric that isn't registered does nothing, so these are safe either way.
        store.Set(MicMuted, mic is null ? null : mic.Muted ? 1 : 0);
        store.SetText(MicName, mic?.Name);
        var apps = AppsRecording(recording);
        store.Set(MicInUse, apps is null ? 0 : 1);
        store.SetText(MicApps, apps);
        apps = AppsUsing(cameraApps);
        store.Set(CameraInUse, apps is null ? 0 : 1);
        store.SetText(CameraApps, apps);
    }

    private static bool HasHistory(IReadOnlyList<ConsentEntry>? entries) => entries?.Any(e => e.Start > 0) == true;

    /// <summary>
    /// The names of the apps recording now, e.g. "Discord, OBS", or null when none is: those with a stream from a
    /// microphone going, which is what Windows counts as using it. One that's open but stopped doesn't count.
    /// </summary>
    internal static string? AppsRecording(IReadOnlyList<AppSound> sounds) =>
        Names(sounds.Where(s => s.Flow == SoundFlow.In && s.Active).Select(s => ProgramName(s.App)));

    /// <summary>The names of the apps using it now by Windows' record, e.g. "Discord, OBS", or null when none is.</summary>
    internal static string? AppsUsing(IReadOnlyList<ConsentEntry>? entries) =>
        entries is null ? null : Names(entries.Where(e => e.InUse).Select(AppName));

    /// <summary>Each name once, in order and joined up; null when there are none.</summary>
    private static string? Names(IEnumerable<string> names)
    {
        var list = names.Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count > 0 ? string.Join(", ", list) : null;
    }

    /// <summary>
    /// A readable name for an app's record. A desktop app is its program's name (see <see cref="ProgramName"/>):
    /// "C:#Users#Sam#AppData#Local#Discord#app-1.0.9260#Discord.exe" → "Discord". A Store app is the end of its package
    /// family name: "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "WindowsCamera".
    /// </summary>
    internal static string AppName(ConsentEntry entry)
    {
        var name = entry.Key;
        if (!entry.Packaged)
            return ProgramName(name[(name.LastIndexOf('#') + 1)..]);

        // Family names end with the publisher's id after an underscore.
        var underscore = name.LastIndexOf('_');
        if (underscore > 0)
            name = name[..underscore];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1)
            name = name[(dot + 1)..];
        return Capitalise(name);
    }

    /// <summary>
    /// A program's name, nicely cased, from its file name with or without .exe: "Discord.exe" → "Discord", "obs64" →
    /// "OBS", "chrome" → "Chrome".
    /// </summary>
    internal static string ProgramName(string name)
    {
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        // obs64, ProjectZomboid64: the number is the 64-bit build's, not part of the app's name.
        if (name.Length > 2 && (name.EndsWith("64", StringComparison.Ordinal) || name.EndsWith("32", StringComparison.Ordinal)) && char.IsLetter(name[^3]))
            name = name[..^2];
        // A short all lower-case name is most likely letters, like obs or vlc.
        if (name.Length <= 3 && name.All(char.IsLower))
            return name.ToUpperInvariant();
        return Capitalise(name);
    }

    /// <summary>"discord" → "Discord"; a name with capitals of its own (MSTeams, iTunes) stays as it is.</summary>
    private static string Capitalise(string name) =>
        name.Length > 0 && name.All(c => !char.IsUpper(c)) ? char.ToUpperInvariant(name[0]) + name[1..] : name;

    public void Dispose()
    {
        _microphone?.Dispose();
        _recording?.Dispose();
        _cameraApps?.Dispose();
    }
}

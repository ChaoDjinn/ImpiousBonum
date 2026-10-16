using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Whether the microphone is muted, and which apps are using it or the camera: for a light that goes red while you're
/// live, or a reminder that you're muted. The microphone's metrics appear once there's a microphone or an app has used
/// one, the camera's once an app has used a camera. Nothing here uses either, so Windows never asks permission or shows
/// its microphone or camera icon for it (see <see cref="ConsentStoreReader"/>).
/// </summary>
/// <param name="microphone">The microphone's name and mute (null when there's none); Windows unless a test swaps it.</param>
/// <param name="consent">
/// The apps' records of using <see cref="MicrophoneKey"/> or <see cref="CameraKey"/> (null when there are none); Windows
/// unless a test swaps it.
/// </param>
public sealed class PrivacyProvider(Func<MicrophoneState?>? microphone = null, Func<string, IReadOnlyList<ConsentEntry>?>? consent = null) : IMetricProvider
{
    public const string MicMuted = "mic.muted";
    public const string MicName = "mic.name";
    public const string MicInUse = "mic.inuse";
    public const string MicApps = "mic.apps";
    public const string CameraInUse = "camera.inuse";
    public const string CameraApps = "camera.apps";

    /// <summary>The microphone's and camera's keys in Windows' record, as Windows names them.</summary>
    public const string MicrophoneKey = "microphone", CameraKey = "webcam";

    private const string Category = "Microphone and camera";

    private readonly MicrophoneReader? _microphone = microphone is null ? new MicrophoneReader() : null;
    private readonly ConsentStoreReader? _microphoneApps = consent is null ? new ConsentStoreReader($@"{ConsentStoreReader.Root}\{MicrophoneKey}") : null;
    private readonly ConsentStoreReader? _cameraApps = consent is null ? new ConsentStoreReader($@"{ConsentStoreReader.Root}\{CameraKey}") : null;
    private bool _microphoneRegistered;
    private bool _cameraRegistered;

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        Publish(store,
            microphone is null ? _microphone!.Read() : microphone(),
            consent is null ? _microphoneApps!.Read() : consent(MicrophoneKey),
            consent is null ? _cameraApps!.Read() : consent(CameraKey));
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes one reading of the microphone and the apps' records, registering the metrics the first time there's something to show.</summary>
    internal void Publish(MetricStore store, MicrophoneState? mic, IReadOnlyList<ConsentEntry>? micApps, IReadOnlyList<ConsentEntry>? cameraApps)
    {
        if (!_microphoneRegistered && (mic is not null || HasHistory(micApps)))
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
        var apps = AppsUsing(micApps);
        store.Set(MicInUse, apps is null ? 0 : 1);
        store.SetText(MicApps, apps);
        apps = AppsUsing(cameraApps);
        store.Set(CameraInUse, apps is null ? 0 : 1);
        store.SetText(CameraApps, apps);
    }

    private static bool HasHistory(IReadOnlyList<ConsentEntry>? entries) => entries?.Any(e => e.Start > 0) == true;

    /// <summary>The names of the apps using it now, e.g. "Discord, OBS", or null when none is.</summary>
    internal static string? AppsUsing(IReadOnlyList<ConsentEntry>? entries)
    {
        if (entries is null)
            return null;
        var names = entries.Where(e => e.InUse)
            .Select(AppName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>
    /// A readable name for an app's record. A desktop app is its program's name, nicely cased:
    /// "C:#Users#Sam#AppData#Local#Discord#app-1.0.9260#Discord.exe" → "Discord", "…#obs64.exe" → "OBS", "…#chrome.exe" →
    /// "Chrome". A Store app is the end of its package family name: "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "WindowsCamera".
    /// </summary>
    internal static string AppName(ConsentEntry entry)
    {
        var name = entry.Key;
        if (entry.Packaged)
        {
            // Family names end with the publisher's id after an underscore.
            var underscore = name.LastIndexOf('_');
            if (underscore > 0)
                name = name[..underscore];
            var dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1)
                name = name[(dot + 1)..];
            return Capitalise(name);
        }

        name = name[(name.LastIndexOf('#') + 1)..];
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
        _microphoneApps?.Dispose();
        _cameraApps?.Dispose();
    }
}

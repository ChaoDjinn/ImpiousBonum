using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The sound: the volume of the device Windows plays to (speakers or headphones) and whether it's muted, every second;
/// and, while a volume mixer is on screen, every app's volume, mute and whether it's playing (<see cref="Mixer"/>).
/// The apps are only read while a mixer asks (<see cref="Want"/>), so they cost nothing without one.
/// </summary>
public sealed class SoundProvider : IMetricProvider
{
    public const string Volume = "sound.volume";
    public const string Muted = "sound.muted";
    public const string Device = "sound.device";

    /// <summary>The device and the apps as a <see cref="MixerState"/>, while a mixer wants them; none otherwise.</summary>
    public const string Mixer = "sound.mixer";

    /// <summary>The playback devices that are on, as <see cref="SoundOutput"/>s, while something wants them; none otherwise.</summary>
    public const string Outputs = "sound.outputs";

    /// <summary>When a mixer last asked for the apps (<see cref="Want"/>).</summary>
    private const string WantedAt = "sound.mixer.wanted";

    /// <summary>When something last asked for the outputs (<see cref="WantOutputs"/>).</summary>
    private const string OutputsWantedAt = "sound.outputs.wanted";

    /// <summary>The outputs' full names seen since the app started, for a picker.</summary>
    private const string OutputsSeen = "sound.outputs.seen";

    /// <summary>The apps' names seen since the app started, for a mixer's picker.</summary>
    private const string Seen = "sound.apps.seen";

    private const string Category = "Sound";

    /// <summary>How long the apps are read for after a mixer last asked: long enough to outlast a missed refresh.</summary>
    internal static readonly TimeSpan WantedFor = TimeSpan.FromSeconds(5);

    /// <summary>How often the apps' streams are looked for again, as an app starts playing or closes.</summary>
    internal static readonly TimeSpan LookAgainEvery = TimeSpan.FromSeconds(3);

    private readonly Func<bool, IReadOnlyList<AppVolume>> _apps;
    private readonly Func<OutputVolume?> _output;
    private readonly Func<IReadOnlyList<SoundOutput>> _outputs;
    private readonly List<string> _outputsSeen = [];
    private readonly TimeProvider _time;
    private readonly AppVolumeReader? _reader;
    private readonly SortedSet<string> _seen = new(StringComparer.CurrentCultureIgnoreCase);
    private DateTimeOffset? _lookedAt;
    private bool _registered;

    /// <param name="apps">The apps' volumes, looked for again first when asked; Windows' unless a test swaps it.</param>
    /// <param name="output">The device Windows plays to; Windows' unless a test swaps it.</param>
    /// <param name="time">The clock; the system's unless a test swaps it.</param>
    /// <param name="outputs">The playback devices; Windows' unless a test swaps it.</param>
    public SoundProvider(Func<bool, IReadOnlyList<AppVolume>>? apps = null, Func<OutputVolume?>? output = null, TimeProvider? time = null,
        Func<IReadOnlyList<SoundOutput>>? outputs = null)
    {
        if (apps is null || output is null)
            _reader = new AppVolumeReader();
        _apps = apps ?? _reader!.ReadApps;
        _output = output ?? _reader!.ReadOutput;
        _outputs = outputs ?? SoundOutputs.List;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Asks for the outputs for the next few seconds: a sound output widget, or buttons that switch output, call it each refresh.</summary>
    public static void WantOutputs(MetricStore store, DateTimeOffset now) => store.SetAttachment(OutputsWantedAt, now);

    /// <summary>The outputs' full names seen since the app started, in the order first seen, for a picker.</summary>
    public static IReadOnlyList<string> OutputNames(MetricStore store) => store.GetAttachment(OutputsSeen) as IReadOnlyList<string> ?? [];

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <summary>Asks for the apps for the next few seconds: a mixer on screen calls it each time it's refreshed.</summary>
    public static void Want(MetricStore store, DateTimeOffset now) => store.SetAttachment(WantedAt, now);

    /// <summary>The apps' names seen since the app started, in name order, for a mixer's picker.</summary>
    public static IReadOnlyList<string> Names(MetricStore store) => store.GetAttachment(Seen) as IReadOnlyList<string> ?? [];

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        IReadOnlyList<AppVolume>? apps = null;
        if (store.GetAttachment(WantedAt) is DateTimeOffset asked && now - asked < WantedFor)
        {
            var lookAgain = _lookedAt is not { } looked || now - looked >= LookAgainEvery;
            if (lookAgain)
                _lookedAt = now;
            apps = _apps(lookAgain);
        }
        else
            // No mixer: look afresh when one comes back, rather than at streams that may have gone.
            _lookedAt = null;
        Publish(store, _output(), apps);
        PublishOutputs(store, store.GetAttachment(OutputsWantedAt) is DateTimeOffset wanted && now - wanted < WantedFor ? _outputs() : null);
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes the playback devices (null while nothing wants them), remembering their names for a picker.</summary>
    public void PublishOutputs(MetricStore store, IReadOnlyList<SoundOutput>? outputs)
    {
        store.SetAttachment(Outputs, outputs);
        var added = false;
        foreach (var output in outputs ?? [])
        {
            if (!_outputsSeen.Contains(output.FullName, StringComparer.OrdinalIgnoreCase))
            {
                _outputsSeen.Add(output.FullName);
                added = true;
            }
        }
        if (added)
            store.SetAttachment(OutputsSeen, _outputsSeen.ToList());
    }

    /// <summary>Writes the device's readings, and the mixer's state while there are <paramref name="apps"/> (null while no mixer wants them).</summary>
    public void Publish(MetricStore store, OutputVolume? output, IReadOnlyList<AppVolume>? apps)
    {
        if (!_registered && output is not null)
        {
            store.Register(new MetricDefinition(Volume, "Sound: volume", Category, MetricUnit.Percent, 100, Format: "0"));
            store.Register(new MetricDefinition(Muted, "Sound: muted (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(Device, "Sound: playing on", Category, MetricUnit.Text));
            _registered = true;
        }
        store.Set(Volume, output is null ? null : Math.Round(output.Volume * 100));
        store.Set(Muted, output is null ? null : output.Muted ? 1 : 0);
        store.SetText(Device, output?.Name);

        store.SetAttachment(Mixer, apps is null ? null : new MixerState(output, apps));
        if (apps is not null && apps.Any(app => _seen.Add(app.Name)))
            store.SetAttachment(Seen, _seen.ToList());
    }

    public void Dispose() => _reader?.Dispose();
}

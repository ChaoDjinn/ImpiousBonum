using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Discord's voice at one moment: whether you're in a call, whether anyone in it is talking, and whether your microphone
/// is picking you up. Talking and you stay on for a moment after the sound stops, so a light doesn't flicker between words.
/// </summary>
/// <param name="InCall">Discord has a microphone open, which it does in a voice channel or call.</param>
/// <param name="Talking">In a call, and sound is coming out of Discord: someone talking, or Discord's own sounds.</param>
/// <param name="You">In a call, and your microphone is picking up sound, whether or not Discord sends it (muted, or push to talk).</param>
/// <param name="Level">How loud Discord's sound is, from 0 (silence) to 1 (full scale).</param>
/// <param name="MicLevel">How loud your microphone is, as Discord hears it, from 0 to 1; 0 outside a call.</param>
public sealed record VoiceState(bool InCall, bool Talking, bool You, double Level, double MicLevel);

/// <summary>
/// Discord's voice chat, from the sound levels Windows meters for each app (the green bars in the Volume mixer): whether
/// you're in a call, whether someone's talking, and whether your microphone is picking you up. It reads the meters ten
/// times a second, for the Discord voice widget's lights, and writes the readings once a second like everything else.
/// Reading a meter opens nothing: the microphone's is there only while Discord has it open, and Windows doesn't count this
/// app as using it. Discord's readings appear the first time it's making or hearing sound, and are empty while it isn't
/// running.
/// </summary>
public sealed class VoiceChatProvider : IMetricProvider
{
    public const string InCall = "discord.call";
    public const string Talking = "discord.talking";
    public const string You = "discord.you";
    public const string Level = "discord.level";
    public const string MicLevel = "discord.mic.level";

    /// <summary>The latest <see cref="VoiceState"/>, ten times a second; none while Discord isn't making or hearing sound.</summary>
    public const string Live = "discord.voice";

    /// <summary>Discord's program names: the app, its test builds and its developer build.</summary>
    public static readonly IReadOnlySet<string> Apps =
        new HashSet<string>(["Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How loud counts as sound: 2% of full scale, about -34 dB, above a headset's hiss and below a quiet voice. Discord
    /// sends silence when no one's talking, so its own sound is either well under this or well over.
    /// </summary>
    internal const double Threshold = 0.02;

    /// <summary>How long talking and you stay on after the sound drops below <see cref="Threshold"/>.</summary>
    internal static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How often Discord's sound is looked for again: it starts its microphone's when you join a call, and a new process
    /// can take over from an old one. Looking means asking every sound device, so it isn't done every time.
    /// </summary>
    internal static readonly TimeSpan LookAgainEvery = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan WriteEvery = TimeSpan.FromSeconds(1);

    private const string Category = "Discord";

    private readonly Func<bool, IReadOnlyList<AppSound>> _read;
    private readonly TimeProvider _time;
    private readonly AudioSessionReader? _reader;
    private DateTimeOffset? _lookedAt;
    private DateTimeOffset? _wroteAt;
    private DateTimeOffset _heardAt = DateTimeOffset.MinValue;
    private DateTimeOffset _spokeAt = DateTimeOffset.MinValue;
    private bool _registered;

    // Since the readings were last written.
    private VoiceState? _latest;
    private bool _talked;
    private bool _spoke;
    private double _loudest;
    private double _micLoudest;

    /// <param name="read">
    /// Discord's sound streams, looked for again first when asked; Windows' meters unless a test swaps it.
    /// </param>
    /// <param name="time">The clock; the system's unless a test swaps it.</param>
    public VoiceChatProvider(Func<bool, IReadOnlyList<AppSound>>? read = null, TimeProvider? time = null)
    {
        if (read is null)
        {
            var reader = _reader = new AudioSessionReader();
            read = lookAgain => reader.Read(Apps, lookAgain);
        }
        _read = read;
        _time = time ?? TimeProvider.System;
    }

    public TimeSpan Interval => TimeSpan.FromMilliseconds(100);

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var lookAgain = _lookedAt is not { } looked || now - looked >= LookAgainEvery;
        if (lookAgain)
            _lookedAt = now;
        Publish(store, _read(lookAgain), now);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Works out Discord's voice from one reading of its sound streams and puts it in <see cref="Live"/>; once a second,
    /// writes the readings too, for the second gone (talking if anyone talked in it, the loudest it got).
    /// </summary>
    internal void Publish(MetricStore store, IReadOnlyList<AppSound> sounds, DateTimeOffset now)
    {
        var state = StateOf(sounds, now);
        store.SetAttachment(Live, state);
        if (state is not null)
        {
            _latest = state;
            _talked |= state.Talking;
            _spoke |= state.You;
            _loudest = Math.Max(_loudest, state.Level);
            _micLoudest = Math.Max(_micLoudest, state.MicLevel);
        }
        else
            _latest = null;

        if (_wroteAt is { } wrote && now - wrote < WriteEvery)
            return;
        _wroteAt = now;
        Write(store);
    }

    private VoiceState? StateOf(IReadOnlyList<AppSound> sounds, DateTimeOffset now)
    {
        if (sounds.Count == 0)
            return null;
        // Discord plays its sound through more than one process, and on more than one device when its notifications go
        // somewhere else: the loudest counts. Its microphone is open only while it's in a call.
        var level = sounds.Where(s => s.Flow == SoundFlow.Out).Select(s => s.Peak).DefaultIfEmpty().Max();
        var mics = sounds.Where(s => s.Flow == SoundFlow.In && s.Active).ToList();
        var inCall = mics.Count > 0;
        var mic = mics.Select(s => s.Peak).DefaultIfEmpty().Max();
        // A message's ping outside a call isn't anyone talking.
        if (inCall && level >= Threshold)
            _heardAt = now;
        if (inCall && mic >= Threshold)
            _spokeAt = now;
        return new VoiceState(inCall, inCall && now - _heardAt < Hold, inCall && now - _spokeAt < Hold, level, mic);
    }

    private void Write(MetricStore store)
    {
        if (!_registered && _latest is not null)
        {
            store.Register(new MetricDefinition(InCall, "In a Discord call (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(Talking, "Someone talking in Discord (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(You, "Your microphone picking up sound in Discord (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(Level, "Discord's sound level", Category, MetricUnit.Percent, 100));
            store.Register(new MetricDefinition(MicLevel, "Microphone level in Discord", Category, MetricUnit.Percent, 100));
            _registered = true;
        }

        // Setting a metric that isn't registered does nothing, so these are safe either way.
        var latest = _latest;
        store.Set(InCall, latest is null ? null : latest.InCall ? 1 : 0);
        store.Set(Talking, latest is null ? null : _talked ? 1 : 0);
        store.Set(You, latest is null ? null : _spoke ? 1 : 0);
        store.Set(Level, latest is null ? null : Math.Round(_loudest * 100, 1));
        store.Set(MicLevel, latest is null ? null : Math.Round(_micLoudest * 100, 1));
        _talked = _spoke = false;
        _loudest = _micLoudest = 0;
    }

    public void Dispose() => _reader?.Dispose();
}

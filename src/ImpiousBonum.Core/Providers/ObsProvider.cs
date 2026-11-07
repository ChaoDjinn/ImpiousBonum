using System.Globalization;
using System.Text.Json.Nodes;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Obs;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// OBS Studio, through its WebSocket server: whether it's recording, streaming or keeping a replay buffer, for how long,
/// the scene on air, and how the stream is going. Asked once a second in one go. The readings appear once OBS is set up
/// in settings, and have no reading while OBS can't be reached; <see cref="Status"/> says why.
/// </summary>
/// <param name="obs">The connection the OBS buttons share; null when there's none, as in a snapshot.</param>
/// <param name="time">The clock, for the stream's bitrate; the system's unless a test swaps it.</param>
public sealed class ObsProvider(ObsConnection? obs, TimeProvider? time = null) : IMetricProvider
{
    public const string Status = "obs.status";
    public const string Recording = "obs.recording";
    public const string RecordingTime = "obs.recording.time";
    public const string Streaming = "obs.streaming";
    public const string StreamingTime = "obs.streaming.time";
    public const string Replay = "obs.replay";
    public const string Scene = "obs.scene";
    public const string Fps = "obs.fps";
    public const string Dropped = "obs.dropped";
    public const string Bitrate = "obs.bitrate";

    private const string Category = "OBS";

    /// <summary>What's asked each second, in this order.</summary>
    internal static readonly IReadOnlyList<ObsCall> Calls =
    [
        new("GetRecordStatus"),
        new("GetStreamStatus"),
        new("GetReplayBufferStatus"),
        new("GetCurrentProgramScene"),
        new("GetStats"),
    ];

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private bool _registered;
    private (DateTimeOffset At, double Bytes)? _streamed;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        if (obs is null)
            return;
        var results = await obs.SendAsync(Calls, cancellationToken).ConfigureAwait(false);
        Publish(store, obs.State, results);
    }

    /// <summary>
    /// Writes one round of OBS's answers to <see cref="Calls"/> (null when it couldn't be asked), registering the
    /// readings once OBS is set up.
    /// </summary>
    public void Publish(MetricStore store, ObsState state, IReadOnlyList<ObsResult>? results)
    {
        if (state == ObsState.Off && !_registered)
            return;
        if (!_registered)
        {
            store.Register(new MetricDefinition(Status, "OBS: connection", Category, MetricUnit.Text));
            store.Register(new MetricDefinition(Recording, "OBS: recording (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(RecordingTime, "OBS: how long it's been recording", Category, MetricUnit.Text));
            store.Register(new MetricDefinition(Streaming, "OBS: streaming (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(StreamingTime, "OBS: how long it's been streaming", Category, MetricUnit.Text));
            store.Register(new MetricDefinition(Replay, "OBS: replay buffer on (1) or not (0)", Category, MetricUnit.None, 1, Format: "0"));
            store.Register(new MetricDefinition(Scene, "OBS: the scene on air", Category, MetricUnit.Text));
            store.Register(new MetricDefinition(Fps, "OBS: frames a second", Category, MetricUnit.FramesPerSecond, Format: "0"));
            store.Register(new MetricDefinition(Dropped, "OBS: stream frames dropped", Category, MetricUnit.Percent, 100));
            store.Register(new MetricDefinition(Bitrate, "OBS: stream bitrate", Category, MetricUnit.None, Format: "0' kbps'"));
            _registered = true;
        }

        store.SetText(Status, StatusText(state));
        var record = Ok(results, 0);
        var stream = Ok(results, 1);
        var recording = Bool(record, "outputActive");
        var streaming = Bool(stream, "outputActive");
        store.Set(Recording, recording is null ? null : recording.Value ? 1 : 0);
        store.SetText(RecordingTime, recording == true ? Time(record, Bool(record, "outputPaused") == true) : null);
        store.Set(Streaming, streaming is null ? null : streaming.Value ? 1 : 0);
        store.SetText(StreamingTime, streaming == true ? Time(stream, paused: false) : null);
        // No replay buffer answer means it isn't set up in OBS's output settings: off, rather than unknown.
        store.Set(Replay, results is null ? null : Bool(Ok(results, 2), "outputActive") == true ? 1 : 0);
        var scene = Ok(results, 3);
        store.SetText(Scene, (string?)scene?["currentProgramSceneName"] ?? (string?)scene?["sceneName"]);
        store.Set(Fps, Number(Ok(results, 4), "activeFps") is { } fps ? Math.Round(fps, 1) : null);

        var skipped = Number(stream, "outputSkippedFrames");
        var total = Number(stream, "outputTotalFrames");
        store.Set(Dropped, streaming == true && skipped is { } s && total is { } t && t > 0 ? Math.Round(s / t * 100, 1) : null);
        store.Set(Bitrate, streaming == true ? Kbps(Number(stream, "outputBytes")) : Forget());
    }

    /// <summary>The stream's bitrate since the last answer, in kilobits a second; null on the first.</summary>
    private double? Kbps(double? bytes)
    {
        var now = _time.GetUtcNow();
        var last = _streamed;
        _streamed = bytes is { } b ? (now, b) : null;
        if (last is not { } previous || bytes is not { } current || current < previous.Bytes)
            return null;
        var seconds = (now - previous.At).TotalSeconds;
        return seconds > 0 ? Math.Round((current - previous.Bytes) * 8 / 1000 / seconds) : null;
    }

    private double? Forget()
    {
        _streamed = null;
        return null;
    }

    /// <summary>What <see cref="Status"/> says for each state.</summary>
    public static string StatusText(ObsState state) => state switch
    {
        ObsState.Connected => "Connected",
        ObsState.NotRunning => "OBS isn't running",
        ObsState.WrongPassword => "OBS turned the password away: copy it from OBS's WebSocket Server Settings",
        ObsState.Unreachable => "Can't reach OBS: turn on its WebSocket server, or check the address",
        _ => "Off",
    };

    /// <summary>
    /// How long it's been going, from OBS's timecode (<c>01:02:03.456</c>): <c>1:02:03</c>, or <c>2:03</c> under an hour,
    /// with <c>(paused)</c> after it while paused.
    /// </summary>
    internal static string? Time(JsonObject? status, bool paused)
    {
        // Hours, minutes and seconds, the hours going past 24 for a long stream.
        var parts = ((string?)status?["outputTimecode"])?.Split(':');
        if (parts is not [var h, var m, var s]
            || !int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
            return null;
        var whole = (int)seconds;
        var text = hours > 0 ? $"{hours}:{minutes:00}:{whole:00}" : $"{minutes}:{whole:00}";
        return paused ? $"{text} (paused)" : text;
    }

    private static JsonObject? Ok(IReadOnlyList<ObsResult>? results, int index) =>
        results is not null && index < results.Count && results[index].Ok ? results[index].Data : null;

    private static bool? Bool(JsonObject? data, string name) =>
        data?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>A number, whether OBS sent it as a whole number or not.</summary>
    private static double? Number(JsonObject? data, string name) =>
        data?[name] is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number
            && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    public void Dispose()
    {
    }
}

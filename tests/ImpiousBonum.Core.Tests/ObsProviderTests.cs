using System.Text.Json.Nodes;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Obs;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class ObsProviderTests
{
    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    /// <summary>OBS's answers to <see cref="ObsProvider.Calls"/>: recording, streaming, the replay buffer, the scene and its stats.</summary>
    private static List<ObsResult> Answers(JsonObject? record = null, JsonObject? stream = null, bool? replay = false, string scene = "Gaming", double fps = 60) =>
    [
        new(true, record ?? new JsonObject { ["outputActive"] = false, ["outputPaused"] = false, ["outputTimecode"] = "00:00:00.000" }),
        new(true, stream ?? new JsonObject { ["outputActive"] = false, ["outputTimecode"] = "00:00:00.000", ["outputBytes"] = 0, ["outputSkippedFrames"] = 0, ["outputTotalFrames"] = 0 }),
        replay is { } on ? new(true, new JsonObject { ["outputActive"] = on }) : new(false, null),
        new(true, new JsonObject { ["currentProgramSceneName"] = scene }),
        new(true, new JsonObject { ["activeFps"] = fps, ["cpuUsage"] = 3.2 }),
    ];

    [Fact]
    public void Nothing_shows_until_OBS_is_set_up()
    {
        var store = new MetricStore();
        using var provider = new ObsProvider(null);

        provider.Publish(store, ObsState.Off, null);

        Assert.False(store.TryGet(ObsProvider.Status, out _));
    }

    [Fact]
    public void While_OBS_can_t_be_reached_the_readings_are_empty_and_the_status_says_why()
    {
        var store = new MetricStore();
        using var provider = new ObsProvider(null);

        provider.Publish(store, ObsState.NotRunning, null);
        Assert.Equal("OBS isn't running", Text(store, ObsProvider.Status));
        Assert.Null(Value(store, ObsProvider.Recording));
        Assert.Null(Value(store, ObsProvider.Replay));
        Assert.Null(Text(store, ObsProvider.Scene));

        provider.Publish(store, ObsState.WrongPassword, null);
        Assert.StartsWith("OBS turned the password away", Text(store, ObsProvider.Status));
    }

    [Fact]
    public void Recording_and_the_scene_and_frame_rate_come_from_OBS_s_answers()
    {
        var store = new MetricStore();
        using var provider = new ObsProvider(null);

        provider.Publish(store, ObsState.Connected, Answers(
            record: new JsonObject { ["outputActive"] = true, ["outputPaused"] = false, ["outputTimecode"] = "00:12:05.250" },
            replay: true, scene: "Just chatting", fps: 59.94));

        Assert.Equal("Connected", Text(store, ObsProvider.Status));
        Assert.Equal(1, Value(store, ObsProvider.Recording));
        Assert.Equal("12:05", Text(store, ObsProvider.RecordingTime));
        Assert.Equal(0, Value(store, ObsProvider.Streaming));
        Assert.Null(Text(store, ObsProvider.StreamingTime));
        Assert.Equal(1, Value(store, ObsProvider.Replay));
        Assert.Equal("Just chatting", Text(store, ObsProvider.Scene));
        Assert.Equal(59.9, Value(store, ObsProvider.Fps));
    }

    [Theory]
    [InlineData("00:00:07.900", false, "0:07")]
    [InlineData("00:59:59.999", false, "59:59")]
    [InlineData("01:02:03.000", false, "1:02:03")]
    [InlineData("26:00:00.000", true, "26:00:00 (paused)")]
    public void How_long_it_s_been_going_is_short(string timecode, bool paused, string expected) =>
        Assert.Equal(expected, ObsProvider.Time(new JsonObject { ["outputTimecode"] = timecode }, paused));

    [Fact]
    public void Without_a_replay_buffer_set_up_it_reads_as_off()
    {
        var store = new MetricStore();
        using var provider = new ObsProvider(null);

        provider.Publish(store, ObsState.Connected, Answers(replay: null));

        Assert.Equal(0, Value(store, ObsProvider.Replay));
    }

    [Fact]
    public void A_stream_s_dropped_frames_and_bitrate_show_while_it_s_live()
    {
        var store = new MetricStore();
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        using var provider = new ObsProvider(null, time);
        JsonObject Stream(long bytes, int skipped, int total) => new()
        {
            ["outputActive"] = true, ["outputTimecode"] = "01:30:00.000", ["outputBytes"] = bytes,
            ["outputSkippedFrames"] = skipped, ["outputTotalFrames"] = total,
        };

        provider.Publish(store, ObsState.Connected, Answers(stream: Stream(1_000_000, 0, 1000)));
        Assert.Equal(1, Value(store, ObsProvider.Streaming));
        Assert.Equal("1:30:00", Text(store, ObsProvider.StreamingTime));
        Assert.Equal(0, Value(store, ObsProvider.Dropped));
        Assert.Null(Value(store, ObsProvider.Bitrate));

        // A second later: 750,000 bytes more is 6,000 kbps, and 15 of 2,000 frames dropped.
        time.Now += TimeSpan.FromSeconds(1);
        provider.Publish(store, ObsState.Connected, Answers(stream: Stream(1_750_000, 15, 2000)));
        Assert.Equal(6000, Value(store, ObsProvider.Bitrate));
        Assert.Equal(0.8, Value(store, ObsProvider.Dropped));
        Assert.Equal("6000 kbps", MetricFormatter.Format(store.TryGet(ObsProvider.Bitrate, out var bitrate) ? bitrate : default));

        // The stream ends: no bitrate, and a new one starts counting afresh.
        provider.Publish(store, ObsState.Connected, Answers());
        Assert.Null(Value(store, ObsProvider.Bitrate));
        Assert.Null(Value(store, ObsProvider.Dropped));
    }

    [Fact]
    public async Task Each_second_it_asks_OBS_everything_in_one_go()
    {
        await using var obs = new FakeObs();
        obs.Answer = (type, _) => type switch
        {
            "GetRecordStatus" => (true, new JsonObject { ["outputActive"] = true, ["outputPaused"] = true, ["outputTimecode"] = "00:00:42.000" }),
            "GetCurrentProgramScene" => (true, new JsonObject { ["sceneName"] = "BRB" }),
            _ => (false, null),
        };
        await using var connection = new ObsConnection(() => obs.Request(null), () => true);
        using var provider = new ObsProvider(connection);
        var store = new MetricStore();

        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal(ObsProvider.Calls.Select(c => c.Type), obs.Requests.Select(r => r.Type));
        Assert.Equal("0:42 (paused)", Text(store, ObsProvider.RecordingTime));
        Assert.Equal("BRB", Text(store, ObsProvider.Scene));
        Assert.Null(Value(store, ObsProvider.Streaming));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

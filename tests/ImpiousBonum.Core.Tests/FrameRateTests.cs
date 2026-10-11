using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Core.Tests;

public sealed class FrameRateTests
{
    private const long TicksPerSecond = 1000;

    private static PresentRateTracker Tracker() =>
        new(TicksPerSecond, window: TimeSpan.FromSeconds(2), staleAfter: TimeSpan.FromSeconds(5));

    private static void Record60Fps(PresentRateTracker tracker, int processId, long start, int frames,
        PresentSource source = PresentSource.Runtime) =>
        RecordFps(tracker, processId, start, frames, 60, source);

    private static void RecordFps(PresentRateTracker tracker, int processId, long start, int frames, double fps,
        PresentSource source = PresentSource.Runtime)
    {
        for (var i = 0; i < frames; i++)
            tracker.Record(processId, start + (long)(i * 1000.0 / fps), source);
    }

    [Fact]
    public void Rate_is_frames_over_the_time_they_span()
    {
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 10_000, frames: 61);

        var (processId, fps) = Assert.Single(tracker.Snapshot(now: 11_000));
        Assert.Equal(42, processId);
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void Events_delivered_late_still_count()
    {
        // ETW handed over this second of frames 3 seconds after they happened.
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 10_000, frames: 61);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 14_000));
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void Only_the_latest_window_is_measured()
    {
        var tracker = Tracker();
        // A slow burst long ago, then a steady 60 FPS for the last 2 seconds.
        tracker.Record(42, 0);
        tracker.Record(42, 500);
        Record60Fps(tracker, 42, start: 8_000, frames: 121);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 10_000));
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void Silent_processes_are_forgotten()
    {
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 0, frames: 61);

        Assert.Empty(tracker.Snapshot(now: 7_000));
    }

    [Fact]
    public void A_single_present_has_no_rate_yet()
    {
        var tracker = Tracker();
        tracker.Record(1, 1_000);

        Assert.Empty(tracker.Snapshot(now: 1_500));
    }

    [Fact]
    public void Kernel_presents_count_for_apps_the_runtime_doesnt_see()
    {
        // A Vulkan or OpenGL game: only the graphics kernel reports its frames.
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 10_000, frames: 61, PresentSource.KernelPresent);

        var (processId, fps) = Assert.Single(tracker.Snapshot(now: 11_000));
        Assert.Equal(42, processId);
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void A_DirectX_game_seen_by_runtime_and_kernel_is_not_double_counted()
    {
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 10_000, frames: 61);
        Record60Fps(tracker, 42, start: 10_000, frames: 61, PresentSource.KernelPresent);
        Record60Fps(tracker, 42, start: 10_000, frames: 61, PresentSource.KernelPresentHistory);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 11_000));
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void The_runtime_rate_wins_over_the_kernel_rate()
    {
        // DirectX games report the same numbers as before kernel events were counted.
        var tracker = Tracker();
        RecordFps(tracker, 42, start: 10_000, frames: 61, fps: 60);
        RecordFps(tracker, 42, start: 10_000, frames: 145, fps: 144, PresentSource.KernelPresentHistory);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 11_000));
        Assert.Equal(60, fps, precision: 0);
    }

    [Fact]
    public void The_busiest_kernel_source_is_used_when_there_is_no_runtime()
    {
        // Not every present path raises every kernel event, so a quieter one is missing frames, not a slower app.
        var tracker = Tracker();
        RecordFps(tracker, 42, start: 10_000, frames: 11, fps: 10, PresentSource.KernelPresent);
        RecordFps(tracker, 42, start: 10_000, frames: 145, fps: 144, PresentSource.KernelPresentHistory);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 11_000));
        Assert.Equal(144, fps, precision: 0);
    }

    [Fact]
    public void Kernel_rate_takes_over_once_the_runtime_goes_quiet()
    {
        var tracker = Tracker();
        Record60Fps(tracker, 42, start: 0, frames: 61);
        Record60Fps(tracker, 42, start: 6_000, frames: 61, PresentSource.KernelPresent);

        var (_, fps) = Assert.Single(tracker.Snapshot(now: 7_000));
        Assert.Equal(60, fps, precision: 0);
    }

    /// <summary>Records frames at the given times (in ms, which is the test clock's tick).</summary>
    private static void RecordAt(PresentRateTracker tracker, IEnumerable<long> times)
    {
        foreach (var time in times)
            tracker.Record(42, time);
    }

    /// <summary>Frame times at 60 fps, with every <paramref name="stutterEvery"/>th frame taking <paramref name="stutterMs"/> instead.</summary>
    private static IEnumerable<long> Frames(int count, long start = 0, int stutterEvery = 0, int stutterMs = 0)
    {
        var time = (double)start;
        for (var i = 0; i < count; i++)
        {
            yield return (long)time;
            time += stutterEvery > 0 && i % stutterEvery == stutterEvery - 1 ? stutterMs : 1000.0 / 60;
        }
    }

    [Fact]
    public void Steady_frames_have_even_frame_times_and_lows_near_the_rate()
    {
        var tracker = Tracker();
        RecordAt(tracker, Frames(1201));

        var stats = Assert.Single(tracker.Snapshot(now: 20_000));
        Assert.Equal(16.7, stats.FrameTime, precision: 1);
        Assert.InRange(stats.MaxFrameTime, 16, 17);
        Assert.InRange(stats.OnePercentLow!.Value, 58, 61);
        Assert.InRange(stats.PointOnePercentLow!.Value, 58, 61);
    }

    [Fact]
    public void Stutters_pull_the_lows_down_and_show_as_the_slowest_frame()
    {
        // A 100 ms hitch every 50 frames: 2% of frames, so the slowest 1% are all hitches.
        var tracker = Tracker();
        var times = Frames(1200, stutterEvery: 50, stutterMs: 100).ToList();
        RecordAt(tracker, times);

        var stats = Assert.Single(tracker.Snapshot(now: times[^1]));
        Assert.Equal(10, stats.OnePercentLow!.Value, precision: 0);
        Assert.Equal(10, stats.PointOnePercentLow!.Value, precision: 0);
        Assert.Equal(100, stats.MaxFrameTime, precision: 0);
    }

    [Fact]
    public void Only_the_last_second_counts_for_the_slowest_frame()
    {
        var tracker = Tracker();
        var times = Frames(120).ToList();
        times = [.. times, times[^1] + 250, .. Frames(120, start: times[^1] + 250)];
        RecordAt(tracker, times);

        // The 250 ms hitch was two seconds before the latest frame.
        Assert.InRange(Assert.Single(tracker.Snapshot(now: times[^1])).MaxFrameTime, 16, 17);
    }

    [Fact]
    public void Pauses_longer_than_a_second_are_not_slow_frames()
    {
        // Alt-tabbed for four seconds between two bursts of steady frames.
        var tracker = Tracker();
        var first = Frames(600).ToList();
        RecordAt(tracker, [.. first, .. Frames(600, start: first[^1] + 4000)]);

        var stats = Assert.Single(tracker.Snapshot(now: first[^1] + 4000 + 10_000));
        Assert.InRange(stats.OnePercentLow!.Value, 58, 61);
    }

    [Fact]
    public void Lows_wait_for_enough_frames()
    {
        var tracker = Tracker();
        RecordAt(tracker, Frames(150));

        var stats = Assert.Single(tracker.Snapshot(now: 2_500));
        Assert.NotNull(stats.OnePercentLow);
        Assert.Null(stats.PointOnePercentLow);
    }

    [Fact]
    public void Frame_times_cross_the_pipe_and_older_hosts_send_none()
    {
        var message = new SensorMessage { Type = SensorMessage.ValuesType, Values = new Dictionary<string, double?>(), Presenters = [new(42, "game", 143.9, 6.95, 21.3, 98.2, 61.4)] };
        var back = Assert.Single(SensorProtocol.Deserialize(SensorProtocol.Serialize(message))!.Presenters!);
        Assert.Equal(new PresenterInfo(42, "game", 143.9, 6.95, 21.3, 98.2, 61.4), back);

        var old = SensorProtocol.Deserialize("""{"type":"values","values":{},"presenters":[{"processId":42,"name":"game","fps":60}]}""");
        var presenter = Assert.Single(old!.Presenters!);
        Assert.Equal(60, presenter.Fps);
        Assert.Null(presenter.FrameTime);
        Assert.Null(presenter.Low1);
    }

    [Fact]
    public void Foreground_prefers_the_exact_process()
    {
        PresenterInfo[] presenters = [new(10, "claude", 30), new(11, "claude", 59), new(20, "game", 144)];
        Assert.Equal(10, PresenterInfo.ForForeground(presenters, 10, "claude")!.ProcessId);
    }

    [Fact]
    public void Foreground_falls_back_to_a_sibling_process_with_the_same_exe()
    {
        // Chromium/Electron: the window belongs to the browser process, frames come from its GPU process.
        PresenterInfo[] presenters = [new(11, "claude", 59), new(12, "Claude", 20), new(20, "game", 144)];
        Assert.Equal(11, PresenterInfo.ForForeground(presenters, 5, "claude")!.ProcessId);
    }

    [Fact]
    public void Foreground_without_presents_has_no_rate()
    {
        PresenterInfo[] presenters = [new(20, "game", 144)];
        Assert.Null(PresenterInfo.ForForeground(presenters, 5, "explorer"));
    }
}

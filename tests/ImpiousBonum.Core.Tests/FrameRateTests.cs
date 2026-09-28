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

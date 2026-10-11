namespace ImpiousBonum.Core.Sensors;

/// <summary>Where a "frame presented" event came from.</summary>
public enum PresentSource : byte
{
    /// <summary>The graphics runtime (DXGI or D3D9). Preferred whenever a process has it.</summary>
    Runtime,

    /// <summary>The graphics kernel finishing a present (DxgKrnl Present_Info). Covers Vulkan, OpenGL and older blit presents.</summary>
    KernelPresent,

    /// <summary>The graphics kernel queueing a frame for the compositor (DxgKrnl PresentHistory_Start). Covers flip-model presents.</summary>
    KernelPresentHistory,
}

/// <summary>
/// One app's frame pacing. <see cref="FrameTime"/> and <see cref="MaxFrameTime"/> are in milliseconds; the lows are
/// frame rates, null until there are enough frames to tell (100 for the 1% low, 1,000 for the 0.1% low).
/// </summary>
/// <param name="MaxFrameTime">The slowest frame in the last second: a spike on a graph of it is a stutter.</param>
/// <param name="OnePercentLow">The frame rate of the slowest 1% of frames over the last 30 seconds (from the average of their frame times).</param>
/// <param name="PointOnePercentLow">The same for the slowest 0.1%.</param>
public sealed record PresentStats(int ProcessId, double Fps, double FrameTime, double MaxFrameTime, double? OnePercentLow, double? PointOnePercentLow)
{
    public void Deconstruct(out int processId, out double fps)
    {
        processId = ProcessId;
        fps = Fps;
    }
}

/// <summary>
/// Turns a stream of per-process "frame presented" timestamps into frames per second, frame times and lows.
/// ETW delivers real-time events in batches, sometimes seconds late, so each process's rate is measured over the
/// window ending at its own latest frame rather than at "now". A process is only forgotten once it has been silent
/// for <paramref name="staleAfter"/>, which is what stops a delayed batch from looking like the app stopped drawing.
/// <para>
/// The same frame can be reported by several sources (a DirectX game shows up in the runtime and in the kernel).
/// Each source is counted separately and never added together: the runtime's rate wins when there is one, and
/// otherwise the highest kernel rate, since not every present path raises every kernel event.
/// </para>
/// <para>
/// The lows leave out gaps longer than a second (alt-tabbed, a loading screen), which are pauses rather than slow frames.
/// </para>
/// </summary>
/// <param name="lowsWindow">How far back the 1% and 0.1% lows look; 30 seconds unless given.</param>
public sealed class PresentRateTracker(long ticksPerSecond, TimeSpan window, TimeSpan staleAfter, TimeSpan? lowsWindow = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(int ProcessId, PresentSource Source), Queue<long>> _presents = [];
    private readonly long _windowTicks = (long)(window.TotalSeconds * ticksPerSecond);
    private readonly long _staleTicks = (long)(staleAfter.TotalSeconds * ticksPerSecond);
    private readonly long _keepTicks = (long)(Math.Max(window.TotalSeconds, (lowsWindow ?? TimeSpan.FromSeconds(30)).TotalSeconds) * ticksPerSecond);

    public void Record(int processId, long timestamp, PresentSource source = PresentSource.Runtime)
    {
        lock (_gate)
        {
            if (!_presents.TryGetValue((processId, source), out var queue))
                _presents[(processId, source)] = queue = new Queue<long>();
            queue.Enqueue(timestamp);
        }
    }

    /// <summary>Rates for every process that presented recently. <paramref name="now"/> is on the same clock as the recorded timestamps.</summary>
    public List<PresentStats> Snapshot(long now)
    {
        var best = new Dictionary<int, (PresentSource Source, double Fps, Queue<long> Queue)>();

        lock (_gate)
        {
            foreach (var (key, queue) in _presents.ToList())
            {
                var latest = queue.Last();
                if (latest < now - _staleTicks)
                {
                    _presents.Remove(key);
                    continue;
                }

                // Keep enough for the lows; the rate only looks at the last window.
                while (queue.Peek() < latest - _keepTicks)
                    queue.Dequeue();

                var cutoff = latest - _windowTicks;
                var first = queue.FirstOrDefault(t => t >= cutoff);
                var count = queue.Count(t => t >= cutoff);
                if (count < 2)
                    continue;

                // n frames span n-1 intervals.
                var span = (double)(latest - first) / ticksPerSecond;
                if (span <= 0)
                    continue;

                var fps = (count - 1) / span;
                if (!best.TryGetValue(key.ProcessId, out var current) || Beats(key.Source, fps, (current.Source, current.Fps)))
                    best[key.ProcessId] = (key.Source, fps, queue);
            }

            return best.Select(p => Stats(p.Key, p.Value.Fps, p.Value.Queue)).ToList();
        }
    }

    private PresentStats Stats(int processId, double fps, Queue<long> queue)
    {
        var times = queue.ToArray();
        var latest = times[^1];
        long slowestRecent = 0;
        var intervals = new List<long>(times.Length);
        for (var i = 1; i < times.Length; i++)
        {
            var interval = times[i] - times[i - 1];
            if (interval <= 0)
                continue;
            if (times[i] > latest - ticksPerSecond)
                slowestRecent = Math.Max(slowestRecent, interval);
            if (interval <= ticksPerSecond)
                intervals.Add(interval);
        }

        // Slowest first, for both lows.
        intervals.Sort((a, b) => b.CompareTo(a));
        return new PresentStats(processId, fps, 1000 / fps, slowestRecent * 1000.0 / ticksPerSecond, Low(intervals, 100), Low(intervals, 1000));
    }

    /// <summary>The frame rate of the slowest 1/<paramref name="fraction"/> of frames, or null without that many frames.</summary>
    private double? Low(List<long> slowestFirst, int fraction)
    {
        var take = slowestFirst.Count / fraction;
        if (take < 1)
            return null;
        return ticksPerSecond / slowestFirst.Take(take).Average();
    }

    private static bool Beats(PresentSource source, double fps, (PresentSource Source, double Fps) current)
    {
        if (current.Source == PresentSource.Runtime)
            return false;
        return source == PresentSource.Runtime || fps > current.Fps;
    }

    public void Clear()
    {
        lock (_gate)
            _presents.Clear();
    }
}

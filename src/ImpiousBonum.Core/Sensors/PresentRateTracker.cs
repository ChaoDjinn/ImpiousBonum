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
/// Turns a stream of per-process "frame presented" timestamps into frames per second.
/// ETW delivers real-time events in batches, sometimes seconds late, so each process's rate is measured over the
/// window ending at its own latest frame rather than at "now". A process is only forgotten once it has been silent
/// for <paramref name="staleAfter"/>, which is what stops a delayed batch from looking like the app stopped drawing.
/// <para>
/// The same frame can be reported by several sources (a DirectX game shows up in the runtime and in the kernel).
/// Each source is counted separately and never added together: the runtime's rate wins when there is one, and
/// otherwise the highest kernel rate, since not every present path raises every kernel event.
/// </para>
/// </summary>
public sealed class PresentRateTracker(long ticksPerSecond, TimeSpan window, TimeSpan staleAfter)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(int ProcessId, PresentSource Source), Queue<long>> _presents = [];
    private readonly long _windowTicks = (long)(window.TotalSeconds * ticksPerSecond);
    private readonly long _staleTicks = (long)(staleAfter.TotalSeconds * ticksPerSecond);

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
    public List<(int ProcessId, double Fps)> Snapshot(long now)
    {
        var best = new Dictionary<int, (PresentSource Source, double Fps)>();

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

                var cutoff = latest - _windowTicks;
                while (queue.Peek() < cutoff)
                    queue.Dequeue();

                if (queue.Count < 2)
                    continue;

                // n frames span n-1 intervals.
                var span = (double)(latest - queue.Peek()) / ticksPerSecond;
                if (span <= 0)
                    continue;

                var fps = (queue.Count - 1) / span;
                if (!best.TryGetValue(key.ProcessId, out var current) || Beats(key.Source, fps, current))
                    best[key.ProcessId] = (key.Source, fps);
            }
        }

        return best.Select(p => (p.Key, p.Value.Fps)).ToList();
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

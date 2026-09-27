namespace ImpiousBonum.Core.Sensors;

/// <summary>
/// Turns a stream of per-process "frame presented" timestamps into frames per second.
/// ETW delivers real-time events in batches, sometimes seconds late, so each process's rate is measured over the
/// window ending at its own latest frame rather than at "now". A process is only forgotten once it has been silent
/// for <paramref name="staleAfter"/>, which is what stops a delayed batch from looking like the app stopped drawing.
/// </summary>
public sealed class PresentRateTracker(long ticksPerSecond, TimeSpan window, TimeSpan staleAfter)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Queue<long>> _presents = [];
    private readonly long _windowTicks = (long)(window.TotalSeconds * ticksPerSecond);
    private readonly long _staleTicks = (long)(staleAfter.TotalSeconds * ticksPerSecond);

    public void Record(int processId, long timestamp)
    {
        lock (_gate)
        {
            if (!_presents.TryGetValue(processId, out var queue))
                _presents[processId] = queue = new Queue<long>();
            queue.Enqueue(timestamp);
        }
    }

    /// <summary>Rates for every process that presented recently. <paramref name="now"/> is on the same clock as the recorded timestamps.</summary>
    public List<(int ProcessId, double Fps)> Snapshot(long now)
    {
        var rates = new List<(int, double)>();

        lock (_gate)
        {
            foreach (var (processId, queue) in _presents.ToList())
            {
                var latest = queue.Last();
                if (latest < now - _staleTicks)
                {
                    _presents.Remove(processId);
                    continue;
                }

                var cutoff = latest - _windowTicks;
                while (queue.Peek() < cutoff)
                    queue.Dequeue();

                if (queue.Count < 2)
                    continue;

                // n frames span n-1 intervals.
                var span = (double)(latest - queue.Peek()) / ticksPerSecond;
                if (span > 0)
                    rates.Add((processId, (queue.Count - 1) / span));
            }
        }

        return rates;
    }

    public void Clear()
    {
        lock (_gate)
            _presents.Clear();
    }
}

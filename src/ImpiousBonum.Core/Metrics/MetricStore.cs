namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// Thread-safe home for every metric definition and its latest value plus a short history.
/// Providers write from background threads; the UI reads on its own tick.
/// </summary>
/// <remarks>
/// Every metric keeps its last <see cref="DefaultHistoryLength"/> readings. Longer, averaged history
/// (<see cref="GetHistory(string, LongSpan)"/>) is kept only for the metrics a graph has asked it of, from when it first
/// asked, so the metrics no graph shows that way cost nothing more.
/// </remarks>
public sealed class MetricStore
{
    public const int DefaultHistoryLength = 240;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _attachments = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _historyLength;
    private readonly TimeProvider _time;

    /// <param name="time">The clock averaged history follows; the system's unless a test swaps it.</param>
    public MetricStore(int historyLength = DefaultHistoryLength, TimeProvider? time = null)
    {
        _historyLength = historyLength;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Registers (or re-describes) a metric. Existing values and history are kept.</summary>
    public void Register(MetricDefinition definition)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(definition.Id, out var entry))
                entry.Definition = definition;
            else
                _entries[definition.Id] = new Entry(definition, _historyLength);
        }
    }

    public void Unregister(string id)
    {
        lock (_gate)
            _entries.Remove(id);
    }

    public void Set(string id, double? value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry))
                return;

            entry.Value = value;
            entry.History.Add(value ?? double.NaN);
            if (entry.Tiers is { } tiers && value is { } reading && double.IsFinite(reading))
            {
                var now = _time.GetUtcNow();
                foreach (var tier in tiers)
                    tier?.Add(now, reading);
            }
        }
    }

    public void SetText(string id, string? text)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
                entry.Text = text;
        }
    }

    /// <summary>
    /// Stores data that goes with the metrics but isn't a number or text, such as album art, under its own id.
    /// Values are replaced, never changed in place, so readers can keep what they got. Null removes it.
    /// </summary>
    public void SetAttachment(string id, object? value)
    {
        lock (_gate)
        {
            if (value is null)
                _attachments.Remove(id);
            else
                _attachments[id] = value;
        }
    }

    public object? GetAttachment(string id)
    {
        lock (_gate)
            return _attachments.GetValueOrDefault(id);
    }

    public bool TryGet(string id, out MetricSample sample)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                sample = new MetricSample(entry.Definition, entry.Value, entry.Text);
                return true;
            }
        }

        sample = default;
        return false;
    }

    /// <summary>Copies up to <paramref name="count"/> of the most recent history points, oldest first. Gaps are <see cref="double.NaN"/>.</summary>
    public double[] GetHistory(string id, int count)
    {
        lock (_gate)
            return _entries.TryGetValue(id, out var entry) ? entry.History.Latest(count) : [];
    }

    /// <summary>
    /// The metric's averaged history over <paramref name="span"/>. The first time a metric is asked for a span, it starts
    /// keeping that history, so it fills from then on, and keeps it while the app runs.
    /// </summary>
    public AveragedHistory GetHistory(string id, LongSpan span)
    {
        var (size, points) = AveragedTier.Of(span);
        var (step, capacity) = AveragedTier.Sizes[size];
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry))
                return AveragedHistory.Empty(step);
            entry.Tiers ??= new AveragedTier?[AveragedTier.Sizes.Length];
            var tier = entry.Tiers[size] ??= new AveragedTier(step, capacity);
            return tier.Latest(_time.GetUtcNow(), points);
        }
    }

    public IReadOnlyList<MetricDefinition> Definitions
    {
        get
        {
            lock (_gate)
                return _entries.Values.Select(e => e.Definition).OrderBy(d => d.Category).ThenBy(d => d.Id).ToArray();
        }
    }

    private sealed class Entry(MetricDefinition definition, int historyLength)
    {
        public MetricDefinition Definition { get; set; } = definition;
        public double? Value { get; set; }
        public string? Text { get; set; }
        public RingBuffer History { get; } = new(historyLength);

        /// <summary>The averaged history asked for, by <see cref="AveragedTier.Sizes"/>; null until a graph asks.</summary>
        public AveragedTier?[]? Tiers { get; set; }
    }
}

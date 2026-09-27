namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// Thread-safe home for every metric definition and its latest value plus a short history.
/// Providers write from background threads; the UI reads on its own tick.
/// </summary>
public sealed class MetricStore
{
    public const int DefaultHistoryLength = 240;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _historyLength;

    public MetricStore(int historyLength = DefaultHistoryLength) => _historyLength = historyLength;

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
    }
}

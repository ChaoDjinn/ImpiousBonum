using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Your own metrics from web addresses and commands (see <see cref="CustomSource"/>), each read on its own schedule and
/// in the background, so a slow one never holds up the rest. Follows changes to the sources as they're saved.
/// </summary>
/// <param name="sources">The current sources; read every second.</param>
/// <param name="fetch">Gets a source's output; <see cref="CustomFetcher"/> unless a test swaps it.</param>
public sealed class CustomDataProvider(
    Func<IReadOnlyList<CustomSource>> sources,
    Func<CustomSource, CancellationToken, Task<string>>? fetch = null,
    TimeProvider? time = null) : IMetricProvider
{
    private readonly Func<CustomSource, CancellationToken, Task<string>> _fetch = fetch ?? CustomFetcher.FetchAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store) => Sync(store, Usable());

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var usable = Usable();
        Sync(store, usable);

        var now = _time.GetUtcNow();
        foreach (var source in usable)
        {
            var state = _states[source.Name];
            if (state.Busy || now < state.Due)
                continue;
            state.Busy = true;
            state.Due = now + source.Interval;
            _ = ReadAsync(store, source, state);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Reads one source now and returns what it found, for the setup window's Test button.</summary>
    public static async Task<(IReadOnlyList<CustomReading> Readings, string Status)> TestAsync(
        CustomSource source, CancellationToken cancellationToken, Func<CustomSource, CancellationToken, Task<string>>? fetch = null)
    {
        try
        {
            var output = await (fetch ?? CustomFetcher.FetchAsync)(source, cancellationToken).ConfigureAwait(false);
            return (source.Read(output), "OK");
        }
        catch (CustomSourceException ex)
        {
            return ([], ex.Message);
        }
    }

    private async Task ReadAsync(MetricStore store, CustomSource source, State state)
    {
        try
        {
            var (readings, status) = await TestAsync(source, _stop.Token, _fetch).ConfigureAwait(false);
            // A source changed or removed while it was being read: drop the result.
            if (state.Replaced)
                return;
            foreach (var reading in readings)
            {
                var id = source.MetricId(reading.Field);
                store.Set(id, reading.Value);
                store.SetText(id, reading.Text);
            }
            if (readings.Count == 0)
            {
                foreach (var field in source.FieldsOrValue)
                {
                    store.Set(source.MetricId(field), null);
                    store.SetText(source.MetricId(field), null);
                }
            }
            store.SetText(source.StatusId, status);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            state.Busy = false;
        }
    }

    private List<CustomSource> Usable() =>
        sources().Where(s => s is not null && s.Problem() is null).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Registers the metrics of new and changed sources and removes those of sources that are gone.</summary>
    private void Sync(MetricStore store, IReadOnlyList<CustomSource> usable)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in usable)
        {
            var key = source.Key;
            if (!_states.TryGetValue(source.Name, out var state) || state.Key != key)
            {
                // New or edited: read it straight away.
                if (state is not null)
                    state.Replaced = true;
                _states[source.Name] = new State(key) { Due = DateTimeOffset.MinValue };
                var category = $"Custom: {source.Name}";
                foreach (var field in source.FieldsOrValue)
                {
                    var unit = CustomField.ParseUnit(field.Unit) ?? MetricUnit.None;
                    var max = field.Max ?? (unit == MetricUnit.Percent ? 100 : null);
                    store.Register(new MetricDefinition(source.MetricId(field), $"{source.Name}: {field.Name}", category, unit, max));
                }
                store.Register(new MetricDefinition(source.StatusId, $"{source.Name}: whether the last read worked", category, MetricUnit.Text));
            }
            wanted.UnionWith(source.FieldsOrValue.Select(source.MetricId).Append(source.StatusId));
        }

        foreach (var gone in _registered.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList())
            store.Unregister(gone);
        foreach (var name in _states.Keys.Where(name => !usable.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _states[name].Replaced = true;
            _states.Remove(name);
        }
        _registered.Clear();
        _registered.UnionWith(wanted);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private sealed class State(string key)
    {
        /// <summary>The source's settings when this state was made (see <see cref="CustomSource.Key"/>).</summary>
        public string Key { get; } = key;

        public DateTimeOffset Due { get; set; }

        public volatile bool Busy;

        /// <summary>The source was edited or removed: a read still in flight is dropped.</summary>
        public volatile bool Replaced;
    }
}

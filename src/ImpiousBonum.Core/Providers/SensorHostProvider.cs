using System.IO.Pipes;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Receives temperatures, fans, power and the rest from the elevated sensor host over a named pipe,
/// so the dashboard itself never needs admin rights or a kernel driver. Reconnects automatically if the host restarts.
/// </summary>
public sealed class SensorHostProvider : IMetricProvider
{
    /// <summary>Text metric describing the connection, e.g. "Sensors running" or "Sensor service not running".</summary>
    public const string Status = "sensors.status";

    public const string NotRunning = "Sensor service not running";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _aliases = SensorAliases.All.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hostIds = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;

    /// <summary>Streams rather than polls, so the sampler doesn't run it on a timer.</summary>
    public TimeSpan Interval => Timeout.InfiniteTimeSpan;

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Status, "Sensor service status", "Sensors", MetricUnit.Text));
        store.SetText(Status, NotRunning);
        foreach (var alias in SensorAliases.All)
            store.Register(alias.ToDefinition());

        _loop = Task.Run(() => RunAsync(store, _stop.Token));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private async Task RunAsync(MetricStore store, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", SensorProtocol.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);

                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (SensorProtocol.Deserialize(line) is { } message)
                        Apply(store, message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // Host not installed, not started yet, or restarting. Try again shortly.
            }

            ClearValues(store);
            store.SetText(Status, NotRunning);

            try
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Apply(MetricStore store, SensorMessage message)
    {
        switch (message.Type)
        {
            case SensorMessage.StatusType:
                store.SetText(Status, message.Status);
                break;

            case SensorMessage.CatalogType when message.Sensors is not null:
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sensor in message.Sensors)
                {
                    store.Register(sensor.ToDefinition());
                    ids.Add(sensor.Id);
                }
                // Hardware that went away. Aliases stay registered so layouts keep showing "—".
                foreach (var gone in _hostIds.Except(ids).Where(id => !_aliases.Contains(id)))
                    store.Unregister(gone);
                _hostIds = ids;
                break;

            case SensorMessage.ValuesType when message.Values is not null:
                foreach (var (id, value) in message.Values)
                    store.Set(id, value);
                break;
        }
    }

    private void ClearValues(MetricStore store)
    {
        foreach (var id in _hostIds.Concat(_aliases))
            store.Set(id, null);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }
}

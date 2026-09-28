using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Sensors;

/// <summary>
/// Reads sensors and frame rates once a second and broadcasts them.
/// Sits idle, without touching the hardware or tracing presents, while no dashboard is connected.
/// </summary>
internal sealed class SensorHost : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Informational version (e.g. "0.2.0+commit"), matching the dashboard it was built with.</summary>
    public static string HostVersion { get; } =
        typeof(SensorHost).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";

    private readonly SensorCollector _collector = new();
    private readonly PipeBroadcaster _broadcaster = new();
    private readonly FrameRateMonitor _frames = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public void Start()
    {
        _broadcaster.Start();
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await _broadcaster.SetStatusAsync(new SensorMessage { Type = SensorMessage.StatusType, Status = "Sensors starting" });

        try
        {
            _collector.Open();
            _collector.Update();
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open hardware sensors: {ex}");
            await _broadcaster.SetStatusAsync(new SensorMessage { Type = SensorMessage.StatusType, Status = "Sensor host failed to start" });
            return;
        }

        await PublishCatalogAsync();
        Log.Info($"{_collector.Status}. {_collector.Catalog.Count} sensors. Admin: {_collector.IsElevated}, PawnIO: {_collector.IsPawnIoRunning}.");

        using var timer = new PeriodicTimer(Interval);
        while (await WaitAsync(timer, cancellationToken))
        {
            if (!_broadcaster.HasClients)
            {
                if (_frames.IsRunning)
                {
                    _frames.Stop();
                    Log.Info("Frame counting stopped (no dashboards connected).");
                }
                continue;
            }

            try
            {
                var catalogChanged = _collector.Update();
                if (!_frames.IsRunning && _frames.Problem is null)
                {
                    // Only tried once per run: a failure (e.g. no admin rights) won't fix itself by retrying.
                    if (!_frames.Start())
                        Log.Error(_frames.Problem!);
                    catalogChanged = true;
                }
                if (catalogChanged)
                    await PublishCatalogAsync();

                await _broadcaster.BroadcastAsync(new SensorMessage
                {
                    Type = SensorMessage.ValuesType,
                    Values = _collector.ReadValues(),
                    Presenters = _frames.IsRunning ? _frames.Snapshot() : null,
                });
            }
            catch (Exception ex)
            {
                // One bad sensor read shouldn't take the service down.
                Log.Error($"Sensor update failed: {ex.Message}");
            }
        }
    }

    private async Task PublishCatalogAsync()
    {
        await _broadcaster.SetStatusAsync(new SensorMessage
        {
            Type = SensorMessage.StatusType,
            Status = _frames.Problem is { } problem ? $"{_collector.Status}. {problem}" : _collector.Status,
            Version = HostVersion,
            LowLevelAccess = _collector.HasLowLevelAccess,
        });
        await _broadcaster.SetCatalogAsync(new SensorMessage { Type = SensorMessage.CatalogType, Sensors = _collector.Catalog });
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
            await _loop;
        await _broadcaster.DisposeAsync();
        _frames.Dispose();
        _collector.Dispose();
        _stop.Dispose();
    }
}

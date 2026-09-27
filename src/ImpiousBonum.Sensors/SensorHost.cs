using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Sensors;

/// <summary>Reads sensors once a second and broadcasts them. Sits idle, without touching the hardware, while no dashboard is connected.</summary>
internal sealed class SensorHost : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly SensorCollector _collector = new();
    private readonly PipeBroadcaster _broadcaster = new();
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
                continue;

            try
            {
                if (_collector.Update())
                    await PublishCatalogAsync();
                await _broadcaster.BroadcastAsync(new SensorMessage { Type = SensorMessage.ValuesType, Values = _collector.ReadValues() });
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
            Status = _collector.Status,
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
        _collector.Dispose();
        _stop.Dispose();
    }
}

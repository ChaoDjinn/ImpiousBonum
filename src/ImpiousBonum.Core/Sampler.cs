using System.Diagnostics;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core;

/// <summary>Runs each provider on its own background loop, writing into a shared <see cref="MetricStore"/>.</summary>
public sealed class Sampler : IAsyncDisposable
{
    private readonly MetricStore _store;
    private readonly IReadOnlyList<IMetricProvider> _providers;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = [];

    public Sampler(MetricStore store, IEnumerable<IMetricProvider> providers)
    {
        _store = store;
        _providers = providers.ToList();
    }

    public MetricStore Store => _store;

    /// <param name="claudeUsagePath">Where Claude Code's status line writes plan usage; null leaves out the Claude metrics.</param>
    public static IReadOnlyList<IMetricProvider> CreateDefaultProviders(
        string pingHost = "1.1.1.1", Func<(int ProcessId, string? Name)>? frameRateTarget = null, string? claudeUsagePath = null) =>
    [
        new CpuProvider(),
        new MemoryProvider(),
        new GpuProvider(),
        new DriveProvider(),
        new NetworkProvider(),
        new PingProvider(pingHost),
        new SensorHostProvider(frameRateTarget),
        .. claudeUsagePath is null ? Array.Empty<IMetricProvider>() : [new ClaudeUsageProvider(claudeUsagePath)],
    ];

    public void Start()
    {
        foreach (var provider in _providers)
        {
            try
            {
                provider.Initialize(_store);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} disabled: {ex.Message}");
                continue;
            }

            if (provider.Interval > TimeSpan.Zero)
                _loops.Add(Task.Run(() => RunAsync(provider, _stop.Token)));
        }
    }

    /// <summary>Samples every provider once, synchronously. Used for snapshots so the first frame has data.</summary>
    public async Task SampleOnceAsync()
    {
        foreach (var provider in _providers)
        {
            try
            {
                await provider.SampleAsync(_store, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} sample failed: {ex.Message}");
            }
        }
    }

    private async Task RunAsync(IMetricProvider provider, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(provider.Interval);
        do
        {
            try
            {
                await provider.SampleAsync(_store, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} sample failed: {ex.Message}");
            }
        }
        while (await WaitAsync(timer, cancellationToken));
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
        // ConfigureAwait(false) so a UI thread blocking on shutdown can't deadlock against these continuations.
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_loops).ConfigureAwait(false);
        foreach (var provider in _providers)
            provider.Dispose();
        _stop.Dispose();
    }
}

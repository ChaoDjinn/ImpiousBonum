using System.Net.NetworkInformation;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>Round-trip time to a host (Cloudflare's 1.1.1.1 by default). Reports no value while the host is unreachable.</summary>
public sealed class PingProvider(string host = "1.1.1.1") : IMetricProvider
{
    public const string Latency = "net.ping";

    private readonly Ping _ping = new();

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Latency, $"Ping ({host})", "Network", MetricUnit.Milliseconds));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _ping.SendPingAsync(host, TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
            store.Set(Latency, reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
        }
        catch (PingException)
        {
            store.Set(Latency, null);
        }
    }

    public void Dispose() => _ping.Dispose();
}

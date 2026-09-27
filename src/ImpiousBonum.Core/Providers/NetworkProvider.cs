using System.Net.NetworkInformation;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Download and upload rate across internet-facing adapters (those with a default gateway).
/// Counting only gateway adapters avoids double counting when a Hyper-V or VPN adapter sits on top of the physical one.
/// </summary>
public sealed class NetworkProvider : IMetricProvider
{
    public const string Download = "net.down";
    public const string Upload = "net.up";

    private readonly Dictionary<string, (long Received, long Sent)> _last = [];
    private long _lastTimestamp;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Download, "Download", "Network", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(Upload, "Upload", "Network", MetricUnit.BytesPerSecond));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = _lastTimestamp == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();
        var withGateway = interfaces.Where(n => n.GetIPProperties().GatewayAddresses.Count > 0).ToList();
        var counted = withGateway.Count > 0 ? withGateway : interfaces;

        long received = 0, sent = 0;
        var current = new Dictionary<string, (long, long)>();
        foreach (var nic in counted)
        {
            var stats = nic.GetIPStatistics();
            current[nic.Id] = (stats.BytesReceived, stats.BytesSent);
            // Only count adapters we also saw last time; a newly appeared adapter would otherwise spike.
            if (_last.TryGetValue(nic.Id, out var previous))
            {
                received += Math.Max(0, stats.BytesReceived - previous.Received);
                sent += Math.Max(0, stats.BytesSent - previous.Sent);
            }
        }

        _last.Clear();
        foreach (var (id, value) in current)
            _last[id] = value;

        if (seconds > 0)
        {
            store.Set(Download, received / seconds);
            store.Set(Upload, sent / seconds);
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }
}

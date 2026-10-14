using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Download and upload rate and totals across internet-facing adapters (those with a default gateway), plus the
/// adapter's name and local IP address.
/// Counting only gateway adapters avoids double counting when a Hyper-V or VPN adapter sits on top of the physical one.
/// Each adapter also has its own rates, link speed and address, e.g. <c>net.ethernet.down</c> (see <see cref="IdFor"/>),
/// from when it's first seen connected; they stay, empty, while it's disconnected.
/// </summary>
public sealed class NetworkProvider : IMetricProvider
{
    public const string Download = "net.down";
    public const string Upload = "net.up";
    public const string DownloadTotal = "net.down.total";
    public const string UploadTotal = "net.up.total";
    public const string Address = "net.ip";
    public const string Adapter = "net.adapter";

    /// <summary>The metrics each adapter has its own of, after its part of the id.</summary>
    public const string AdapterDown = "down", AdapterUp = "up", AdapterSpeed = "speed", AdapterAddress = "ip";

    private readonly Dictionary<string, (long Received, long Sent)> _last = [];

    // Adapters with metrics of their own, by id part, and the adapter (Windows' id for it) each belongs to.
    private readonly Dictionary<string, string> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private long _lastTimestamp;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Download, "Download", "Network", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(Upload, "Upload", "Network", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(DownloadTotal, "Downloaded since the adapter connected", "Network", MetricUnit.Bytes));
        store.Register(new MetricDefinition(UploadTotal, "Uploaded since the adapter connected", "Network", MetricUnit.Bytes));
        store.Register(new MetricDefinition(Address, "Local IP address", "Network", MetricUnit.Text));
        store.Register(new MetricDefinition(Adapter, "Network adapter", "Network", MetricUnit.Text));
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

        long received = 0, sent = 0, receivedTotal = 0, sentTotal = 0;
        var current = new Dictionary<string, (long, long)>();
        var rates = new Dictionary<string, (double? Down, double? Up)>();
        foreach (var nic in interfaces)
        {
            var stats = nic.GetIPStatistics();
            current[nic.Id] = (stats.BytesReceived, stats.BytesSent);
            var isCounted = counted.Contains(nic);
            if (isCounted)
            {
                receivedTotal += stats.BytesReceived;
                sentTotal += stats.BytesSent;
            }
            // Only count adapters we also saw last time; a newly appeared adapter would otherwise spike.
            if (_last.TryGetValue(nic.Id, out var previous))
            {
                var down = Math.Max(0, stats.BytesReceived - previous.Received);
                var up = Math.Max(0, stats.BytesSent - previous.Sent);
                if (isCounted)
                {
                    received += down;
                    sent += up;
                }
                if (seconds > 0)
                    rates[nic.Id] = (down / seconds, up / seconds);
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

        store.Set(DownloadTotal, counted.Count == 0 ? null : receivedTotal);
        store.Set(UploadTotal, counted.Count == 0 ? null : sentTotal);
        var primary = withGateway.FirstOrDefault();
        store.SetText(Adapter, primary?.Name);
        store.SetText(Address, IPv4(primary));

        PublishAdapters(store, interfaces, rates);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The id of one adapter's own metric: <c>net.ethernet.down</c> for <paramref name="metric"/> <c>down</c> of the
    /// adapter called "Ethernet", <c>net.wi-fi.speed</c> for "Wi-Fi". Null for a name with no letters or digits.
    /// </summary>
    public static string? IdFor(string adapterName, string metric) =>
        MetricIdPart.From(adapterName) is { Length: > 0 } part ? $"net.{part}.{metric}" : null;

    private void PublishAdapters(MetricStore store, IReadOnlyList<NetworkInterface> connected, Dictionary<string, (double? Down, double? Up)> rates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nic in connected)
        {
            // Windows also lists the filters stacked on an adapter ("Ethernet-QoS Packet Scheduler-0000") and hidden
            // virtual ones ("Local Area Connection* 6"), none with an address of their own; leave those out.
            if (!HasAddress(nic))
                continue;
            var part = MetricIdPart.From(nic.Name);
            // Two adapters whose names give the same id: only the first has metrics of its own.
            var known = _adapters.TryGetValue(part, out var owner);
            if (part.Length == 0 || (known && owner != nic.Id) || !seen.Add(part))
                continue;
            if (!known)
            {
                _adapters[part] = nic.Id;
                var category = $"Network: {nic.Name}";
                store.Register(new MetricDefinition(IdFor(nic.Name, AdapterDown)!, $"{nic.Name}: download", category, MetricUnit.BytesPerSecond));
                store.Register(new MetricDefinition(IdFor(nic.Name, AdapterUp)!, $"{nic.Name}: upload", category, MetricUnit.BytesPerSecond));
                store.Register(new MetricDefinition(IdFor(nic.Name, AdapterSpeed)!, $"{nic.Name}: link speed, e.g. 1 Gbps", category, MetricUnit.Text));
                store.Register(new MetricDefinition(IdFor(nic.Name, AdapterAddress)!, $"{nic.Name}: IP address", category, MetricUnit.Text));
            }
            var rate = rates.GetValueOrDefault(nic.Id);
            store.Set(IdFor(nic.Name, AdapterDown)!, rate.Down);
            store.Set(IdFor(nic.Name, AdapterUp)!, rate.Up);
            store.SetText(IdFor(nic.Name, AdapterSpeed)!, Speed(nic.Speed));
            store.SetText(IdFor(nic.Name, AdapterAddress)!, IPv4(nic));
        }

        // Disconnected adapters keep their metrics, empty, so widgets showing them say so rather than break.
        foreach (var part in _adapters.Keys.Where(p => !seen.Contains(p)))
        {
            store.Set($"net.{part}.{AdapterDown}", null);
            store.Set($"net.{part}.{AdapterUp}", null);
            store.SetText($"net.{part}.{AdapterSpeed}", null);
            store.SetText($"net.{part}.{AdapterAddress}", null);
        }
    }

    /// <summary>A link speed as Windows shows it: "1 Gbps", "2.5 Gbps", "866.7 Mbps"; null when it isn't known.</summary>
    internal static string? Speed(long bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => null,
        >= 1_000_000_000 => $"{(bitsPerSecond / 1e9).ToString("0.#", CultureInfo.CurrentCulture)} Gbps",
        >= 1_000_000 => $"{(bitsPerSecond / 1e6).ToString("0.#", CultureInfo.CurrentCulture)} Mbps",
        _ => $"{(bitsPerSecond / 1e3).ToString("0.#", CultureInfo.CurrentCulture)} Kbps",
    };

    /// <summary>A gateway, an IPv4 address, or an IPv6 one that isn't link-local: what a real connection has.</summary>
    private static bool HasAddress(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();
        return properties.GatewayAddresses.Count > 0 || properties.UnicastAddresses.Any(a =>
            a.Address.AddressFamily == AddressFamily.InterNetwork
            || (a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal));
    }

    private static string? IPv4(NetworkInterface? nic) => nic?.GetIPProperties().UnicastAddresses
        .Select(a => a.Address)
        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();

    public void Dispose()
    {
    }
}

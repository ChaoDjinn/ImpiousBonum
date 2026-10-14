using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The Wi-Fi signal and the network's name, on PCs with Wi-Fi: its metrics appear once a Wi-Fi adapter is seen.
/// Read without location access, so Windows never asks for it or shows its location icon (see <see cref="WlanReader"/>).
/// </summary>
/// <param name="read">Whether there's Wi-Fi, and the connection's signal in dBm (null when not connected); Windows unless a test swaps it.</param>
/// <param name="ssid">The connected network's name; Windows unless a test swaps it.</param>
public sealed class WifiProvider(Func<(bool HasWifi, int? Rssi)>? read = null, Func<string?>? ssid = null, TimeProvider? time = null) : IMetricProvider
{
    public const string Signal = "wifi.signal";
    public const string Rssi = "wifi.rssi";
    public const string Network = "wifi.ssid";

    /// <summary>How often the network's name is looked up again while connected; it rarely changes.</summary>
    public static readonly TimeSpan SsidEvery = TimeSpan.FromSeconds(30);

    private readonly WlanReader? _reader = read is null ? new WlanReader() : null;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private bool _registered;
    private string? _ssid;
    private DateTimeOffset _ssidDue;

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var (hasWifi, rssi) = read is null ? Read() : read();
        if (!hasWifi && !_registered)
            return ValueTask.CompletedTask;
        if (!_registered)
        {
            store.Register(new MetricDefinition(Signal, "Wi-Fi signal", "Network", MetricUnit.Percent, 100));
            store.Register(new MetricDefinition(Rssi, "Wi-Fi signal strength in dBm", "Network", MetricUnit.None, Format: "0"));
            store.Register(new MetricDefinition(Network, "Wi-Fi network", "Network", MetricUnit.Text));
            _registered = true;
        }

        var now = _time.GetUtcNow();
        if (rssi is null)
        {
            _ssid = null;
            _ssidDue = now;
        }
        else if (now >= _ssidDue)
        {
            _ssid = ssid is null ? WlanReader.ConnectedSsid() : ssid();
            _ssidDue = now + SsidEvery;
        }

        store.Set(Signal, rssi is { } dbm ? Quality(dbm) : null);
        store.Set(Rssi, rssi);
        store.SetText(Network, _ssid);
        return ValueTask.CompletedTask;
    }

    private (bool, int?) Read() => _reader!.TryReadRssi(out var rssi) ? (true, rssi) : (false, null);

    /// <summary>Signal quality as Windows shows it: −100 dBm or weaker is 0%, −50 dBm or stronger 100%, in a straight line between.</summary>
    internal static double Quality(int rssi) => Math.Clamp(2 * (rssi + 100), 0, 100);

    public void Dispose() => _reader?.Dispose();
}

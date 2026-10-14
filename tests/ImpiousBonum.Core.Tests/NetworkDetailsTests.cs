using System.Net.NetworkInformation;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class NetworkDetailsTests
{
    [Theory]
    [InlineData("Ethernet", "ethernet")]
    [InlineData("Ethernet 2", "ethernet-2")]
    [InlineData("Wi-Fi", "wi-fi")]
    [InlineData(" vEthernet (Default Switch) ", "vethernet-default-switch")]
    [InlineData("()", "")]
    public void Names_become_part_of_a_metric_id(string name, string expected) => Assert.Equal(expected, MetricIdPart.From(name));

    [Fact]
    public void Adapters_have_ids_and_speeds_read_naturally()
    {
        Assert.Equal("net.ethernet-2.down", NetworkProvider.IdFor("Ethernet 2", NetworkProvider.AdapterDown));
        Assert.Null(NetworkProvider.IdFor("--", NetworkProvider.AdapterDown));
        Assert.Equal("1 Gbps", NetworkProvider.Speed(1_000_000_000));
        Assert.Equal((2.5).ToString("0.#") + " Gbps", NetworkProvider.Speed(2_500_000_000));
        Assert.Equal((866.7).ToString("0.#") + " Mbps", NetworkProvider.Speed(866_700_000));
        Assert.Null(NetworkProvider.Speed(0));
        Assert.Null(NetworkProvider.Speed(-1));
    }

    [Fact]
    public void Battery_time_left_is_only_there_when_windows_knows_it()
    {
        Assert.Equal("2h 14m", SystemProvider.TimeLeft((uint)TimeSpan.FromMinutes(134).TotalSeconds));
        Assert.Null(SystemProvider.TimeLeft(uint.MaxValue));
    }

    [Theory]
    [InlineData(-40, 100)]
    [InlineData(-50, 100)]
    [InlineData(-67, 66)]
    [InlineData(-100, 0)]
    [InlineData(-110, 0)]
    public void Wifi_signal_quality_is_worked_out_as_windows_does(int rssi, double expected) =>
        Assert.Equal(expected, WifiProvider.Quality(rssi));

    [Fact]
    public async Task Wifi_metrics_appear_once_there_is_wifi()
    {
        var reading = (HasWifi: false, Rssi: (int?)null);
        var lookups = 0;
        var time = new FakeTime(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        using var provider = new WifiProvider(() => reading, () => $"Home {++lookups}", time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.DoesNotContain(store.Definitions, d => d.Id.StartsWith("wifi.", StringComparison.Ordinal));

        // Wi-Fi, but not connected: the metrics are there, empty.
        reading = (true, null);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Signal, out var signal) && signal.Value is null);
        Assert.Equal(0, lookups);

        reading = (true, -67);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Signal, out signal) && signal.Value == 66);
        Assert.True(store.TryGet(WifiProvider.Rssi, out var rssi) && rssi.Value == -67);
        Assert.True(store.TryGet(WifiProvider.Network, out var ssid) && ssid.Text == "Home 1");

        // The name is looked up again only every 30 seconds…
        time.Advance(TimeSpan.FromSeconds(10));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, lookups);
        time.Advance(WifiProvider.SsidEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Network, out ssid) && ssid.Text == "Home 2");

        // …or straight away after reconnecting, perhaps to another network.
        reading = (true, null);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Network, out ssid) && ssid.Text is null);
        reading = (true, -50);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Network, out ssid) && ssid.Text == "Home 3");

        // An adapter switched off keeps the metrics, empty.
        reading = (false, null);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.True(store.TryGet(WifiProvider.Signal, out signal) && signal.Value is null);
    }

    // These read the real adapters, so they only check anything on Windows (CI runs there).
    [Fact]
    public async Task Each_connected_adapter_has_its_own_metrics_but_not_the_filters_on_it()
    {
        using var network = new NetworkProvider();
        using var wifi = new WifiProvider();
        var store = new MetricStore();
        network.Initialize(store);
        wifi.Initialize(store);
        await network.SampleAsync(store, CancellationToken.None);
        await Task.Delay(200);
        await network.SampleAsync(store, CancellationToken.None);
        await wifi.SampleAsync(store, CancellationToken.None);

        var connected = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && n.GetIPProperties().GatewayAddresses.Count > 0)
            .Select(n => n.Name)
            .Where(n => MetricIdPart.From(n).Length > 0)
            .DistinctBy(MetricIdPart.From)
            .ToList();
        foreach (var name in connected)
        {
            Assert.True(store.TryGet(NetworkProvider.IdFor(name, NetworkProvider.AdapterDown)!, out var down), $"{name} has no download metric");
            Assert.True(down.Value >= 0, $"{name}'s download rate is {down.Value}");
            Assert.True(store.TryGet(NetworkProvider.IdFor(name, NetworkProvider.AdapterSpeed)!, out _));
        }
        Assert.DoesNotContain(store.Definitions, d => d.Id.Contains("packet-scheduler", StringComparison.Ordinal) || d.Id.Contains("lightweight-filter", StringComparison.Ordinal));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

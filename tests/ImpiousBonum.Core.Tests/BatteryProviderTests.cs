using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class BatteryProviderTests
{
    [Fact]
    public async Task Nothing_appears_without_devices()
    {
        using var provider = new BatteryProvider(() => []);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Empty(store.Definitions);
        Assert.Null(store.GetAttachment(BatteryProvider.Devices));
        Assert.Empty(BatteryProvider.Names(store));
    }

    [Fact]
    public async Task Each_device_has_its_level_and_name()
    {
        IReadOnlyList<DeviceBattery> devices = [new("WH-1000XM4", 72), new("Xbox Wireless Controller", 55)];
        using var provider = new BatteryProvider(() => devices);
        var store = new MetricStore();
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal(72, Value(store, "battery.wh-1000xm4"));
        Assert.Equal("WH-1000XM4", Text(store, "battery.wh-1000xm4.name"));
        Assert.Equal(55, Value(store, "battery.xbox-wireless-controller"));
        Assert.Equal("Xbox Wireless Controller", Text(store, "battery.xbox-wireless-controller.name"));
        Assert.True(store.TryGet("battery.wh-1000xm4", out var sample));
        Assert.Equal("72 %", MetricFormatter.Format(sample));
        // By name, for the widget.
        Assert.Equal(
            [new BatteryLevel("wh-1000xm4", "WH-1000XM4", 72), new BatteryLevel("xbox-wireless-controller", "Xbox Wireless Controller", 55)],
            Assert.IsAssignableFrom<IReadOnlyList<BatteryLevel>>(store.GetAttachment(BatteryProvider.Devices)));
    }

    [Fact]
    public async Task Devices_with_the_same_name_or_id_are_told_apart()
    {
        IReadOnlyList<DeviceBattery> devices =
        [
            new("Controller", 40),
            new("Controller", 90),
            // Only the punctuation differs, so its id would be the same.
            new("Pad!", 30),
            new("Pad", 35),
            // battery.low is taken.
            new("Low", 60),
            new("   ", 50),
        ];
        using var provider = new BatteryProvider(() => devices);
        var store = new MetricStore();
        await provider.SampleAsync(store, CancellationToken.None);

        // By name, whatever order Windows listed them in; one without a name is a Device.
        var levels = (IReadOnlyList<BatteryLevel>)store.GetAttachment(BatteryProvider.Devices)!;
        Assert.Equal(
            [("controller", "Controller"), ("controller-2", "Controller 2"), ("device", "Device"), ("low-2", "Low"), ("pad", "Pad"), ("pad-2", "Pad!")],
            levels.Select(l => (l.Slug, l.Name)));
        Assert.Equal(40, Value(store, "battery.controller"));
        Assert.Equal(90, Value(store, "battery.controller-2"));
        Assert.Equal(60, Value(store, "battery.low-2"));
        Assert.Equal(MetricUnit.Text, store.Definitions.Single(d => d.Id == BatteryProvider.Low).Unit);
    }

    [Fact]
    public async Task Devices_come_and_go_and_keep_their_readings_while_away()
    {
        IReadOnlyList<DeviceBattery> devices = [new("Headset", 72)];
        using var provider = new BatteryProvider(() => devices);
        var store = new MetricStore();
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(72, Value(store, "battery.headset"));

        // The mouse connects, and the headset is switched off.
        devices = [new("Mouse", 64)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Value(store, "battery.headset"));
        Assert.Equal("Headset", Text(store, "battery.headset.name"));
        Assert.Equal(64, Value(store, "battery.mouse"));
        Assert.Equal(["Mouse"], Levels(store).Select(l => l.Name));
        // Both can still be picked in the widget.
        Assert.Equal(["Headset", "Mouse"], BatteryProvider.Names(store));

        // The headset's back, with the same id; then both go.
        devices = [new("Mouse", 63), new("Headset", 70)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(70, Value(store, "battery.headset"));
        Assert.Equal(["Headset", "Mouse"], Levels(store).Select(l => l.Name));
        devices = [];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Value(store, "battery.headset"));
        Assert.Null(Value(store, "battery.mouse"));
        Assert.Null(store.GetAttachment(BatteryProvider.Devices));
    }

    [Fact]
    public async Task Low_names_the_one_with_the_least_charge_under_20()
    {
        IReadOnlyList<DeviceBattery> devices = [new("Headset", 72), new("Mouse", 25)];
        using var provider = new BatteryProvider(() => devices);
        var store = new MetricStore();
        await provider.SampleAsync(store, CancellationToken.None);
        // None is low: no reading, so a text widget hiding without one hides.
        Assert.True(store.TryGet(BatteryProvider.Low, out var low));
        Assert.False(low.HasValue);

        devices = [new("Headset", 19), new("Mouse", 12), new("Keyboard", 20)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Mouse", Text(store, BatteryProvider.Low));

        // Once it's charged, the next lowest under 20.
        devices = [new("Headset", 19), new("Mouse", 100), new("Keyboard", 20)];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("Headset", Text(store, BatteryProvider.Low));
    }

    private static IReadOnlyList<BatteryLevel> Levels(MetricStore store) =>
        store.GetAttachment(BatteryProvider.Devices) as IReadOnlyList<BatteryLevel> ?? [];

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;
}

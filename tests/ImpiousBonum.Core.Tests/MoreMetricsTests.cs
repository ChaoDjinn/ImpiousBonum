using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class MoreMetricsTests
{
    [Theory]
    [InlineData("3D", GpuProvider.EngineGroup.ThreeD)]
    [InlineData("VideoDecode", GpuProvider.EngineGroup.Decode)]
    [InlineData("VideoEncode", GpuProvider.EngineGroup.Encode)]
    [InlineData("Compute_0", GpuProvider.EngineGroup.Compute)]
    [InlineData("Cuda", GpuProvider.EngineGroup.Compute)]
    [InlineData("Copy", GpuProvider.EngineGroup.Other)]
    [InlineData("Graphics_1", GpuProvider.EngineGroup.Other)]
    public void Gpu_engines_are_grouped_by_their_type_name(string engineType, GpuProvider.EngineGroup expected) =>
        Assert.Equal(expected, GpuProvider.Classify(engineType));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    [InlineData("_Total", null)]
    public void Processor_instances_are_core_numbers(string instance, int? expected) =>
        Assert.Equal(expected, CpuProvider.ParseCore(instance));

    [Theory]
    [InlineData("C:", "C")]
    [InlineData("d:", "D")]
    [InlineData("_Total", null)]
    [InlineData("HarddiskVolume5", null)]
    public void Disk_instances_map_to_drive_letters(string instance, string? expected) =>
        Assert.Equal(expected, DiskActivityProvider.Letter(instance));

    [Theory]
    [InlineData(1, 8, "Charging")]
    [InlineData(1, 1, "Plugged in")]
    [InlineData(0, 2, "On battery")]
    [InlineData(255, 0, "Unknown")]
    public void Battery_status_reads_naturally(byte acLine, byte flag, string expected) =>
        Assert.Equal(expected, SystemProvider.Describe(acLine, flag));

    [Fact]
    public void Desktops_without_a_battery_get_no_battery_metrics()
    {
        Assert.False(SystemProvider.HasBattery(128));
        Assert.False(SystemProvider.HasBattery(255));
        Assert.True(SystemProvider.HasBattery(1));
    }

    // The tests below read the real Windows counters, so they only check anything on Windows (CI runs there).

    [Fact]
    public async Task Cpu_reports_each_logical_processor()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new MetricStore();
        using var cpu = new CpuProvider();
        cpu.Initialize(store);
        await Task.Delay(200);
        await cpu.SampleAsync(store, CancellationToken.None);

        Assert.True(store.TryGet(CpuProvider.Clock, out _));
        for (var i = 0; i < Environment.ProcessorCount; i++)
        {
            Assert.True(store.TryGet(CpuProvider.CoreLoad(i), out var core), $"cpu.core.{i}.load missing");
            Assert.InRange(core.Value!.Value, 0, 100);
        }
    }

    [Fact]
    public async Task Memory_reports_committed_memory()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new MetricStore();
        using var memory = new MemoryProvider();
        memory.Initialize(store);
        await memory.SampleAsync(store, CancellationToken.None);

        store.TryGet(MemoryProvider.CommitTotal, out var total);
        store.TryGet(MemoryProvider.CommitUsed, out var used);
        Assert.True(total.Value > 0);
        Assert.InRange(used.Value!.Value, 1, total.Value!.Value);
    }

    [Fact]
    public async Task Disk_activity_and_system_readings_are_reported()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new MetricStore();
        using var disks = new DiskActivityProvider();
        using var system = new SystemProvider();
        disks.Initialize(store);
        system.Initialize(store);
        await Task.Delay(200);
        await disks.SampleAsync(store, CancellationToken.None);
        await system.SampleAsync(store, CancellationToken.None);

        Assert.True(store.TryGet(DiskActivityProvider.ReadTotal, out var read) && read.Value >= 0);
        var letter = Path.GetPathRoot(Environment.SystemDirectory)![..1];
        Assert.True(store.TryGet(DiskActivityProvider.Active(letter), out _), $"disk.{letter}.active missing");
        Assert.True(store.TryGet(SystemProvider.Uptime, out var uptime) && uptime.Text is { Length: > 0 });
        Assert.True(store.TryGet(SystemProvider.ComputerName, out var name) && name.Text == Environment.MachineName);
    }
}

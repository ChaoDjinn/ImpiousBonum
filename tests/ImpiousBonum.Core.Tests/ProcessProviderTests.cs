using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class ProcessProviderTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    [Fact]
    public void Cpu_is_the_share_of_every_core_over_the_gap()
    {
        // 4 cores for 2 seconds is 8 seconds of CPU; 2 seconds of it is 25%.
        var apps = ProcessProvider.Usage(
            [new ProcessReading(10, "game.exe", 0, 100)],
            [new ProcessReading(10, "game.exe", TwoSeconds.Ticks, 100)],
            TwoSeconds, cores: 4);

        var app = Assert.Single(apps);
        Assert.Equal("game", app.Name);
        Assert.Equal(25, app.Cpu, 3);
    }

    [Fact]
    public void An_apps_processes_count_together()
    {
        var apps = ProcessProvider.Usage(
            [new ProcessReading(1, "chrome.exe", 0, 0), new ProcessReading(2, "Chrome.exe", 0, 0), new ProcessReading(3, "notepad.exe", 0, 0)],
            [
                new ProcessReading(1, "chrome.exe", TwoSeconds.Ticks / 10, 300),
                new ProcessReading(2, "Chrome.exe", TwoSeconds.Ticks / 10, 200),
                new ProcessReading(3, "notepad.exe", 0, 1000),
            ],
            TwoSeconds, cores: 1);

        Assert.Equal(2, apps.Count);
        Assert.Equal("chrome", apps[0].Name);
        Assert.Equal(2, apps[0].Processes);
        Assert.Equal(20, apps[0].Cpu, 3);
        Assert.Equal(500, apps[0].Memory);
        Assert.Equal("notepad", apps[1].Name);
        Assert.Equal(0, apps[1].Cpu);
    }

    [Fact]
    public void New_processes_and_reused_ids_start_from_nothing()
    {
        var apps = ProcessProvider.Usage(
            [new ProcessReading(7, "old.exe", 0, 0)],
            [new ProcessReading(7, "new.exe", TwoSeconds.Ticks * 50, 10), new ProcessReading(8, "fresh.exe", TwoSeconds.Ticks * 50, 10)],
            TwoSeconds, cores: 1);

        Assert.All(apps, a => Assert.Equal(0, a.Cpu));
    }

    [Fact]
    public void An_app_never_shows_more_than_the_whole_cpu()
    {
        var apps = ProcessProvider.Usage(
            [new ProcessReading(1, "x.exe", 0, 0)],
            [new ProcessReading(1, "x.exe", TwoSeconds.Ticks * 3, 0)],
            TwoSeconds, cores: 2);

        Assert.Equal(100, Assert.Single(apps).Cpu);
    }

    [Theory]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("SETUP.EXE", "SETUP")]
    [InlineData("System", "System")]
    [InlineData("Registry", "Registry")]
    public void Names_lose_their_exe(string image, string expected) =>
        Assert.Equal(expected, ProcessProvider.DisplayName(image));

    [Fact]
    public async Task The_first_reading_ranks_nothing_and_the_second_fills_the_metrics()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var cores = Environment.ProcessorCount;
        var readings = new List<IReadOnlyList<ProcessReading>>
        {
            new[] { new ProcessReading(1, "busy.exe", 0, 10), new ProcessReading(2, "big.exe", 0, 5_000) },
            new[] { new ProcessReading(1, "busy.exe", TwoSeconds.Ticks * cores / 2, 10), new ProcessReading(2, "big.exe", 0, 5_000) },
        };
        var next = 0;
        using var provider = new ProcessProvider(() => readings[next++], time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(ProcessProvider.Apps));
        Assert.True(store.TryGet(ProcessProvider.CpuName(1), out var first) && !first.HasValue);

        time.Advance(TwoSeconds);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<AppUsage>>(store.GetAttachment(ProcessProvider.Apps)).Count);
        Assert.True(store.TryGet(ProcessProvider.CpuName(1), out var busiest));
        Assert.Equal("busy", busiest.Text);
        Assert.True(store.TryGet(ProcessProvider.Cpu(1), out var cpu));
        Assert.Equal(50, cpu.Value!.Value, 3);
        Assert.True(store.TryGet(ProcessProvider.MemoryName(1), out var biggest));
        Assert.Equal("big", biggest.Text);
        Assert.True(store.TryGet(ProcessProvider.Memory(1), out var memory));
        Assert.Equal(5_000, memory.Value);
        // Only two apps: the rest of the ranks are empty, not stale.
        Assert.True(store.TryGet(ProcessProvider.CpuName(3), out var third));
        Assert.False(third.HasValue);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

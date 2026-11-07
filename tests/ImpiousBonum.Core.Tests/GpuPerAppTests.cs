using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class GpuPerAppTests
{
    private const string Luid = "luid_0x00000000_0x0000D1B5_phys_0";

    private static (string, double) Engine(int process, int engine, string type, double value) =>
        ($"pid_{process}_{Luid}_eng_{engine}_engtype_{type}", value);

    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000D1B5_phys_0_eng_3_engtype_3D", 1234)]
    [InlineData("pid_8_luid_0x0_0x1_phys_0_eng_0_engtype_Copy", 8)]
    [InlineData("pid_0_luid_0x0_0x1_phys_0_eng_0_engtype_3D", null)]
    [InlineData("luid_0x00000000_0x0000D1B5_phys_0", null)]
    [InlineData("pid_x_luid", null)]
    public void The_process_comes_from_the_instance_name(string instance, int? process) =>
        Assert.Equal(process, GpuProvider.ProcessOf(instance));

    [Fact]
    public void A_process_s_share_is_its_busiest_engine_type_as_Task_Manager_has_it()
    {
        var shares = GpuProvider.PerProcess(
        [
            // The game: 3D on two engines of that type, and some copying.
            Engine(100, 0, "3D", 40),
            Engine(100, 1, "3D", 25),
            Engine(100, 2, "Copy", 30),
            // The browser decoding video: its decode engine is busier than its 3D.
            Engine(200, 0, "3D", 2),
            Engine(200, 5, "VideoDecode", 12.5),
            // Idle, and over 100 from a counter's rounding.
            Engine(300, 0, "3D", 0),
            Engine(400, 0, "3D", 104),
            ("luid_0x0_0x1_phys_0_eng_0_engtype_3D", 50),
        ]);

        Assert.Equal(new Dictionary<int, double> { [100] = 65, [200] = 12.5, [400] = 100 }, shares);
    }

    [Fact]
    public void An_app_s_GPU_share_is_its_processes_added_up_and_only_apps_using_it_are_ranked()
    {
        var now = TimeSpan.FromSeconds(2);
        var apps = ProcessProvider.Usage(
            [new ProcessReading(1, "chrome.exe", 0, 100), new ProcessReading(2, "chrome.exe", 0, 100), new ProcessReading(3, "game.exe", 0, 100), new ProcessReading(4, "notepad.exe", 0, 100)],
            [new ProcessReading(1, "chrome.exe", 0, 100), new ProcessReading(2, "chrome.exe", 0, 100), new ProcessReading(3, "game.exe", now.Ticks, 100), new ProcessReading(4, "notepad.exe", 0, 100)],
            now, cores: 1, gpu: new Dictionary<int, double> { [1] = 3, [2] = 4.5, [3] = 88 });

        Assert.Equal(7.5, apps.Single(a => a.Name == "chrome").Gpu);
        Assert.Equal(88, apps.Single(a => a.Name == "game").Gpu);
        Assert.Equal(0, apps.Single(a => a.Name == "notepad").Gpu);
    }

    [Fact]
    public void The_busiest_apps_on_the_GPU_are_readings()
    {
        var store = new MetricStore();
        var readings = new Queue<IReadOnlyList<ProcessReading>>(
        [
            [new ProcessReading(3, "game.exe", 0, 100), new ProcessReading(4, "notepad.exe", 0, 100)],
            [new ProcessReading(3, "game.exe", 0, 100), new ProcessReading(4, "notepad.exe", 0, 100)],
        ]);
        using var provider = new ProcessProvider(() => readings.Dequeue());
        provider.Initialize(store);
        store.SetAttachment(GpuProvider.Processes, new Dictionary<int, double> { [3] = 88 });

        provider.SampleAsync(store, CancellationToken.None);
        provider.SampleAsync(store, CancellationToken.None);

        Assert.True(store.TryGet(ProcessProvider.Gpu(1), out var top));
        Assert.Equal(88, top.Value);
        Assert.True(store.TryGet(ProcessProvider.GpuName(1), out var name));
        Assert.Equal("game", name.Text);
        // Notepad isn't using the GPU, so there's no second.
        Assert.True(store.TryGet(ProcessProvider.GpuName(2), out var second));
        Assert.Null(second.Text);
    }
}

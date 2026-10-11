using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class CoresWidgetTests
{
    [Theory]
    [InlineData(16, 600, 200, 0, 2, 8)]
    [InlineData(16, 200, 440, 0, 6, 3)]
    [InlineData(16, 1000, 100, 0, 1, 16)]
    [InlineData(16, 360, 120, 2, 2, 8)]
    [InlineData(6, 100, 100, 0, 2, 3)]
    [InlineData(0, 100, 100, 0, 0, 0)]
    public void Heatmap_cells_stay_close_to_square(int count, double width, double height, int rows, int expectedRows, int expectedColumns) =>
        Assert.Equal((expectedRows, expectedColumns), CoresWidget.Grid(count, width, height, rows));

    [Fact]
    public void The_widget_reads_every_thread_it_finds()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            for (var i = 0; i < 4; i++)
            {
                store.Register(new MetricDefinition(CpuProvider.CoreLoad(i), $"Core {i}", "CPU", MetricUnit.Percent, 100));
                store.Set(CpuProvider.CoreLoad(i), i * 25);
            }
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "cores", ["style"] = "heatmap" }, Theme.From(new ThemeSettings()));
            widget.Refresh(store, DateTime.Now);
            widget.Refresh(new MetricStore(), DateTime.Now);
        });
    }
}

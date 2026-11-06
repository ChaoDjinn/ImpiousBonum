using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class GraphSpanTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    // On the hour, so 10-second steps line up with the readings.
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private static (MetricStore Store, FakeTime Time) Store()
    {
        var time = new FakeTime(Start);
        var store = new MetricStore(time: time);
        store.Register(new MetricDefinition("cpu.load", "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition("gpu.load", "GPU load", "GPU", MetricUnit.Percent, 100));
        return (store, time);
    }

    /// <summary>A step of ten readings a second apart, for each metric, with one spike in the CPU's.</summary>
    private static void Step(MetricStore store, FakeTime time, double cpu, double spike, double gpu)
    {
        for (var i = 0; i < 10; i++)
        {
            store.Set("cpu.load", i == 4 ? spike : cpu);
            store.Set("gpu.load", gpu);
            time.Now += TimeSpan.FromSeconds(1);
        }
    }

    private static Widget Create(string type, JsonObject settings)
    {
        settings["type"] = type;
        settings["shrinkToFit"] = false;
        return WidgetFactory.Create(settings, Plain);
    }

    private static IEnumerable<T> All<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T found)
            yield return found;
        if (parent is Panel panel)
        {
            foreach (var child in panel.Children.OfType<DependencyObject>().SelectMany(All<T>))
                yield return child;
        }
    }

    [Theory]
    [InlineData("15 minutes", 90)]
    [InlineData("1 hour", 360)]
    [InlineData("6 hours", 360)]
    [InlineData("24 hours", 288)]
    public void A_longer_span_has_a_point_a_step(string span, int points)
    {
        Sta.Run(() =>
        {
            var graph = All<GraphElement>(Create("graph", new JsonObject { ["span"] = span })).Single();
            Assert.Equal(points, graph.Points);
        });
    }

    [Fact]
    public void Over_an_hour_a_graph_shows_each_step_s_average_with_its_lowest_and_highest_behind()
    {
        Sta.Run(() =>
        {
            var (store, time) = Store();
            var widget = Create("graph", new JsonObject { ["span"] = "1 hour" });
            widget.Refresh(store, DateTime.Now);

            Step(store, time, cpu: 10, spike: 90, gpu: 0);
            Step(store, time, cpu: 30, spike: 30, gpu: 0);
            widget.Refresh(store, DateTime.Now);

            var graph = All<GraphElement>(widget).Single();
            Assert.Equal([18.0, 30.0], graph.Shown.Values);
            Assert.Equal([10.0, 30.0], graph.Shown.Low!);
            Assert.Equal([90.0, 30.0], graph.Shown.High!);
            Assert.NotNull(graph.BandFill);
        });
    }

    [Fact]
    public void Without_the_band_only_the_averages_are_drawn()
    {
        Sta.Run(() =>
        {
            var (store, time) = Store();
            var widget = Create("graph", new JsonObject { ["span"] = "1 hour", ["band"] = false });
            widget.Refresh(store, DateTime.Now);
            Step(store, time, cpu: 10, spike: 90, gpu: 0);
            widget.Refresh(store, DateTime.Now);

            var graph = All<GraphElement>(widget).Single();
            Assert.Equal([18.0], graph.Shown.Values);
            Assert.Null(graph.BandFill);
        });
    }

    [Fact]
    public void Seconds_are_the_readings_themselves_as_before()
    {
        Sta.Run(() =>
        {
            var (store, time) = Store();
            Step(store, time, cpu: 10, spike: 90, gpu: 0);
            // A layout from before time spans, with just its points.
            var widget = Create("graph", new JsonObject { ["points"] = 5 });
            widget.Refresh(store, DateTime.Now);

            var graph = All<GraphElement>(widget).Single();
            Assert.Equal(5, graph.Points);
            Assert.Equal([10.0, 10.0, 10.0, 10.0, 10.0], graph.Shown.Values);
            Assert.Null(graph.Shown.Low);
            Assert.Null(GraphSpan.Of(new WidgetSettings(new JsonObject { ["span"] = "seconds" }, GraphWidget.Descriptor.Settings)));
        });
    }

    [Fact]
    public void A_stat_tile_over_a_span_trends_the_averages_and_gives_the_readings_lowest_and_highest()
    {
        Sta.Run(() =>
        {
            var (store, time) = Store();
            var widget = Create("stat", new JsonObject { ["span"] = "15 minutes" });
            widget.Refresh(store, DateTime.Now);
            Step(store, time, cpu: 10, spike: 90, gpu: 0);
            Step(store, time, cpu: 30, spike: 30, gpu: 0);
            Step(store, time, cpu: 20, spike: 5, gpu: 0);
            widget.Refresh(store, DateTime.Now);

            Assert.Equal([18.0, 30.0, 18.5], All<GraphElement>(widget).Single().Shown.Values);
            Assert.Equal("↓ 5.0 %   ↑ 90.0 %", All<TextBlock>(widget).Single(t => t.Text.StartsWith('↓')).Text);
        });
    }

    [Fact]
    public void A_multi_line_graph_over_a_span_shows_each_line_s_averages()
    {
        Sta.Run(() =>
        {
            var (store, time) = Store();
            var widget = Create("lines", new JsonObject
            {
                ["span"] = "1 hour",
                ["lines"] = new JsonArray(
                    new JsonObject { ["label"] = "CPU", ["metric"] = "cpu.load" },
                    new JsonObject { ["label"] = "GPU", ["metric"] = "gpu.load" }),
            });
            widget.Refresh(store, DateTime.Now);
            Step(store, time, cpu: 10, spike: 90, gpu: 40);
            widget.Refresh(store, DateTime.Now);

            var graphs = All<GraphElement>(widget).ToList();
            Assert.Equal([[18.0], [40.0]], graphs.Select(g => g.Shown.Values));
            Assert.All(graphs, g => Assert.Equal(360, g.Points));
        });
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

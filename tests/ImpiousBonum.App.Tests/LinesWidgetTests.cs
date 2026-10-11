using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class LinesWidgetTests
{
    [Fact]
    public void Lines_share_a_natural_maximum_or_fit_the_highest()
    {
        Assert.Equal(100, LinesWidget.SharedMaximum([100, 100], [[10, 20], [30]]));
        Assert.Equal(2_000, LinesWidget.SharedMaximum([null, null], [[10, double.NaN, 2_000], [30]]));
        Assert.Equal(400, LinesWidget.SharedMaximum([100, null], [[10, 400], [30]]));
        Assert.Null(LinesWidget.SharedMaximum([null], [[double.NaN]]));
    }

    [Fact]
    public void The_widget_draws_every_line_with_and_without_a_legend()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition("a", "A", "Test", MetricUnit.Percent, 100));
            store.Register(new MetricDefinition("b", "B", "Test", MetricUnit.BytesPerSecond));
            for (var i = 0; i < 10; i++)
            {
                store.Set("a", i * 10);
                store.Set("b", i * 1_000);
            }
            foreach (var legend in new[] { true, false })
            {
                var widget = WidgetFactory.Create(new JsonObject
                {
                    ["type"] = "lines",
                    ["legend"] = legend,
                    ["lines"] = new JsonArray(
                        new JsonObject { ["label"] = "A", ["metric"] = "a", ["color"] = "accent" },
                        new JsonObject { ["label"] = "B", ["metric"] = "b", ["color"] = "#FF00FF" },
                        new JsonObject { ["label"] = "Missing", ["metric"] = "nope" }),
                }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
            }
            // A new widget starts with CPU and GPU.
            WidgetFactory.Create(new JsonObject { ["type"] = "lines" }, Theme.From(new ThemeSettings())).Refresh(store, DateTime.Now);
        });
    }
}

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class StatWidgetTests
{
    [Fact]
    public void The_range_ignores_gaps()
    {
        Assert.Equal((2.0, 9.0), StatWidget.Range([double.NaN, 5, 2, double.NaN, 9]));
        Assert.Null(StatWidget.Range([double.NaN]));
        Assert.Null(StatWidget.Range([]));
    }

    [Fact]
    public void The_trend_is_scaled_to_its_own_range_with_room_around_it()
    {
        Assert.Equal((19, 31), StatWidget.Scale(20, 30));
        Assert.Equal((47.5, 52.5), StatWidget.Scale(50, 50));
        Assert.Equal((-1, 1), StatWidget.Scale(0, 0));
    }

    [Fact]
    public void The_tile_shows_the_value_range_and_warning_colour()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var store = new MetricStore();
            store.Register(new MetricDefinition("gpu.temp", "GPU temperature", "Sensors", MetricUnit.Celsius));
            foreach (var reading in new double[] { 60, 72, 66 })
                store.Set("gpu.temp", reading);

            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "stat",
                ["metric"] = "gpu.temp",
                ["label"] = "GPU",
                ["thresholds"] = new JsonArray(new JsonObject { ["above"] = 65, ["color"] = "warning" }),
            }, theme);
            widget.Refresh(store, DateTime.Now);

            var texts = Descendants<TextBlock>(widget).ToList();
            Assert.Equal("GPU", texts[0].Text);
            Assert.Equal("↓ 60.0 °C   ↑ 72.0 °C", texts[1].Text);
            Assert.Equal("66.0 °C", texts[2].Text);
            Assert.Same(theme.Warning, texts[2].Foreground);
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}

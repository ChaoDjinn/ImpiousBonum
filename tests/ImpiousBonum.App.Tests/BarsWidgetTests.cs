using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class BarsWidgetTests
{
    private static MetricSample Sample(MetricUnit unit, double? value, double? max = null) =>
        new(new MetricDefinition("m", "m", "m", unit, max), value, null);

    [Fact]
    public void Bars_fill_to_the_bar_maximum_then_the_metric_maximum_then_100()
    {
        Assert.Equal(0.25, BarsWidget.Fraction(Sample(MetricUnit.Percent, 25, 100), null));
        Assert.Equal(0.5, BarsWidget.Fraction(Sample(MetricUnit.Bytes, 8, 16), null));
        Assert.Equal(0.8, BarsWidget.Fraction(Sample(MetricUnit.Celsius, 80), null));
        Assert.Equal(0.5, BarsWidget.Fraction(Sample(MetricUnit.Celsius, 50), 100));
        Assert.Equal(1, BarsWidget.Fraction(Sample(MetricUnit.Percent, 150, 100), null));
        Assert.Equal(0, BarsWidget.Fraction(Sample(MetricUnit.Percent, null, 100), null));
    }

    [Fact]
    public void Claude_usage_shows_only_while_Claude_Code_reports_and_faded_while_editing()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            new ClaudeUsageProvider("unused").Initialize(store);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "claude" }, Theme.From(new ThemeSettings()));

            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Collapsed, widget.Visibility);

            widget.Editing = true;
            Assert.Equal(Visibility.Visible, widget.Visibility);
            Assert.True(widget.Opacity < 1);

            widget.Editing = false;
            store.Set(ClaudeUsageProvider.Session, 23.5);
            store.Set(ClaudeUsageProvider.Week, 41.2);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
            Assert.Equal(1, widget.Opacity);
        });
    }

    [Fact]
    public void A_bar_without_a_reading_hides_while_the_others_show()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition("a", "a", "t", MetricUnit.Percent, 100));
            store.Register(new MetricDefinition("b", "b", "t", MetricUnit.Percent, 100));
            store.Set("a", 50);
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "bars",
                ["bars"] = new JsonArray(new JsonObject { ["metric"] = "a" }, new JsonObject { ["metric"] = "b" }),
            }, Theme.From(new ThemeSettings()));

            widget.Refresh(store, DateTime.Now);
            var rows = ((StackPanel)widget.Children[0]).Children;
            Assert.Equal(Visibility.Visible, widget.Visibility);
            Assert.Equal(Visibility.Visible, rows[0].Visibility);
            Assert.Equal(Visibility.Collapsed, rows[1].Visibility);

            widget.Editing = true;
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, rows[1].Visibility);
            Assert.True(rows[1].Opacity < 1);
        });
    }

    [Fact]
    public void Bars_can_be_kept_when_they_have_no_reading()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "claude", ["hideUnavailable"] = false }, Theme.From(new ThemeSettings()));
            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }
}

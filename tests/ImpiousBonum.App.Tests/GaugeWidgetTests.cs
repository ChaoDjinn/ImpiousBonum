using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class GaugeWidgetTests
{
    private static MetricSample Sample(MetricUnit unit, double? value, double? max = null) =>
        new(new MetricDefinition("m", "m", "m", unit, max), value, null);

    [Fact]
    public void The_dial_fills_between_the_minimum_and_the_gauge_then_metric_maximum()
    {
        Assert.Equal(0.25, GaugeWidget.Fraction(Sample(MetricUnit.Percent, 25, 100), 0, null));
        Assert.Equal(0.5, GaugeWidget.Fraction(Sample(MetricUnit.Bytes, 8, 16), 0, null));
        Assert.Equal(0.8, GaugeWidget.Fraction(Sample(MetricUnit.Celsius, 80), 0, null));
        Assert.Equal(0.5, GaugeWidget.Fraction(Sample(MetricUnit.Celsius, 60), 30, 90));
        Assert.Equal(0, GaugeWidget.Fraction(Sample(MetricUnit.Celsius, 20), 30, 90));
        Assert.Equal(1, GaugeWidget.Fraction(Sample(MetricUnit.Percent, 150, 100), 0, null));
        Assert.Equal(0, GaugeWidget.Fraction(Sample(MetricUnit.Percent, null, 100), 0, null));
        // A range that's empty or backwards fills nothing rather than dividing by zero.
        Assert.Equal(0, GaugeWidget.Fraction(Sample(MetricUnit.Percent, 50), 100, 100));
    }

    [Fact]
    public void Shapes_cover_their_part_of_the_circle()
    {
        Assert.Equal((135.0, 270.0), GaugeElement.Angles(GaugeShape.Arc));
        Assert.Equal((180.0, 180.0), GaugeElement.Angles(GaugeShape.Half));
        Assert.Equal((270.0, 360.0), GaugeElement.Angles(GaugeShape.Ring));
    }

    [Fact]
    public void The_dial_and_value_change_colour_as_the_value_crosses_thresholds()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "gauge",
                ["metric"] = "gpu.temp",
                ["max"] = 100,
                ["thresholds"] = new JsonArray(new JsonObject { ["above"] = 80, ["color"] = "warning" }),
            }, theme);
            var gauge = Assert.IsType<GaugeElement>(Assert.Single(widget.Children.Cast<object>()));
            var store = new MetricStore();
            store.Register(new MetricDefinition("gpu.temp", "GPU temperature", "GPU", MetricUnit.Celsius));

            store.Set("gpu.temp", 40);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(0.4, gauge.Fraction, 3);
            Assert.Same(theme.Accent, gauge.Fill);
            Assert.Same(theme.Foreground, gauge.ValueBrush);
            Assert.Contains("40", gauge.Value);

            store.Set("gpu.temp", 85);
            widget.Refresh(store, DateTime.Now);
            Assert.Same(theme.Warning, gauge.Fill);
            Assert.Same(theme.Warning, gauge.ValueBrush);
        });
    }

    [Fact]
    public void A_metric_without_a_reading_shows_an_empty_dial_and_a_dash()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "gauge", ["metric"] = "missing" }, Theme.From(new ThemeSettings()));
            var gauge = Assert.IsType<GaugeElement>(Assert.Single(widget.Children.Cast<object>()));
            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(0, gauge.Fraction);
            Assert.Equal("—", gauge.Value);
        });
    }
}

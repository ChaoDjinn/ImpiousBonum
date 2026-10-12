using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class TextWidgetTests
{
    [Fact]
    public void Long_text_shrinks_to_the_widgets_width_and_short_text_keeps_its_size()
    {
        Sta.Run(() =>
        {
            var (widget, store) = Create(new JsonObject { ["text"] = "{fps:0 nounit}", ["fontSize"] = 100 }, width: 300);
            var fit = Assert.IsType<ShrinkToFit>(Assert.Single(widget.Children.Cast<object>()));

            store.Set("fps", 60);
            Measure(widget, store);
            Assert.Equal(1, fit.Scale);

            store.Set("fps", 1440000);
            Measure(widget, store);
            Assert.InRange(fit.Scale, 0.3, 0.99);
            Assert.True(fit.DesiredSize.Width <= 300.5, $"{fit.DesiredSize.Width} is wider than the widget");
        });
    }

    [Fact]
    public void Shrink_to_fit_can_be_turned_off()
    {
        Sta.Run(() =>
        {
            var (widget, _) = Create(new JsonObject { ["text"] = "{fps}", ["shrinkToFit"] = false }, width: 420);
            Assert.IsType<TextBlock>(Assert.Single(widget.Children.Cast<object>()));
        });
    }

    [Fact]
    public void Text_can_hide_until_one_of_its_metrics_has_a_reading()
    {
        Sta.Run(() =>
        {
            var (widget, store) = Create(new JsonObject { ["text"] = "{fps} · {fps.low1}", ["hideUnavailable"] = true }, width: 420);

            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Collapsed, widget.Visibility);

            store.Set("fps", 60);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Fact]
    public void Text_without_metrics_or_without_the_option_always_shows()
    {
        Sta.Run(() =>
        {
            var (plain, store) = Create(new JsonObject { ["text"] = "Hello", ["hideUnavailable"] = true }, width: 420);
            plain.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, plain.Visibility);

            var (shown, _) = Create(new JsonObject { ["text"] = "{fps}" }, width: 420);
            shown.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, shown.Visibility);
        });
    }

    private static (Widget Widget, MetricStore Store) Create(JsonObject definition, double width)
    {
        definition["type"] = "text";
        var store = new MetricStore();
        store.Register(new MetricDefinition("fps", "FPS", "Sensors", MetricUnit.FramesPerSecond));
        store.Register(new MetricDefinition("fps.low1", "1% low", "Sensors", MetricUnit.FramesPerSecond));
        var widget = WidgetFactory.Create(definition, Theme.From(new ThemeSettings()));
        widget.Width = width;
        widget.Height = 190;
        return (widget, store);
    }

    private static void Measure(Widget widget, MetricStore store)
    {
        widget.Refresh(store, DateTime.Now);
        widget.Measure(new Size(widget.Width, widget.Height));
        widget.Arrange(new Rect(0, 0, widget.Width, widget.Height));
        // As on the dashboard: new text re-measures the text, and the box around it follows.
        widget.UpdateLayout();
    }
}

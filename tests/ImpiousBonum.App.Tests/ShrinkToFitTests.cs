using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class ShrinkToFitTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    [Theory]
    [InlineData(300, 400, 1)]
    [InlineData(400, 400, 1)]
    [InlineData(800, 400, 0.5)]
    [InlineData(0, 400, 1)]
    [InlineData(800, 0, 1)]
    public void Only_what_is_bigger_than_its_space_shrinks(double natural, double available, double scale) =>
        Assert.Equal(scale, ShrinkToFit.ScaleFor(natural, available));

    [Theory]
    // A line taller than its space whose ink still fits, in the middle: left alone.
    [InlineData(0.5, 133, 35, 110, 120, 1)]
    // In the middle, ink reaching 10 below a 100 high space: the 60 of it below the middle has to fit in 50.
    [InlineData(0.5, 130, 20, 125, 100, 50.0 / 60)]
    // At the top, only the ink's bottom can be cut off.
    [InlineData(0, 130, 10, 120, 100, 100.0 / 120)]
    // At the bottom, only its top.
    [InlineData(1, 130, 30, 140, 50, 50.0 / 100)]
    // Stretched counts as the top.
    [InlineData(-1, 130, 10, 120, 60, 0.5)]
    [InlineData(0.5, 130, 20, 125, 0, 1)]
    public void Only_ink_that_would_be_cut_off_shrinks_a_line(double anchor, double height, double inkTop, double inkBottom, double available, double scale) =>
        Assert.Equal(scale, ShrinkToFit.ScaleForInk(anchor, height, inkTop, inkBottom, available), 6);

    [Fact]
    public void Text_shrinks_evenly_to_whichever_of_width_and_height_is_shorter_and_never_grows()
    {
        Sta.Run(() =>
        {
            var wide = Fitted("21:47:00 21:47:00", 100, FitDirection.Both, new Size(300, 400));
            Assert.InRange(wide.Scale, 0.1, 0.99);
            Assert.Equal(300, wide.Child.DesiredSize.Width * wide.Scale, 1);
            Assert.True(wide.DesiredSize.Width <= 300.5);

            var tall = Fitted("Hg", 200, FitDirection.Both, new Size(1000, 100));
            Assert.InRange(tall.Scale, 0.1, 0.99);
            AssertInside(tall, new Size(1000, 100));

            // Only the width counts when asked for that alone.
            Assert.Equal(1, Fitted("Hg", 200, FitDirection.Width, new Size(1000, 100)).Scale);

            var small = Fitted("12", 20, FitDirection.Both, new Size(400, 400));
            Assert.Equal(1, small.Scale);
            Assert.Equal(small.Child.DesiredSize, small.DesiredSize);
        });
    }

    [Fact]
    public void A_line_a_little_taller_than_its_space_is_left_alone_while_its_ink_fits()
    {
        Sta.Run(() =>
        {
            // Segoe UI's line is about 1.33 times the font size; digits fill much less of it.
            var line = Fitted("21:47", 100, FitDirection.Both, new Size(400, 120));
            Assert.True(line.Child.DesiredSize.Height > 120);
            Assert.Equal(1, line.Scale);
            AssertInside(line, new Size(400, 120));
        });
    }

    [Fact]
    public void Text_that_fits_is_laid_out_exactly_as_without_shrink_to_fit()
    {
        Sta.Run(() =>
        {
            foreach (var descriptor in WidgetFactory.Descriptors.Where(d => d.Find(Setting.ShrinkToFitKey) is not null))
            {
                var on = Positions(Laid(descriptor.Type, new JsonObject(), descriptor.DefaultWidth, descriptor.DefaultHeight));
                var off = Positions(Laid(descriptor.Type, new JsonObject { [Setting.ShrinkToFitKey] = false }, descriptor.DefaultWidth, descriptor.DefaultHeight));
                Assert.True(on.SequenceEqual(off), $"{descriptor.Type}: {string.Join(", ", on)} vs {string.Join(", ", off)}");
            }
        });
    }

    [Theory]
    [InlineData("""{ "type": "clock", "timeSize": 300 }""", 420, 200)]
    [InlineData("""{ "type": "clock", "dateSize": 90 }""", 420, 200)]
    [InlineData("""{ "type": "timer", "fontSize": 200, "label": "Pizza" }""", 360, 170)]
    [InlineData("""{ "type": "text", "text": "{cpu.load}", "fontSize": 200 }""", 400, 80)]
    [InlineData("""{ "type": "stat", "fontSize": 160 }""", 300, 160)]
    [InlineData("""{ "type": "rows", "fontSize": 60, "rows": [{ "label": "Download", "text": "{net.down}" }, { "label": "Upload", "text": "{net.up}" }] }""", 300, 110)]
    [InlineData("""{ "type": "bars", "fontSize": 50 }""", 300, 130)]
    [InlineData("""{ "type": "drives", "fontSize": 60 }""", 300, 230)]
    [InlineData("""{ "type": "processes", "fontSize": 40, "count": 6 }""", 400, 220)]
    [InlineData("""{ "type": "weather", "fontSize": 200 }""", 900, 180)]
    [InlineData("""{ "type": "media", "fontSize": 90 }""", 640, 160)]
    public void Text_too_big_for_its_widget_shrinks_to_fit_inside_it(string json, double width, double height)
    {
        Sta.Run(() =>
        {
            var definition = JsonNode.Parse(json)!.AsObject();
            var widget = Laid((string)definition["type"]!, definition, width, height);
            Assert.Contains(Descendants<ShrinkToFit>(widget), f => f.Scale < 1);
            var ink = VisualTreeHelper.GetDescendantBounds(widget);
            Assert.True(new Rect(-0.5, -0.5, width + 1, height + 1).Contains(ink), $"{ink} is outside {width}×{height}");

            definition[Setting.ShrinkToFitKey] = false;
            var off = Laid((string)definition["type"]!, definition, width, height);
            Assert.Empty(Descendants<ShrinkToFit>(off));
        });
    }

    [Fact]
    public void A_label_and_a_long_value_shrink_together_rather_than_overlap()
    {
        Sta.Run(() =>
        {
            var widget = Laid("graph", new JsonObject { ["label"] = "Memory", ["text"] = "{mem.used} of {mem.total} in use" }, 300, 100);
            var texts = Descendants<TextBlock>(widget).ToList();
            var label = Bounds(texts[0], widget);
            var value = Bounds(texts[1], widget);
            Assert.True(value.Left >= label.Right, $"{value} overlaps {label}");
            Assert.True(value.Right <= 300.5, $"{value} runs off the edge");
        });
    }

    [Fact]
    public void A_gauges_value_shrinks_to_fit_inside_the_dial_unless_told_not_to()
    {
        Sta.Run(() =>
        {
            var gauge = Assert.IsType<GaugeElement>(Assert.Single(Laid("gauge", new JsonObject { ["fontSize"] = 300 }, 260, 240).Children.Cast<object>()));
            Assert.InRange(gauge.ShownValueSize, 6, 299);
            var unshrunk = Assert.IsType<GaugeElement>(Assert.Single(Laid("gauge", new JsonObject { ["fontSize"] = 300, [Setting.ShrinkToFitKey] = false }, 260, 240).Children.Cast<object>()));
            Assert.Equal(300, unshrunk.ShownValueSize);
            var normal = Assert.IsType<GaugeElement>(Assert.Single(Laid("gauge", new JsonObject(), 260, 240).Children.Cast<object>()));
            Assert.Equal(56, normal.ShownValueSize);
        });
    }

    [Fact]
    public void A_half_dial_has_room_for_a_box_standing_on_its_flat_edge()
    {
        var (width, height) = GaugeElement.ValueRoom(GaugeShape.Half, 100);
        // The box's top corners touch the inside of the dial, never beyond it.
        Assert.True(Math.Pow(width / 2, 2) + Math.Pow(height, 2) <= 100 * 100 + 0.001);
        var (ringWidth, ringHeight) = GaugeElement.ValueRoom(GaugeShape.Ring, 100);
        Assert.True(Math.Pow(ringWidth / 2, 2) + Math.Pow(ringHeight / 2, 2) <= 100 * 100 + 0.001);
    }

    private static ShrinkToFit Fitted(string text, double fontSize, FitDirection direction, Size space)
    {
        var block = new TextBlock { Text = text, FontSize = fontSize, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center };
        var fit = new ShrinkToFit { Direction = direction, Child = block };
        fit.Measure(space);
        fit.Arrange(new Rect(space));
        fit.UpdateLayout();
        return fit;
    }

    /// <summary>The child's ink, as drawn after shrinking, is inside the space.</summary>
    private static void AssertInside(ShrinkToFit fit, Size space)
    {
        var ink = VisualTreeHelper.GetDescendantBounds(fit);
        Assert.True(new Rect(-0.5, -0.5, space.Width + 1, space.Height + 1).Contains(ink), $"{ink} is outside {space}");
    }

    /// <summary>A widget with the demo readings, laid out as on the dashboard.</summary>
    private static Widget Laid(string type, JsonObject definition, double width, double height)
    {
        definition["type"] = type;
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening, DemoArtwork.Png(64));
        var widget = WidgetFactory.Create(definition, Theme.From(new ThemeSettings()));
        widget.Width = width;
        widget.Height = height;
        widget.Refresh(store, Evening);
        widget.Measure(new Size(width, height));
        widget.Arrange(new Rect(0, 0, width, height));
        widget.UpdateLayout();
        return widget;
    }

    private static List<string> Positions(Widget widget) =>
        [.. Descendants<TextBlock>(widget).Select(t => $"{t.Text} {t.FontSize:0.##} {Bounds(t, widget)}")];

    private static Rect Bounds(FrameworkElement element, Visual ancestor)
    {
        var rect = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        return new Rect(Math.Round(rect.X, 2), Math.Round(rect.Y, 2), Math.Round(rect.Width, 2), Math.Round(rect.Height, 2));
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

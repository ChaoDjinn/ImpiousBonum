using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

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
    public void Ink_counts_only_what_is_shown()
    {
        Sta.Run(() =>
        {
            var top = new TextBlock { Text = "Top", FontSize = 40 };
            var bottom = new TextBlock { Text = "Bottom", FontSize = 40 };
            var stack = new StackPanel { Children = { top, bottom } };
            var host = new Grid { Children = { stack } };
            Lay(host, 400, 400);
            Assert.True(ShrinkToFit.Ink(host).Bottom > top.ActualHeight);

            // A collapsed element keeps its last drawing, but it isn't on screen.
            bottom.Visibility = Visibility.Collapsed;
            Lay(host, 400, 400);
            Assert.True(ShrinkToFit.Ink(host).Bottom <= top.ActualHeight + 0.5);
            Assert.True(VisualTreeHelper.GetDescendantBounds(host).Bottom > top.ActualHeight);

            // Nor is what a clip cuts off.
            bottom.Visibility = Visibility.Visible;
            stack.Clip = new RectangleGeometry(new Rect(0, 0, 400, 20));
            Lay(host, 400, 400);
            Assert.True(ShrinkToFit.Ink(host).Bottom <= 20.5);
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
    [InlineData("""{ "type": "clock", "zoneSize": 60, "zones": [{ "label": "New York", "zone": "America/New_York" }, { "label": "Tokyo", "zone": "Asia/Tokyo" }, { "label": "Sydney", "zone": "Australia/Sydney" }] }""", 420, 300)]
    [InlineData("""{ "type": "clock", "fontFamily": "Courier New", "zones": [{ "label": "New York", "zone": "America/New_York" }, { "label": "Tokyo", "zone": "Asia/Tokyo" }, { "label": "Sydney", "zone": "Australia/Sydney" }, { "label": "LA", "zone": "America/Los_Angeles" }] }""", 420, 260)]
    [InlineData("""{ "type": "clock", "zoneSize": 40, "zoneLayout": "rows", "zones": [{ "label": "Los Angeles office", "zone": "America/Los_Angeles" }] }""", 300, 260)]
    [InlineData("""{ "type": "timer", "fontSize": 200, "label": "Pizza" }""", 360, 170)]
    [InlineData("""{ "type": "text", "text": "{cpu.load}", "fontSize": 200 }""", 400, 80)]
    [InlineData("""{ "type": "stat", "fontSize": 160 }""", 300, 160)]
    [InlineData("""{ "type": "graph", "fontSize": 80 }""", 427, 60)]
    [InlineData("""{ "type": "graph", "fontSize": 120 }""", 900, 100)]
    [InlineData("""{ "type": "lines", "fontSize": 50 }""", 400, 100)]
    [InlineData("""{ "type": "rows", "fontSize": 60, "rows": [{ "label": "Download", "text": "{net.down}" }, { "label": "Upload", "text": "{net.up}" }] }""", 300, 110)]
    [InlineData("""{ "type": "bars", "fontSize": 50 }""", 300, 130)]
    [InlineData("""{ "type": "drives", "fontSize": 60 }""", 300, 230)]
    [InlineData("""{ "type": "processes", "fontSize": 40, "count": 6 }""", 400, 220)]
    [InlineData("""{ "type": "processes", "fontSize": 50, "count": 3 }""", 400, 220)]
    [InlineData("""{ "type": "weather", "fontSize": 200 }""", 900, 180)]
    [InlineData("""{ "type": "weather", "forecastDetails": "chance of rain, rainfall, wind, gusts, UV", "rainFrom": 0, "details": "conditions, high and low, feels like, wind, gusts, humidity, pressure, UV, chance of rain, rainfall, sunrise, sunset, daylight, dew point, visibility, cloud cover, air quality, pollen" }""", 900, 180)]
    [InlineData("""{ "type": "media", "fontSize": 90 }""", 640, 160)]
    [InlineData("""{ "type": "news", "style": "ticker", "fontSize": 60 }""", 600, 60)]
    [InlineData("""{ "type": "countdowns", "fontSize": 200 }""", 420, 220)]
    [InlineData("""{ "type": "rain", "fontSize": 120 }""", 420, 160)]
    [InlineData("""{ "type": "sun", "fontSize": 80, "moonSize": 200 }""", 560, 200)]
    public void Text_too_big_for_its_widget_shrinks_to_fit_inside_it(string json, double width, double height)
    {
        Sta.Run(() =>
        {
            var definition = JsonNode.Parse(json)!.AsObject();
            var widget = Laid((string)definition["type"]!, definition, width, height);
            Assert.Contains(Descendants<ShrinkToFit>(widget), f => f.Scale < 1);
            // Text's boxes fit the widget; a letter can reach a pixel or so past its box.
            var ink = ShrinkToFit.Ink(widget);
            Assert.True(new Rect(-2, -1, width + 4, height + 2).Contains(ink), $"{ink} is outside {width}×{height}");
            Assert.Empty(CutOff(widget));

            definition[Setting.ShrinkToFitKey] = false;
            var off = Laid((string)definition["type"]!, definition, width, height);
            Assert.Empty(Descendants<ShrinkToFit>(off));
        });
    }

    [Fact]
    public void Lines_that_fit_their_own_width_inside_a_height_fit_shrink_only_as_much_as_they_need()
    {
        Sta.Run(() =>
        {
            // A time too wide and too tall: it shrinks once, until it touches the clock's edge, not twice over.
            var widget = Laid("clock", new JsonObject { ["timeSize"] = 300 }, 420, 200);
            var time = Descendants<TextBlock>(widget).First();
            var ink = ShrinkToFit.Ink(widget);
            Assert.True(ink.Top < 1 || ink.Bottom > 199 || Bounds(time, widget).Width > 419, $"{ink}, {Bounds(time, widget)} leave room in 420×200");
        });
    }

    [Fact]
    public void A_row_that_hides_no_longer_counts()
    {
        Sta.Run(() =>
        {
            JsonObject Bars() => new()
            {
                ["fontSize"] = 40,
                ["bars"] = new JsonArray(
                    new JsonObject { ["label"] = "CPU", ["metric"] = CpuProvider.Load },
                    new JsonObject { ["label"] = "Session", ["metric"] = ClaudeUsageProvider.Session },
                    new JsonObject { ["label"] = "RAM", ["metric"] = MemoryProvider.Load },
                    new JsonObject { ["label"] = "Week", ["metric"] = ClaudeUsageProvider.Week }),
            };
            var store = Demo();
            var widget = Laid("bars", Bars(), 300, 150, store);
            var fit = Descendants<ShrinkToFit>(widget).First();
            var four = fit.Scale;
            Assert.True(four < 1);

            // The bar hides without a reading; the other three get the room.
            store.Set(ClaudeUsageProvider.Week, null);
            widget.Refresh(store, Evening);
            Lay(widget, 300, 150);
            Assert.True(fit.Scale > four);
            Assert.Equal(Descendants<ShrinkToFit>(Laid("bars", Bars(), 300, 150, store)).First().Scale, fit.Scale, 3);
        });
    }

    [Fact]
    public void Text_that_changes_without_changing_size_is_fitted_again()
    {
        Sta.Run(() =>
        {
            // In a monospaced font these are the same size, but the second draws above and below the first.
            var store = Demo();
            store.SetText(MediaProvider.Title, "aaaa");
            var definition = new JsonObject { ["text"] = $"{{{MediaProvider.Title}}}", ["fontSize"] = 100, ["fontFamily"] = "Consolas" };
            var widget = Laid("text", definition, 400, 90, store);
            var fit = Descendants<ShrinkToFit>(widget).Single();
            var size = fit.Child.DesiredSize;
            Assert.Equal(1, fit.Scale);

            store.SetText(MediaProvider.Title, "gÅgÅ");
            widget.Refresh(store, Evening);
            Lay(widget, 400, 90);
            Assert.Equal(size, fit.Child.DesiredSize);
            Assert.True(fit.Scale < 0.95);
            Assert.Equal(Descendants<ShrinkToFit>(Laid("text", definition, 400, 90, store)).Single().Scale, fit.Scale, 3);
            Assert.True(new Rect(-0.5, -0.5, 401, 91).Contains(ShrinkToFit.Ink(widget)));

            store.SetText(MediaProvider.Title, "aaaa");
            widget.Refresh(store, Evening);
            Lay(widget, 400, 90);
            Assert.Equal(1, fit.Scale);
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

    [Theory]
    [InlineData("agenda")]
    [InlineData("news")]
    [InlineData("countdowns")]
    public void A_list_leaves_out_rows_that_would_be_cut_off(string type)
    {
        Sta.Run(() =>
        {
            var definition = new JsonObject { ["fontSize"] = 40, ["headerSize"] = 30 };
            var wasCut = false;
            for (var height = 150; height <= 400; height += 10)
            {
                var widget = Laid(type, definition, 300, height);
                var list = Descendants<WholeRows>(widget).Single();
                Assert.InRange(list.Shown, 1, list.Children.Count);
                Assert.Empty(CutOff(widget));
                Assert.True(new Rect(-0.5, -0.5, 301, height + 1).Contains(ShrinkToFit.Ink(widget)), $"{ShrinkToFit.Ink(widget)} is outside 300×{height}");

                // Without shrink to fit, the last row is often cut in half, as before.
                var off = definition.DeepClone().AsObject();
                off[Setting.ShrinkToFitKey] = false;
                wasCut |= CutOff(Laid(type, off, 300, height)).Count > 0;
            }
            Assert.True(wasCut);
        });
    }

    [Theory]
    // Rows 30 high with 10 under each.
    [InlineData(100, 2)]
    // The space under the last row doesn't have to fit.
    [InlineData(110, 3)]
    [InlineData(1000, 4)]
    // The first row always shows.
    [InlineData(20, 1)]
    public void A_list_shows_the_rows_that_fit_whole_and_always_the_first(double height, int shown)
    {
        Sta.Run(() =>
        {
            var list = new WholeRows();
            for (var i = 0; i < 4; i++)
                list.Children.Add(new Border { Height = 30, Margin = new Thickness(0, 0, 0, 10), Background = Brushes.White });
            var host = new Grid { Children = { list } };
            Lay(host, 200, height);
            Assert.Equal(shown, list.Shown);
            // The rest are clipped off; a first row too tall for the list is cut at its bottom.
            Assert.Equal(Math.Min((shown - 1) * 40 + 30, height), ShrinkToFit.Ink(host).Bottom, 1);
        });
    }

    [Fact]
    public void A_gauges_value_shrinks_to_fit_inside_the_dial_unless_told_not_to()
    {
        Sta.Run(() =>
        {
            var gauge = Gauge(new JsonObject { ["fontSize"] = 300 }, 260, 240);
            Assert.InRange(gauge.ShownValueSize, 6, 299);
            var unshrunk = Gauge(new JsonObject { ["fontSize"] = 300, [Setting.ShrinkToFitKey] = false }, 260, 240);
            Assert.Equal(300, unshrunk.ShownValueSize);
            var normal = Gauge(new JsonObject(), 260, 240);
            Assert.Equal(56, normal.ShownValueSize);
            Assert.Equal(24, normal.ShownLabelSize);
        });
    }

    [Theory]
    [InlineData("arc")]
    [InlineData("half")]
    [InlineData("ring")]
    public void A_gauges_label_shrinks_to_fit_its_width_unless_told_not_to(string style)
    {
        Sta.Run(() =>
        {
            var definition = new JsonObject { ["style"] = style, ["label"] = "Graphics card", ["labelSize"] = 60 };
            var gauge = Gauge(definition, 260, 240);
            Assert.InRange(gauge.ShownLabelSize, 6, 59);
            var ink = VisualTreeHelper.GetContentBounds(gauge);
            Assert.True(ink.Left >= -0.5 && ink.Right <= 260.5, $"{ink} is wider than 260");

            definition[Setting.ShrinkToFitKey] = false;
            Assert.Equal(60, Gauge(definition, 260, 240).ShownLabelSize);
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
        Lay(fit, space.Width, space.Height);
        return fit;
    }

    /// <summary>The child's ink, as drawn after shrinking, is inside the space.</summary>
    private static void AssertInside(ShrinkToFit fit, Size space)
    {
        var ink = VisualTreeHelper.GetDescendantBounds(fit);
        Assert.True(new Rect(-0.5, -0.5, space.Width + 1, space.Height + 1).Contains(ink), $"{ink} is outside {space}");
    }

    private static GaugeElement Gauge(JsonObject definition, double width, double height) =>
        Assert.IsType<GaugeElement>(Assert.Single(Laid("gauge", definition, width, height).Children.Cast<object>()));

    private static MetricStore Demo()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening, DemoArtwork.Png(64));
        return store;
    }

    /// <summary>A widget with the demo readings, or <paramref name="store"/>'s, laid out as on the dashboard.</summary>
    private static Widget Laid(string type, JsonObject definition, double width, double height, MetricStore? store = null)
    {
        definition["type"] = type;
        var widget = WidgetFactory.Create(definition, Theme.From(new ThemeSettings()));
        widget.Width = width;
        widget.Height = height;
        widget.Refresh(store ?? Demo(), Evening);
        Lay(widget, width, height);
        return widget;
    }

    private static void Lay(UIElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        // Settled, rather than fitting going round in circles.
        Assert.All(LaidOut(element), e => Assert.True(e.IsMeasureValid && e.IsArrangeValid, $"{e} isn't laid out"));
    }

    /// <summary>The element and everything in it that isn't collapsed: all that's laid out.</summary>
    private static IEnumerable<UIElement> LaidOut(UIElement element)
    {
        if (element.Visibility == Visibility.Collapsed)
            yield break;
        yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is UIElement child)
            {
                foreach (var inner in LaidOut(child))
                    yield return inner;
            }
        }
    }

    /// <summary>
    /// Text that's partly cut off, by a clip on its way up to the widget or by the widget's edge. Text a list leaves out
    /// altogether doesn't count.
    /// </summary>
    private static List<string> CutOff(Widget widget)
    {
        var cut = new List<string>();
        foreach (var text in Descendants<TextBlock>(widget))
        {
            var ink = VisualTreeHelper.GetContentBounds(text);
            if (ink.IsEmpty)
                continue;
            var shown = ink;
            var hidden = false;
            for (Visual visual = text; !ReferenceEquals(visual, widget); visual = (Visual)VisualTreeHelper.GetParent(visual))
            {
                if (visual is UIElement { Visibility: not Visibility.Visible })
                    hidden = true;
                if (VisualTreeHelper.GetClip(visual) is { } clip)
                    shown.Intersect(clip.Bounds);
                var up = visual.TransformToAncestor((Visual)VisualTreeHelper.GetParent(visual));
                ink = up.TransformBounds(ink);
                shown = shown.IsEmpty ? shown : up.TransformBounds(shown);
            }
            if (!shown.IsEmpty)
                shown.Intersect(new Rect(-0.5, -0.5, widget.Width + 1, widget.Height + 1));
            if (hidden || shown.IsEmpty)
                continue;
            if (shown.Width < ink.Width - 0.5 || shown.Height < ink.Height - 0.5)
                cut.Add($"{text.Text}: {ink} shows as {shown}");
        }
        return cut;
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

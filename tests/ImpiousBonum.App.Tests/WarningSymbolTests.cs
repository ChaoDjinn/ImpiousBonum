using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

/// <summary>With the theme's warning symbols on, a value in a warning colour has a symbol too, so it doesn't rely on colour alone.</summary>
public sealed class WarningSymbolTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    private static readonly Theme Symbols = Theme.From(new ThemeSettings { WarningSymbols = true });

    private static JsonArray Rules(string warning = "warning", string critical = "critical") =>
        [new JsonObject { ["above"] = 80, ["color"] = warning }, new JsonObject { ["above"] = 90, ["color"] = critical }];

    private static MetricStore Store(double value)
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("x", "X", "Test", MetricUnit.Percent, 100));
        store.Set("x", value);
        return store;
    }

    [Theory]
    [InlineData("""{ "type": "text", "text": "{x}" }""")]
    [InlineData("""{ "type": "text", "text": "{x}", "align": "right", "shrinkToFit": false }""")]
    [InlineData("""{ "type": "icon", "text": "{x}" }""")]
    [InlineData("""{ "type": "stat", "metric": "x" }""")]
    [InlineData("""{ "type": "graph", "metric": "x" }""")]
    [InlineData("""{ "type": "graph", "metric": "x", "shrinkToFit": false }""")]
    [InlineData("""{ "type": "bars", "bars": [{ "label": "X", "metric": "x" }] }""")]
    public void A_value_has_a_symbol_only_while_it_is_in_the_warning_or_critical_colour(string json)
    {
        Sta.Run(() =>
        {
            var definition = JsonNode.Parse(json)!.AsObject();
            definition["thresholds"] = Rules();
            var store = Store(50);
            var widget = WidgetFactory.Create(definition, Symbols);
            var symbol = Assert.Single(Descendants<WarningSymbol>(widget));

            (double Value, ThresholdLevel Level, Brush? Brush)[] steps =
                [(50, ThresholdLevel.None, null), (85, ThresholdLevel.Warning, Symbols.Warning), (95, ThresholdLevel.Critical, Symbols.Critical), (50, ThresholdLevel.None, null)];
            foreach (var (value, level, brush) in steps)
            {
                store.Set("x", value);
                widget.Refresh(store, Evening);
                Assert.Equal(level, symbol.Level);
                Assert.Equal(level == ThresholdLevel.None ? Visibility.Collapsed : Visibility.Visible, symbol.Visibility);
                if (brush is not null)
                    Assert.Same(brush, symbol.Brush);
            }

            // Off, the default, the widget is built as it always was.
            Assert.Empty(Descendants<WarningSymbol>(WidgetFactory.Create(definition, Theme.From(new ThemeSettings()))));
        });
    }

    [Fact]
    public void Only_the_themes_warning_and_critical_colours_have_a_symbol()
    {
        Sta.Run(() =>
        {
            // What a colour of the layout's own means, only its author knows.
            var own = WidgetFactory.Create(new JsonObject { ["type"] = "text", ["text"] = "{x}", ["thresholds"] = Rules("#00FF00", "accent") }, Symbols);
            var symbol = Assert.Single(Descendants<WarningSymbol>(own));
            own.Refresh(Store(85), Evening);
            Assert.Equal(ThresholdLevel.None, symbol.Level);
            own.Refresh(Store(95), Evening);
            Assert.Equal(ThresholdLevel.None, symbol.Level);

            // A rule with no colour, or one that isn't a colour, draws in the critical colour, so it's marked as critical.
            var unset = WidgetFactory.Create(new JsonObject { ["type"] = "text", ["text"] = "{x}", ["thresholds"] = Rules("WARNING", "not a colour") }, Symbols);
            symbol = Assert.Single(Descendants<WarningSymbol>(unset));
            unset.Refresh(Store(85), Evening);
            Assert.Equal(ThresholdLevel.Warning, symbol.Level);
            unset.Refresh(Store(95), Evening);
            Assert.Equal(ThresholdLevel.Critical, symbol.Level);
        });
    }

    [Fact]
    public void A_gauge_draws_its_symbol_after_the_value_only_with_the_setting_on()
    {
        Sta.Run(() =>
        {
            foreach (var (theme, on) in new[] { (Symbols, true), (Theme.From(new ThemeSettings()), false) })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "gauge", ["metric"] = "x", ["thresholds"] = Rules() }, theme);
                var gauge = Assert.IsType<GaugeElement>(Assert.Single(widget.Children.Cast<object>()));
                var store = Store(50);
                widget.Refresh(store, Evening);
                Assert.Equal(ThresholdLevel.None, gauge.Symbol);
                store.Set("x", 85);
                widget.Refresh(store, Evening);
                Assert.Equal(on ? ThresholdLevel.Warning : ThresholdLevel.None, gauge.Symbol);
                store.Set("x", 95);
                widget.Refresh(store, Evening);
                Assert.Equal(on ? ThresholdLevel.Critical : ThresholdLevel.None, gauge.Symbol);
            }
        });
    }

    [Fact]
    public void Each_drive_and_app_has_its_own_symbol_and_cpu_threads_keep_to_colour()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            DemoReadings.Fill(store, Evening, DemoArtwork.Png(64));

            // The demo's D: is 69% full, C: and E: under half.
            var drives = WidgetFactory.Create(new JsonObject { ["type"] = "drives", ["thresholds"] = new JsonArray(new JsonObject { ["above"] = 60, ["color"] = "warning" }) }, Symbols);
            drives.Refresh(store, Evening);
            Assert.Equal([ThresholdLevel.None, ThresholdLevel.Warning, ThresholdLevel.None], Descendants<WarningSymbol>(drives).Select(s => s.Level));

            AppUsage[] running = [new("game", 1, 40, 2_000), new("chrome", 12, 10, 3_000), new("notepad", 1, 0, 100)];
            store.SetAttachment(ProcessProvider.Apps, running);
            var apps = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "processes", ["count"] = 3,
                ["thresholds"] = new JsonArray(new JsonObject { ["above"] = 5, ["color"] = "warning" }, new JsonObject { ["above"] = 30, ["color"] = "critical" }),
            }, Symbols);
            apps.Refresh(store, Evening);
            Assert.Equal([ThresholdLevel.Critical, ThresholdLevel.Warning, ThresholdLevel.None], Descendants<WarningSymbol>(apps).Select(s => s.Level));

            // A symbol in every cell would be too busy.
            var cores = WidgetFactory.Create(new JsonObject { ["type"] = "cores", ["thresholds"] = new JsonArray(new JsonObject { ["above"] = 50, ["color"] = "warning" }) }, Symbols);
            cores.Refresh(store, Evening);
            Assert.Empty(Descendants<WarningSymbol>(cores));
        });
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    public void The_symbol_sits_beside_the_text_and_text_that_only_just_fits_shrinks_to_make_room(string align)
    {
        Sta.Run(() =>
        {
            var definition = new JsonObject { ["type"] = "text", ["text"] = "{x}", ["fontSize"] = 60, ["align"] = align, ["thresholds"] = Rules() };
            var store = Store(50);
            var widget = WidgetFactory.Create(definition, Symbols);
            widget.Refresh(store, Evening);
            var text = Descendants<System.Windows.Controls.TextBlock>(widget).Single();
            var fit = Descendants<ShrinkToFit>(widget).Single();
            Lay(widget, 1000, 100);
            var width = Math.Ceiling(text.ActualWidth) + 4;
            Lay(widget, width, 100);
            Assert.Equal(1, fit.Scale);
            var before = Bounds(text, widget);

            store.Set("x", 85);
            widget.Refresh(store, Evening);
            Lay(widget, width, 100);
            var symbol = Descendants<WarningSymbol>(widget).Single();
            Assert.InRange(fit.Scale, 0.3, 0.99);
            var ink = ShrinkToFit.Ink(widget);
            Assert.True(new Rect(-1, -1, width + 2, 102).Contains(ink), $"{ink} is outside {width}×100");

            // Before right-aligned text, after the rest, and level with it.
            var (shown, value) = (Bounds(symbol, widget), Bounds(text, widget));
            Assert.True(align == "right" ? shown.Right <= value.Left : shown.Left >= value.Right, $"{shown} isn't beside {value}");
            Assert.InRange(shown.Top + shown.Height / 2, value.Top, value.Bottom);
            Assert.True(before.Width > value.Width);
        });
    }

    [Fact]
    public void A_gauges_value_shrinks_to_make_room_for_its_symbol()
    {
        Sta.Run(() =>
        {
            double Shown(Theme theme)
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "gauge", ["metric"] = "x", ["fontSize"] = 300, ["thresholds"] = Rules() }, theme);
                widget.Refresh(Store(85), Evening);
                Lay(widget, 260, 240);
                return Assert.IsType<GaugeElement>(Assert.Single(widget.Children.Cast<object>())).ShownValueSize;
            }

            Assert.True(Shown(Symbols) < Shown(Theme.From(new ThemeSettings())));
        });
    }

    [Fact]
    public void The_two_symbols_are_different_shapes_of_the_same_width()
    {
        Rect Outline(ThresholdLevel level)
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
                WarningSymbol.Draw(dc, level, Brushes.White, new Point(50, 50), 20);
            return drawing.Bounds;
        }

        Sta.Run(() =>
        {
            var warning = Outline(ThresholdLevel.Warning);
            var critical = Outline(ThresholdLevel.Critical);
            Assert.True(Outline(ThresholdLevel.None).IsEmpty);
            // Both fit the room kept for either, centred on the same point.
            Assert.All(new[] { warning, critical }, r => Assert.True(r.Width <= WarningSymbol.WidthFor(20) + 0.01 && Math.Abs(r.Left + r.Width / 2 - 50) < 0.01));
            // A triangle is widest at its base, a diamond in its middle.
            Assert.NotEqual(warning, critical);
        });
    }

    private static void Lay(UIElement element, double width, double height)
    {
        if (element is FrameworkElement framework)
            (framework.Width, framework.Height) = (width, height);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>Where <paramref name="element"/> is drawn in <paramref name="root"/>, shrinking included.</summary>
    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

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

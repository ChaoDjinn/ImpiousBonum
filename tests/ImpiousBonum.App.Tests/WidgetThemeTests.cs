using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

/// <summary>Any widget can have its own font and colours instead of the layout theme's.</summary>
public sealed class WidgetThemeTests
{
    [Fact]
    public void Every_widget_offers_its_own_theme()
    {
        Assert.All(WidgetFactory.Descriptors, d =>
        {
            foreach (var key in new[] { "fontFamily", "fontWeight", "foreground", "secondary", "accent", "track", "warning", "critical" })
                Assert.Contains(d.Settings, s => s.Key == key && s.Group == Setting.WidgetTheme);
        });
    }

    [Fact]
    public void A_widget_with_no_overrides_shares_the_layouts_theme()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            Assert.Same(theme, theme.WithOverrides(new JsonObject { ["type"] = "text", ["foreground"] = "", ["fontWeight"] = "theme" }));
        });
    }

    [Fact]
    public void Overrides_replace_only_what_they_name()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings { Accent = "#FF9800", Accent2 = "#FFFFFF", Secondary = "#808080" });
            var own = theme.WithOverrides(new JsonObject { ["foreground"] = "#FF0000", ["accent"] = "#00FF00", ["fontWeight"] = "Bold", ["fontFamily"] = "Consolas" });

            Assert.Equal(Colors.Red, ((SolidColorBrush)own.Foreground).Color);
            // A plain accent, not the layout's gradient.
            Assert.Equal(Colors.Lime, Assert.IsType<SolidColorBrush>(own.Accent).Color);
            Assert.Equal(((SolidColorBrush)theme.Secondary).Color, ((SolidColorBrush)own.Secondary).Color);
            Assert.Equal(FontWeights.Bold, own.FontWeight);
            Assert.StartsWith("Consolas", own.FontFamily.Source);
        });
    }

    [Fact]
    public void A_text_widget_draws_in_its_own_colour_and_named_colours_mean_its_own()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var store = new MetricStore();
            var plain = WidgetFactory.Create(new JsonObject { ["type"] = "text", ["text"] = "Hi" }, theme);
            var own = WidgetFactory.Create(new JsonObject { ["type"] = "text", ["text"] = "Hi", ["foreground"] = "#FF0000" }, theme);
            var accent = WidgetFactory.Create(new JsonObject { ["type"] = "text", ["text"] = "Hi", ["color"] = "accent", ["accent"] = "#0000FF" }, theme);
            foreach (var w in new[] { plain, own, accent })
                w.Refresh(store, DateTime.Now);

            Assert.NotEqual(Colors.Red, Text(plain));
            Assert.Equal(Colors.Red, Text(own));
            Assert.Equal(Colors.Blue, Text(accent));
        });
    }

    [Fact]
    public void The_validator_takes_overrides_and_empty_colours()
    {
        var layout = JsonSerializer.Deserialize<LayoutDocument>(
            """
            { "widgets": [
              { "type": "clock", "x": 0, "y": 0, "width": 100, "height": 50, "foreground": "#FF0000", "accent": "", "fontWeight": "theme" },
              { "type": "gauge", "x": 0, "y": 50, "width": 100, "height": 50, "track": "not a colour" }
            ] }
            """,
            JsonDefaults.Options)!;
        var issue = Assert.Single(LayoutValidator.Validate(layout));
        Assert.Contains("'track'", issue);
    }

    private static Color Text(Widget widget)
    {
        var block = Descendants(widget).OfType<TextBlock>().First(t => t.Text == "Hi");
        return ((SolidColorBrush)block.Foreground).Color;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }
}

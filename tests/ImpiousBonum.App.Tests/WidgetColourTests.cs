using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

/// <summary>Widgets that used to borrow the theme's bar track for other things have colours of their own.</summary>
public sealed class WidgetColourTests
{
    private static readonly Theme RedTrack = Theme.From(new ThemeSettings { Track = "#FF0000" });

    [Fact]
    public void Changing_the_themes_bar_track_leaves_a_media_widget_with_its_own_colours_alone()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "media", ["hideIdle"] = false, ["artColor"] = "#00FF00", ["trackColor"] = "#0000FF",
            }, RedTrack);
            widget.Refresh(new MetricStore(), DateTime.Now);

            var colours = Descendants<Panel>(widget).Select(p => p.Background).Concat(Descendants<Border>(widget).Select(b => b.Background))
                .OfType<SolidColorBrush>().Select(b => b.Color).ToList();
            Assert.Contains(Colors.Lime, colours);
            Assert.Contains(Colors.Blue, colours);
            Assert.DoesNotContain(Colors.Red, colours);
        });
    }

    [Fact]
    public void By_default_they_still_follow_the_theme()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "media", ["hideIdle"] = false }, RedTrack);
            var colours = Descendants<Border>(widget).Select(b => b.Background).OfType<SolidColorBrush>().Select(b => b.Color);
            Assert.Contains(Colors.Red, colours);
        });
    }

    [Theory]
    [InlineData("bars")]
    [InlineData("claude")]
    [InlineData("drives")]
    [InlineData("gauge")]
    [InlineData("cores")]
    [InlineData("buttons")]
    public void Every_widget_that_had_the_bar_track_has_its_own_setting(string type)
    {
        var descriptor = WidgetFactory.Find(type)!;
        Assert.Contains(descriptor.Settings, s => s.Key is "trackColor" or "tileColor" && (string?)s.Default == "track");
    }

    [Fact]
    public void A_bar_takes_its_own_track_and_fill()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition("x", "X", "Test", MetricUnit.Percent, 100));
            store.Set("x", 50);
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "bars", ["trackColor"] = "#0000FF", ["color"] = "#00FF00",
                ["bars"] = new JsonArray(new JsonObject { ["label"] = "X", ["metric"] = "x" }),
            }, RedTrack);
            widget.Refresh(store, DateTime.Now);

            Assert.Contains(Descendants<Grid>(widget), g => g.Background is SolidColorBrush { Color: var c } && c == Colors.Blue);
            Assert.Contains(Descendants<Border>(widget), b => b.Background is SolidColorBrush { Color: var c } && c == Colors.Lime);
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

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Tests;

public sealed class WidgetSetupTests
{
    private static List<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                found.Add(match);
            found.AddRange(All<T>(child));
        }
        return found;
    }

    private static PropertyPanel Panel(string type, Func<WidgetDescriptor, IReadOnlyList<WidgetSetup>>? setups)
    {
        var session = new LayoutSession(new LayoutDocument
        {
            Widgets = [new JsonObject { ["type"] = type, ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 100 }],
        }, new ThemeLibrary(null));
        session.Select(0);
        return new PropertyPanel(session, () => null, () => null) { Setups = setups };
    }

    [Fact]
    public void A_selected_widget_shows_its_setup_with_whats_set_up_now()
    {
        Sta.Run(() =>
        {
            string? place = null;
            Window? openedOver = null;
            IReadOnlyList<WidgetSetup> Setups(WidgetDescriptor descriptor) => descriptor == WeatherWidget.Descriptor
                ? [new(place is null ? "Set location…" : "Change location…", () => place is null ? "No location yet." : $"Forecasts for {place}.", owner =>
                {
                    openedOver = owner;
                    place = "Leeds";
                })]
                : [];

            var panel = Panel("weather", Setups);
            panel.Rebuild();
            var window = new Window { Content = panel };
            var texts = All<TextBlock>(panel).Select(t => t.Text).ToList();
            Assert.Contains("Set up", texts);
            Assert.Contains("No location yet.", texts);

            var button = All<Button>(panel).Single(b => Equals(b.Content, "Set location…"));
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Same(window, openedOver);
            Assert.Contains("Forecasts for Leeds.", All<TextBlock>(panel).Select(t => t.Text));
            Assert.Contains(All<Button>(panel), b => Equals(b.Content, "Change location…"));
        });
    }

    [Fact]
    public void Widgets_with_nothing_to_set_up_have_no_setup_group()
    {
        Sta.Run(() =>
        {
            var panel = Panel("text", _ => []);
            panel.Rebuild();
            Assert.DoesNotContain("Set up", All<TextBlock>(panel).Select(t => t.Text));
        });
    }
}

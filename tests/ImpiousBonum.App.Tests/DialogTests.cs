using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Tests;

public sealed class DialogTests
{
    [Fact]
    public void Placeholders_show_only_while_the_box_or_list_is_empty()
    {
        Sta.Run(() =>
        {
            var box = new TextBox();
            Placeholder.SetText(box, "Search metrics");
            Assert.True(Placeholder.IsShowing(box));
            box.Text = "gpu";
            Assert.False(Placeholder.IsShowing(box));

            var list = new ListBox();
            Assert.False(Placeholder.IsShowing(list));
            Placeholder.SetText(list, "No alerts yet");
            Assert.True(Placeholder.IsShowing(list));
            list.Items.Add("gpu.hotspot above 95");
            Assert.False(Placeholder.IsShowing(list));
        });
    }

    [Fact]
    public void A_problem_message_waits_for_input_without_moving_anything()
    {
        Sta.Run(() =>
        {
            var text = new TextBlock();
            Dialog.SetShowAfterInput(text, true);
            Assert.Equal(Visibility.Hidden, text.Visibility);
        });
    }

    [Fact]
    public void Close_buttons_close_windows_that_arent_dialogs()
    {
        Sta.Run(() =>
        {
            // WPF's IsCancel alone only closes windows shown with ShowDialog, which left these Close buttons doing nothing.
            foreach (var window in new Window[] { new AlertsWindow(new AppSettings(), () => null), new CustomDataWindow(new AppSettings(), _ => null), new AboutWindow("1.0.0", "Running") })
            {
                var closed = false;
                window.Closed += (_, _) => closed = true;
                var close = Buttons(window).Single(b => b.IsCancel);
                Assert.True(Dialog.GetClosesWindow(close), $"{window.GetType().Name}'s Close button doesn't close it");

                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(closed, $"{window.GetType().Name} didn't close");
            }
        });
    }

    [Fact]
    public void Every_widget_is_in_a_group_and_the_groups_dont_split()
    {
        Assert.All(WidgetFactory.Descriptors, d => Assert.False(string.IsNullOrEmpty(d.Category), d.Type));
        var order = WidgetFactory.Descriptors.Select(d => d.Category).ToList();
        var groups = WidgetFactory.Descriptors.GroupBy(d => d.Category).Select(g => g.Key).ToList();
        Assert.Equal(groups, order.Where((c, i) => i == 0 || order[i - 1] != c));
    }

    [Theory]
    [InlineData("Icon and value icon", "icon-and-value-icon")]
    [InlineData("Multi-line graph lines", "multi-line-graph-lines")]
    [InlineData("Weather, news and Claude", "weather-news-and-claude")]
    public void Widget_reference_links_match_GitHubs_headings(string heading, string anchor) =>
        Assert.Equal(anchor, WidgetDocs.Anchor(heading));

    [Fact]
    public void The_widget_list_shows_each_widgets_type_and_what_tells_it_apart()
    {
        var graph = EditorWindow.ListEntry.Of(new JsonObject { ["type"] = "graph", ["label"] = "CPU" });
        Assert.Equal(("Graph", " CPU"), (graph.Name, graph.Detail));
        Assert.Equal("Graph: CPU", graph.ToolTip);

        var clock = EditorWindow.ListEntry.Of(new JsonObject { ["type"] = "clock" });
        Assert.Equal(("Clock", ""), (clock.Name, clock.Detail));

        Assert.Equal("Unknown (dial)", EditorWindow.ListEntry.Of(new JsonObject { ["type"] = "dial" }).Name);
    }

    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button button)
                yield return button;
            foreach (var inner in Buttons(child))
                yield return inner;
        }
    }
}

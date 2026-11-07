using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class ProcessesWidgetTests
{
    private static readonly AppUsage[] Apps =
    [
        new("game", 1, 40, 2_000),
        new("chrome", 12, 10, 3_000),
        new("notepad", 1, 0, 100),
    ];

    [Fact]
    public void Rows_follow_the_chosen_sort()
    {
        Assert.Equal(["game", "chrome"], ProcessesWidget.Top(Apps, "cpu", 2).Select(a => a.Name));
        Assert.Equal(["chrome", "game", "notepad"], ProcessesWidget.Top(Apps, "memory", 5).Select(a => a.Name));
        Assert.Empty(ProcessesWidget.Top(null, "cpu", 5));
    }

    [Fact]
    public void By_GPU_only_the_apps_using_it_are_ranked()
    {
        AppUsage[] apps = [new("game", 1, 40, 2_000, Gpu: 88), new("chrome", 12, 10, 3_000, Gpu: 7.5), new("notepad", 1, 0, 100)];

        Assert.Equal(["game", "chrome"], ProcessesWidget.Top(apps, "gpu", 5).Select(a => a.Name));
    }

    [Fact]
    public void The_GPU_column_has_headings_over_the_numbers_and_is_on_when_sorted_by_GPU()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(ProcessProvider.Apps, new AppUsage[] { new("game", 1, 40, 2_000_000_000, Gpu: 88) });
            static List<string> Texts(System.Windows.DependencyObject root) =>
                [.. Descendants(root).OfType<System.Windows.Controls.TextBlock>().Where(t => t.IsVisible || t.Visibility == System.Windows.Visibility.Visible).Select(t => t.Text)];

            var plain = WidgetFactory.Create(new JsonObject { ["type"] = "processes", ["count"] = 1, ["shrinkToFit"] = false }, Theme.From(new ThemeSettings()));
            plain.Refresh(store, DateTime.Now);
            Assert.Equal(["game", "40.0 %", "2 GB"], Texts(plain));

            foreach (var settings in new[] { new JsonObject { ["gpu"] = true }, new JsonObject { ["sort"] = "gpu" } })
            {
                settings["type"] = "processes";
                settings["count"] = 1;
                settings["shrinkToFit"] = false;
                var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
                Assert.Equal(["CPU", "GPU", "Memory", "game", "40.0 %", "88.0 %", "2 GB"], Texts(widget));
            }
        });
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }

    [Fact]
    public void The_widget_shows_apps_and_copes_without_them()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(ProcessProvider.Apps, Apps);
            foreach (var columns in new[] { "both", "cpu", "memory" })
            {
                var widget = WidgetFactory.Create(
                    new JsonObject { ["type"] = "processes", ["sort"] = "memory", ["columns"] = columns, ["count"] = 2 },
                    Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
                widget.Refresh(new MetricStore(), DateTime.Now);
            }
        });
    }
}

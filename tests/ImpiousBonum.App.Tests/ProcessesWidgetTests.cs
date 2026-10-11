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
        Assert.Equal(["game", "chrome"], ProcessesWidget.Top(Apps, byMemory: false, 2).Select(a => a.Name));
        Assert.Equal(["chrome", "game", "notepad"], ProcessesWidget.Top(Apps, byMemory: true, 5).Select(a => a.Name));
        Assert.Empty(ProcessesWidget.Top(null, byMemory: false, 5));
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

using System.Text.Json.Nodes;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class DrivesWidgetTests
{
    [Fact]
    public void Each_row_shows_its_own_drive_in_label_and_value()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition(DriveProvider.Letters, "letters", "Disk", MetricUnit.Text));
            store.SetText(DriveProvider.Letters, "C,D");
            foreach (var (letter, life, model) in new[] { ("C", 98.0, "Fast SSD"), ("D", 71.0, "Big HDD") })
            {
                store.Register(new MetricDefinition($"disk.{letter}.life", "life", "Disk", MetricUnit.Percent, 100));
                store.Register(new MetricDefinition($"disk.{letter}.model", "model", "Disk", MetricUnit.Text));
                store.Register(new MetricDefinition($"disk.{letter}.usedPct", "used", "Disk", MetricUnit.Percent, 100));
                store.Set($"disk.{letter}.life", life);
                store.SetText($"disk.{letter}.model", model);
            }

            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "drives",
                ["label"] = "{letter}: {model}",
                ["text"] = "{life:0} life · {disk.*.life:0.0}",
            }, Theme.From(new ThemeSettings()));
            widget.Refresh(store, DateTime.Now);

            var rows = ((StackPanel)widget.Children[0]).Children.Cast<StackPanel>().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(["C: Fast SSD", "D: Big HDD"], rows.Select(r => Texts(r)[0]));
            Assert.Equal(["98 % life · 98.0 %", "71 % life · 71.0 %"], rows.Select(r => Texts(r)[1]));
        });
    }

    private static List<string> Texts(StackPanel row) => ((Grid)row.Children[0]).Children.OfType<TextBlock>().Select(t => t.Text).ToList();
}

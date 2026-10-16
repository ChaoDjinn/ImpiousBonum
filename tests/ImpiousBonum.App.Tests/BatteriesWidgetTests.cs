using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class BatteriesWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    [Fact]
    public void Each_device_has_a_row_with_its_charge_and_a_bar_in_the_critical_colour_when_low()
    {
        Sta.Run(() =>
        {
            var (store, _) = Devices(new("Headset", 72), new("Mouse", 18), new("Controller", 55));
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "batteries" }, Plain);
            widget.Refresh(store, DateTime.Now);

            var rows = Rows(widget);
            Assert.Equal(["Controller", "Headset", "Mouse"], rows.Select(r => Texts(r)[0]));
            Assert.Equal(["55 %", "72 %", "18 %"], rows.Select(r => Texts(r)[1]));
            Assert.Equal(72, Bar(rows[1]).ColumnDefinitions[0].Width.Value);
            Assert.Equal(28, Bar(rows[1]).ColumnDefinitions[1].Width.Value);

            // Under 20 % without being told: the mouse's bar and charge are the critical colour.
            Assert.Equal([Plain.Accent, Plain.Accent, Plain.Critical], rows.Select(r => Fill(r).Background));
            Assert.Equal([Plain.Secondary, Plain.Secondary, Plain.Critical], rows.Select(r => Value(r).Foreground));
        });
    }

    [Fact]
    public void Rows_follow_devices_as_they_connect_and_disconnect()
    {
        Sta.Run(() =>
        {
            var (store, fake) = Devices(new DeviceBattery("Headset", 72));
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "batteries" }, Plain);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(["Headset"], Rows(widget).Select(r => Texts(r)[0]));
            Assert.Equal(Visibility.Visible, widget.Visibility);

            fake.Devices = [new("Headset", 71), new("Mouse", 64)];
            Sample(fake.Provider, store);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(["Headset", "Mouse"], Rows(widget).Select(r => Texts(r)[0]));
            Assert.Equal(["71 %", "64 %"], Rows(widget).Select(r => Texts(r)[1]));

            // None connected: nothing shows, but it's there to place while editing.
            fake.Devices = [];
            Sample(fake.Provider, store);
            widget.Refresh(store, DateTime.Now);
            Assert.Empty(Rows(widget));
            Assert.Equal(Visibility.Collapsed, widget.Visibility);
            widget.Editing = true;
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Fact]
    public void Chosen_devices_show_in_the_order_chosen_while_connected()
    {
        Sta.Run(() =>
        {
            var (store, _) = Devices(new("Headset", 72), new("Mouse", 18), new("Controller", 55));
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "batteries", ["devices"] = "mouse, Keyboard, Headset" }, Plain);
            widget.Refresh(store, DateTime.Now);
            // The keyboard isn't connected.
            Assert.Equal(["Mouse", "Headset"], Rows(widget).Select(r => Texts(r)[0]));
        });
    }

    [Fact]
    public void Warning_colours_can_be_changed_or_left_out()
    {
        Sta.Run(() =>
        {
            var (store, _) = Devices(new("Headset", 30), new("Mouse", 18));
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "batteries",
                ["thresholds"] = new JsonArray(new JsonObject { ["below"] = 50, ["color"] = "warning" }),
            }, Plain);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal([Plain.Warning, Plain.Warning], Rows(widget).Select(r => Fill(r).Background));

            widget = WidgetFactory.Create(new JsonObject { ["type"] = "batteries", ["thresholds"] = new JsonArray() }, Plain);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal([Plain.Accent, Plain.Accent], Rows(widget).Select(r => Fill(r).Background));
        });
    }

    [Fact]
    public void A_low_device_has_a_symbol_with_warning_symbols_on()
    {
        Sta.Run(() =>
        {
            var (store, _) = Devices(new("Headset", 72), new("Mouse", 18));
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "batteries" }, Theme.From(new ThemeSettings { WarningSymbols = true }));
            widget.Refresh(store, DateTime.Now);
            Assert.Equal([ThresholdLevel.None, ThresholdLevel.Critical], Descendants<WarningSymbol>(widget).Select(s => s.Level));
        });
    }

    /// <summary>A store with these devices' batteries in it, as the provider writes them, and a way to change them.</summary>
    private static (MetricStore Store, FakeDevices Devices) Devices(params DeviceBattery[] devices)
    {
        var fake = new FakeDevices { Devices = devices };
        var store = new MetricStore();
        Sample(fake.Provider, store);
        return (store, fake);
    }

    private static void Sample(BatteryProvider provider, MetricStore store) =>
        provider.SampleAsync(store, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private static List<StackPanel> Rows(Widget widget) =>
        ((StackPanel)((ShrinkToFit)widget.Children[0]).Child).Children.Cast<StackPanel>().ToList();

    private static List<TextBlock> TextBlocks(StackPanel row) =>
        ((Grid)((ShrinkToFit)row.Children[0]).Child).Children.OfType<TextBlock>().ToList();

    private static List<string> Texts(StackPanel row) => TextBlocks(row).Select(t => t.Text).ToList();

    private static TextBlock Value(StackPanel row) => TextBlocks(row)[1];

    private static Grid Bar(StackPanel row) => (Grid)row.Children[1];

    private static Border Fill(StackPanel row) => (Border)Bar(row).Children[0];

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

    private sealed class FakeDevices
    {
        public FakeDevices() => Provider = new BatteryProvider(() => Devices);

        public IReadOnlyList<DeviceBattery> Devices { get; set; } = [];

        public BatteryProvider Provider { get; }
    }
}

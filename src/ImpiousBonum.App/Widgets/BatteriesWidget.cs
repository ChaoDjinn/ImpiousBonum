using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// One row per wireless device with a battery: its name, how much charge it has left, and a bar, in the critical colour
/// once it's low. Rows follow devices as they connect and disconnect, and the widget hides while there are none.
/// </summary>
public sealed class BatteriesWidget : Widget
{
    /// <summary>The names the editor offers for <c>devices</c>: every device seen since the app started.</summary>
    public const string DeviceNames = "batteryDevices";

    public static WidgetDescriptor Descriptor { get; } = new(
        "batteries", "Device batteries",
        "One row per wireless device, such as a Bluetooth headset, mouse, keyboard or game controller, with how much charge it has left and a bar, which turns the critical colour under 20 %. Follows devices as they connect and disconnect, and shows nothing while there are none.",
        425, 230,
        [
            Setting.Picks("devices", "Devices", PickList.AllValue, source: DeviceNames, all: "All devices",
                none: "No devices with a battery yet. Connect one and it shows here within a minute.",
                help: "All devices, or just the ones chosen, in the order they're listed. Only those connected now show."),
            Setting.Number("fontSize", "Font size", 25, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("barHeight", "Bar height", 5, 1, 100, group: Setting.Appearance),
            Setting.Number("spacing", "Row spacing", 12, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Bar colour", "accent"),
            Setting.Color("trackColor", "Track colour", "track", help: "The empty part of each bar. track is the theme's bar track."),
            Setting.Color("labelColor", "Label colour", "foreground"),
            Setting.Color("valueColor", "Value colour", "secondary"),
            Setting.Thresholds(
                $"Changes each device's bar and charge. An empty metric uses that device's charge (battery.headset for one called Headset). Starts with one: under {BatteryProvider.LowBelow}, the critical colour.",
                defaults: [new JsonObject { ["below"] = BatteryProvider.LowBelow, ["color"] = "critical" }]),
        ],
        (settings, theme) => new BatteriesWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly PickList _devices;
    private readonly double _fontSize;
    private readonly double _barHeight;
    private readonly double _spacing;
    private readonly ThresholdColors _thresholds;
    private readonly Brush _fill;
    private readonly Brush _track;
    private readonly Brush _label;
    private readonly Brush _value;
    private readonly List<Row> _current = [];
    private string? _shown;

    public BatteriesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _devices = settings.Picks("devices");
        _fontSize = settings.Number("fontSize");
        _barHeight = settings.Number("barHeight");
        _spacing = settings.Number("spacing");
        _thresholds = new ThresholdColors(settings, theme);
        _fill = theme.Resolve(settings.String("color"), theme.Accent);
        _track = theme.Resolve(settings.String("trackColor"), theme.Track);
        _label = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _value = theme.Resolve(settings.String("valueColor"), theme.Secondary);
        Children.Add(Fit(_rows, FitDirection.Height));
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var shown = Pick(store.GetAttachment(BatteryProvider.Devices) as IReadOnlyList<BatteryLevel>, _devices);
        // Devices come and go a minute at a time: rebuild only when the ones on show do.
        var key = string.Join("\n", shown.Select(d => $"{d.Slug}|{d.Name}"));
        if (key != _shown)
        {
            _shown = key;
            Rebuild(shown);
        }
        SetUnavailable(shown.Count == 0);

        foreach (var row in _current)
        {
            var id = BatteryProvider.LevelId(row.Slug);
            var has = store.TryGet(id, out var sample);
            row.Value.Text = has ? MetricFormatter.Format(sample) : MetricFormatter.Missing;
            var level = has && sample.Value is double v ? Math.Clamp(v, 0, 100) : 0;
            row.Bar.ColumnDefinitions[0].Width = new GridLength(level, GridUnitType.Star);
            row.Bar.ColumnDefinitions[1].Width = new GridLength(100 - level, GridUnitType.Star);
            row.Fill.Background = _thresholds.Pick(store, id, _fill);
            row.Value.Foreground = _thresholds.Pick(store, id, _value);
        }
    }

    /// <summary>The devices to show: every connected one, by name, or the connected ones chosen, in the order chosen.</summary>
    internal static IReadOnlyList<BatteryLevel> Pick(IReadOnlyList<BatteryLevel>? levels, PickList chosen)
    {
        if (levels is null)
            return [];
        if (chosen.All)
            return levels;
        return chosen.Names.Select(name => levels.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<BatteryLevel>().ToList();
    }

    private void Rebuild(IReadOnlyList<BatteryLevel> levels)
    {
        _rows.Children.Clear();
        _current.Clear();

        foreach (var device in levels)
        {
            var label = CreateText(_fontSize, _label);
            label.Text = device.Name;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var value = CreateText(_fontSize * 0.9, _value, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            var header = LabelAndValue(label, value);

            var bar = new Grid { Height = _barHeight, Background = _track, Margin = new Thickness(0, 2, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) });
            var fill = new Border { Background = _fill };
            bar.Children.Add(fill);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, _spacing) };
            row.Children.Add(header);
            row.Children.Add(bar);
            _rows.Children.Add(row);

            _current.Add(new Row(device.Slug, value, bar, fill));
        }
    }

    private sealed record Row(string Slug, TextBlock Value, Grid Bar, Border Fill);
}

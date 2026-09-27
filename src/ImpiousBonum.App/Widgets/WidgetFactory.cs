using System.Text.Json.Nodes;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

public static class WidgetFactory
{
    /// <summary>Every widget type, in the order the editor offers them.</summary>
    public static readonly IReadOnlyList<WidgetDescriptor> Descriptors =
    [
        TextWidget.Descriptor,
        GraphWidget.Descriptor,
        IconValueWidget.Descriptor,
        ClockWidget.Descriptor,
        DrivesWidget.Descriptor,
        RowsWidget.Descriptor,
    ];

    public static WidgetDescriptor? Find(string? type) =>
        Descriptors.FirstOrDefault(d => d.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static Widget Create(JsonObject definition, Theme theme)
    {
        var type = definition.GetString("type");
        return Find(type) is { } descriptor
            ? descriptor.Create(new WidgetSettings(definition, descriptor.Settings), theme)
            : new UnknownWidget(type, theme);
    }

    /// <summary>Placeholder that makes a typo in layout.json visible instead of silently dropping the widget.</summary>
    private sealed class UnknownWidget : Widget
    {
        public UnknownWidget(string? type, Theme theme) : base(new WidgetSettings([], []), theme)
        {
            var text = CreateText(20, Brushes.IndianRed);
            text.Text = $"Unknown widget type '{type}'";
            Children.Add(text);
        }

        public override void Refresh(MetricStore store, DateTime now)
        {
        }
    }
}

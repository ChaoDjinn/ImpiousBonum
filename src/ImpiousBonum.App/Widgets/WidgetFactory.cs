using System.Text.Json.Nodes;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

public static class WidgetFactory
{
    public static readonly IReadOnlyList<string> Types = ["text", "graph", "icon", "clock", "drives", "rows"];

    public static Widget Create(JsonObject definition, Theme theme) => definition.GetString("type")?.ToLowerInvariant() switch
    {
        "text" => new TextWidget(definition, theme),
        "graph" => new GraphWidget(definition, theme),
        "icon" => new IconValueWidget(definition, theme),
        "clock" => new ClockWidget(definition, theme),
        "drives" => new DrivesWidget(definition, theme),
        "rows" => new RowsWidget(definition, theme),
        var other => new UnknownWidget(other, theme),
    };

    /// <summary>Placeholder that makes a typo in layout.json visible instead of silently dropping the widget.</summary>
    private sealed class UnknownWidget : Widget
    {
        public UnknownWidget(string? type, Theme theme) : base([], theme)
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

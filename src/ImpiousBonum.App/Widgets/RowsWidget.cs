using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A stack of label/value rows, e.g. Download / Upload / Ping.
/// Settings: <c>rows</c> (array of { label, text }), <c>fontSize</c>, <c>valueFontSize</c>, <c>align</c> (bottom stacks from the bottom).
/// </summary>
public sealed class RowsWidget : Widget
{
    private readonly List<(ValueTemplate Template, TextBlock Value)> _rows = [];

    public RowsWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        var fontSize = definition.GetDouble("fontSize", 28);
        var valueFontSize = definition.GetDouble("valueFontSize", fontSize * 0.85);
        var stack = new StackPanel
        {
            VerticalAlignment = definition.GetString("align")?.Equals("bottom", StringComparison.OrdinalIgnoreCase) == true
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top,
        };

        foreach (var row in definition.GetObjects("rows"))
        {
            var grid = new Grid();
            var label = CreateText(fontSize);
            label.Text = row.GetString("label") ?? string.Empty;
            var value = CreateText(valueFontSize, Theme.Secondary, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label);
            grid.Children.Add(value);
            stack.Children.Add(grid);
            _rows.Add((ValueTemplate.Parse(row.GetString("text")), value));
        }

        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        foreach (var (template, value) in _rows)
            value.Text = template.Render(store);
    }
}

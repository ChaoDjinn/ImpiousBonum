using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A stack of label/value rows, e.g. Download / Upload / Ping.</summary>
public sealed class RowsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "rows", "Rows", "A stack of label and value rows, e.g. Download / Upload / Ping.",
        425, 110,
        [
            Setting.Items("rows", "Rows",
            [
                Setting.PlainText("label", "Label", "Download"),
                Setting.Template("text", "Value", "{net.down}"),
            ]),
            Setting.Number("fontSize", "Label size", 28, 6, 200),
            Setting.Number("valueFontSize", "Value size", null, 6, 200, help: "Empty uses 85% of the label size."),
            Setting.Choice("align", "Stack from", "top", ["top", "bottom"], group: Setting.Appearance),
        ],
        (settings, theme) => new RowsWidget(settings, theme));

    private readonly List<(ValueTemplate Template, TextBlock Value)> _rows = [];

    public RowsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var fontSize = settings.Number("fontSize");
        var valueFontSize = settings.OptionalNumber("valueFontSize") ?? fontSize * 0.85;
        var stack = new StackPanel
        {
            VerticalAlignment = settings.String("align") == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Top,
        };

        foreach (var row in settings.Items("rows"))
        {
            var grid = new Grid();
            var label = CreateText(fontSize);
            label.Text = row.String("label");
            var value = CreateText(valueFontSize, Theme.Secondary, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label);
            grid.Children.Add(value);
            stack.Children.Add(grid);
            _rows.Add((ValueTemplate.Parse(row.String("text")), value));
        }

        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        foreach (var (template, value) in _rows)
            value.Text = template.Render(store);
    }
}

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// An accent-coloured icon followed by a value.
/// Settings: <c>icon</c> (cpu, gpu, ram, disk, network, fps), <c>text</c> (template), <c>fontSize</c>, <c>iconSize</c>, <c>gap</c>.
/// </summary>
public sealed class IconValueWidget : Widget
{
    private readonly ValueTemplate _template;
    private readonly TextBlock _text;

    public IconValueWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        _template = ValueTemplate.Parse(definition.GetString("text"));
        var iconSize = definition.GetDouble("iconSize", 60);

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Path
        {
            Data = Icons.Get(definition.GetString("icon")),
            Stroke = theme.Accent,
            StrokeThickness = definition.GetDouble("iconStroke", 2),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 24,
            Height = 24,
        };
        var iconBox = new Viewbox
        {
            Child = icon,
            Width = iconSize,
            Height = iconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, definition.GetDouble("gap", 50), 0),
        };
        Children.Add(iconBox);

        _text = CreateText(definition.GetDouble("fontSize", 64));
        _text.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_text, 1);
        Children.Add(_text);
    }

    public override void Refresh(MetricStore store, DateTime now) => _text.Text = _template.Render(store);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>An accent-coloured icon followed by a value.</summary>
public sealed class IconValueWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "icon", "Icon and value", "An accent-coloured line icon followed by a live value, e.g. a chip and the CPU temperature.",
        360, 80,
        [
            Setting.Icon("icon", "Icon", "cpu"),
            Setting.Template("text", "Text", "{cpu.temp:nounit} °C"),
            Setting.Number("fontSize", "Font size", 64, 6, 400),
            Setting.Number("iconSize", "Icon size", 60, 8, 400, group: Setting.Appearance),
            Setting.Number("iconStroke", "Icon line width", 2, 0.5, 6, group: Setting.Appearance, help: "On the icon's 24×24 grid."),
            Setting.Number("gap", "Gap after icon", 50, 0, 400, group: Setting.Appearance),
        ],
        (settings, theme) => new IconValueWidget(settings, theme));

    private readonly ValueTemplate _template;
    private readonly TextBlock _text;

    public IconValueWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _template = ValueTemplate.Parse(settings.String("text"));
        var iconSize = settings.Number("iconSize");

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Path
        {
            Data = Icons.Get(settings.String("icon")),
            Stroke = theme.Accent,
            StrokeThickness = settings.Number("iconStroke"),
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
            Margin = new Thickness(0, 0, settings.Number("gap"), 0),
        };
        Children.Add(iconBox);

        _text = CreateText(settings.Number("fontSize"));
        _text.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_text, 1);
        Children.Add(_text);
    }

    public override void Refresh(MetricStore store, DateTime now) => _text.Text = _template.Render(store);
}

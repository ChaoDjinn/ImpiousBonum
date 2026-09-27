using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>A line of text with metric placeholders.</summary>
public sealed class TextWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "text", "Text", "A line of text with live values, e.g. \"{gpu.load}\" or \"{cpu.temp:nounit} °C\".",
        400, 80,
        [
            Setting.Template("text", "Text", "{cpu.load}"),
            Setting.Number("fontSize", "Font size", 48, 6, 400),
            Setting.Choice("align", "Alignment", "left", Setting.HorizontalAlignments),
            Setting.Toggle("uppercase", "Uppercase", false),
            Setting.Color("color", "Colour", "foreground"),
        ],
        (settings, theme) => new TextWidget(settings, theme));

    private readonly ValueTemplate _template;
    private readonly TextBlock _text;
    private readonly bool _uppercase;

    public TextWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _template = ValueTemplate.Parse(settings.String("text"));
        _uppercase = settings.Bool("uppercase");
        _text = CreateText(
            settings.Number("fontSize"),
            theme.Resolve(settings.String("color"), theme.Foreground),
            ParseAlignment(settings.String("align")));
        _text.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_text);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var text = _template.Render(store);
        _text.Text = _uppercase ? text.ToUpper() : text;
    }
}

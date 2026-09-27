using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A line of text with metric placeholders.
/// Settings: <c>text</c> (template), <c>fontSize</c>, <c>align</c> (left/center/right), <c>color</c>, <c>uppercase</c>.
/// </summary>
public sealed class TextWidget : Widget
{
    private readonly ValueTemplate _template;
    private readonly TextBlock _text;
    private readonly bool _uppercase;

    public TextWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        _template = ValueTemplate.Parse(definition.GetString("text"));
        _uppercase = definition.GetBool("uppercase", false);
        _text = CreateText(
            definition.GetDouble("fontSize", 48),
            theme.Resolve(definition.GetString("color"), theme.Foreground),
            ParseAlignment(definition.GetString("align")));
        _text.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_text);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var text = _template.Render(store);
        _text.Text = _uppercase ? text.ToUpper() : text;
    }
}

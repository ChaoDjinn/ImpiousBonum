using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Time with the date underneath, in the user's culture.
/// Settings: <c>timeFormat</c> (default HH:mm), <c>dateFormat</c> (default "dddd, d MMMM yyyy", empty hides it),
/// <c>timeSize</c>, <c>dateSize</c>, <c>uppercaseDate</c>, <c>align</c>.
/// </summary>
public sealed class ClockWidget : Widget
{
    private readonly string _timeFormat;
    private readonly string _dateFormat;
    private readonly bool _uppercaseDate;
    private readonly TextBlock _time;
    private readonly TextBlock _date;

    public ClockWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        _timeFormat = definition.GetString("timeFormat", "HH:mm")!;
        _dateFormat = definition.GetString("dateFormat", "dddd, d MMMM yyyy")!;
        _uppercaseDate = definition.GetBool("uppercaseDate", true);
        var alignment = ParseAlignment(definition.GetString("align", "center"));

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _time = CreateText(definition.GetDouble("timeSize", 150), alignment: alignment);
        // Big light digits carry a lot of internal leading; pull the date up under them.
        _time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _time.LineHeight = definition.GetDouble("timeSize", 150) * 1.05;
        _date = CreateText(definition.GetDouble("dateSize", 30), alignment: alignment);
        stack.Children.Add(_time);
        if (_dateFormat.Length > 0)
            stack.Children.Add(_date);
        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _time.Text = now.ToString(_timeFormat, CultureInfo.CurrentCulture);
        var date = now.ToString(_dateFormat, CultureInfo.CurrentCulture);
        _date.Text = _uppercaseDate ? date.ToUpper(CultureInfo.CurrentCulture) : date;
    }
}

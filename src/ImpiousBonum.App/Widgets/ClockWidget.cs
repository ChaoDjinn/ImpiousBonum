using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>Time with the date underneath, in the user's culture.</summary>
public sealed class ClockWidget : Widget
{
    private const string FormatHelp = ".NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM.";

    public static WidgetDescriptor Descriptor { get; } = new(
        "clock", "Clock", "The time with the date underneath, in your Windows language and region.",
        420, 200,
        [
            Setting.PlainText("timeFormat", "Time format", "HH:mm", help: FormatHelp),
            Setting.PlainText("dateFormat", "Date format", "dddd, d MMMM yyyy", help: FormatHelp + " Empty hides the date."),
            Setting.Number("timeSize", "Time size", 150, 6, 600),
            Setting.Number("dateSize", "Date size", 30, 6, 200),
            Setting.Toggle("uppercaseDate", "Uppercase date", true),
            Setting.Choice("align", "Alignment", "center", Setting.HorizontalAlignments),
        ],
        (settings, theme) => new ClockWidget(settings, theme));

    private readonly string _timeFormat;
    private readonly string _dateFormat;
    private readonly bool _uppercaseDate;
    private readonly TextBlock _time;
    private readonly TextBlock _date;

    public ClockWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _timeFormat = settings.String("timeFormat");
        _dateFormat = settings.String("dateFormat");
        _uppercaseDate = settings.Bool("uppercaseDate");
        var alignment = ParseAlignment(settings.String("align"));
        var timeSize = settings.Number("timeSize");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _time = CreateText(timeSize, alignment: alignment);
        // Big light digits carry a lot of internal leading; pull the date up under them.
        _time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _time.LineHeight = timeSize * 1.05;
        _date = CreateText(settings.Number("dateSize"), alignment: alignment);
        stack.Children.Add(_time);
        if (_dateFormat.Length > 0)
            stack.Children.Add(_date);
        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _time.Text = Format(now, _timeFormat);
        var date = Format(now, _dateFormat);
        _date.Text = _uppercaseDate ? date.ToUpper(CultureInfo.CurrentCulture) : date;
    }

    private static string Format(DateTime now, string format)
    {
        try
        {
            return now.ToString(format, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            // A half-typed format shouldn't take the dashboard down.
            return format;
        }
    }
}

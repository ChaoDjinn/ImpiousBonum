using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>Time with the date underneath, in the user's culture, and optionally the time in other places.</summary>
public sealed class ClockWidget : Widget
{
    private const string FormatHelp = ".NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM.";

    public static WidgetDescriptor Descriptor { get; } = new(
        "clock", "Clock", "The time with the date underneath, in your Windows language and region, and the time in other places if you add them.",
        420, 200,
        [
            Setting.PlainText("timeFormat", "Time format", "HH:mm", help: FormatHelp),
            Setting.PlainText("dateFormat", "Date format", "dddd, d MMMM yyyy", help: FormatHelp + " Empty hides the date."),
            Setting.Number("timeSize", "Time size", 150, 6, 600),
            Setting.Number("dateSize", "Date size", 30, 6, 200),
            Setting.Toggle("uppercaseDate", "Uppercase date", true),
            Setting.Choice("align", "Alignment", "center", Setting.HorizontalAlignments),
            Setting.ShrinkToFit(),
            Setting.PlainText("timeZone", "Time zone", null, help: "Empty is this PC's. " + ZoneHelp),
            Setting.Items("zones", "Other places",
            [
                Setting.PlainText("label", "Label", "New York"),
                Setting.PlainText("zone", "Time zone", "America/New_York", help: ZoneHelp),
            ],
            help: "Smaller clocks under the date, each with its own time zone: a world clock."),
            Setting.PlainText("zoneFormat", "Other places' format", "HH:mm", help: FormatHelp),
            Setting.Number("zoneSize", "Other places' size", 26, 6, 200),
            Setting.Choice("zoneLayout", "Other places' layout", "columns", ["columns", "rows"], group: Setting.Appearance,
                help: "columns puts them side by side, label above time; rows stacks them, label left and time right."),
        ],
        (settings, theme) => new ClockWidget(settings, theme));

    private readonly string _timeFormat;
    private readonly string _dateFormat;
    private readonly bool _uppercaseDate;
    private readonly TextBlock _time;
    private readonly TextBlock _date;
    private readonly TimeZoneInfo? _zone;
    private readonly string _zoneFormat;
    private readonly List<(TimeZoneInfo? Zone, TextBlock Time)> _places = [];

    private const string ZoneHelp =
        "A place's time zone, e.g. Europe/London, America/Los_Angeles, Asia/Tokyo, or a Windows name such as GMT Standard Time.";

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
        stack.Children.Add(Fit(_time, FitDirection.Width));
        if (_dateFormat.Length > 0)
            stack.Children.Add(Fit(_date, FitDirection.Width));
        _zone = FindZone(settings.String("timeZone"));
        _zoneFormat = settings.String("zoneFormat");

        var places = settings.Items("zones");
        if (places.Count > 0)
            stack.Children.Add(BuildPlaces(places, settings.Number("zoneSize"), settings.String("zoneLayout") == "rows", alignment));
        Children.Add(Fit(stack, FitDirection.Height));
    }

    private UIElement BuildPlaces(IReadOnlyList<WidgetSettings> places, double size, bool rows, HorizontalAlignment alignment)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid
        {
            Rows = rows ? places.Count : 1,
            Columns = rows ? 1 : places.Count,
            Margin = new Thickness(0, size * 0.5, 0, 0),
        };
        foreach (var place in places)
        {
            var zoneText = place.String("zone");
            var zone = FindZone(zoneText);
            var label = CreateText(size * 0.8, Theme.Secondary, rows ? HorizontalAlignment.Left : alignment);
            label.Text = place.String("label") is { Length: > 0 } name ? name : zoneText;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var time = CreateText(size, zone is null ? Theme.Critical : null, rows ? HorizontalAlignment.Right : alignment);
            if (rows)
            {
                label.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(LabelAndValue(label, time));
            }
            else
            {
                // A label ends with … in its column; a time, which can't, gets smaller.
                var column = new StackPanel();
                column.Children.Add(label);
                column.Children.Add(Fit(time, FitDirection.Width));
                grid.Children.Add(column);
            }
            _places.Add((zone, time));
        }
        return grid;
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var local = now;
        if (_zone is not null)
            now = InZone(now, _zone);
        foreach (var (zone, time) in _places)
            time.Text = zone is null ? "Unknown zone" : Place(InZone(local, zone), now, _zoneFormat);
        _time.Text = Format(now, _timeFormat);
        var date = Format(now, _dateFormat);
        _date.Text = _uppercaseDate ? date.ToUpper(CultureInfo.CurrentCulture) : date;
    }

    /// <summary>The time at a place, with +1 or −1 when it's already tomorrow or still yesterday there.</summary>
    internal static string Place(DateTime there, DateTime here, string format)
    {
        var days = (there.Date - here.Date).Days;
        var text = Format(there, format);
        return days switch
        {
            > 0 => $"{text} +{days}",
            < 0 => $"{text} \u2212{-days}",
            _ => text,
        };
    }

    internal static DateTime InZone(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(local, DateTimeKind.Local), zone);

    /// <summary>A zone by IANA name (Europe/London) or Windows name (GMT Standard Time); null when there's no such zone.</summary>
    internal static TimeZoneInfo? FindZone(string id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : null;

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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Calendar;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>What's coming up in your calendars, by day: each event's time and title, with what's on now picked out.</summary>
public sealed class AgendaWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "agenda", "Agenda",
        "What's coming up in your calendars (set them in the editor, or tray → Calendar…), by day, with what's on now in the accent colour.",
        420, 320,
        [
            Setting.Number("days", "Days", 3, 1, CalendarProvider.DaysAhead + 1, group: Setting.Content, help: "How many days to show, starting today."),
            Setting.Number("count", "Most events", 8, 1, 50, group: Setting.Content),
            Setting.PlainText("calendars", "Calendars", "all", help: "\"all\", or calendar names separated by commas, e.g. \"Work, Family\"."),
            Setting.Toggle("location", "Show where", true, group: Setting.Content),
            Setting.Toggle("dayHeaders", "Day headings", true, group: Setting.Content, help: "Today, Tomorrow, then the date, above each day's events."),
            Setting.PlainText("emptyText", "When there's nothing", "Nothing on", help: "Empty shows nothing."),
            Setting.PlainText("timeFormat", "Time format", "HH:mm", help: ".NET time format, e.g. HH:mm or h:mm tt."),
            Setting.Number("fontSize", "Font size", 20, 6, 200),
            Setting.Number("headerSize", "Heading size", 15, 6, 200),
            Setting.Number("spacing", "Event spacing", 6, 0, 100, group: Setting.Appearance),
            Setting.Color("nowColor", "What's on now", "accent", help: "The colour of events that are happening now."),
        ],
        (settings, theme) => new AgendaWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly int _days;
    private readonly int _count;
    private readonly HashSet<string>? _only;
    private readonly bool _location;
    private readonly bool _dayHeaders;
    private readonly string _emptyText;
    private readonly string _timeFormat;
    private readonly double _fontSize;
    private readonly double _headerSize;
    private readonly double _spacing;
    private readonly Brush _nowColor;
    private string _shown = "\0";

    public AgendaWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _days = (int)settings.Number("days");
        _count = (int)settings.Number("count");
        var calendars = settings.String("calendars");
        _only = calendars.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) || calendars.Trim().Length == 0
            ? null
            : calendars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _location = settings.Bool("location");
        _dayHeaders = settings.Bool("dayHeaders");
        _emptyText = settings.String("emptyText");
        _timeFormat = settings.String("timeFormat");
        _fontSize = settings.Number("fontSize");
        _headerSize = settings.Number("headerSize");
        _spacing = settings.Number("spacing");
        _nowColor = theme.Resolve(settings.String("nowColor"), theme.Accent);
        Children.Add(_rows);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var events = Pick(store.GetAttachment(CalendarProvider.Events) as IReadOnlyList<CalendarEvent>, now, _days, _count, _only);
        // Rebuild only when what's shown changes: the list, or which events are on now.
        var key = string.Join("\n", events.Select(e => $"{e.Calendar}|{e.Title}|{e.Start:O}|{e.End:O}|{e.Location}|{IsOn(e, now)}")) + now.Date.ToString("O");
        if (key == _shown)
            return;
        _shown = key;
        Build(events, now);
    }

    /// <summary>The events to list: not yet over, starting within <paramref name="days"/> days from today, from the chosen calendars.</summary>
    internal static IReadOnlyList<CalendarEvent> Pick(IReadOnlyList<CalendarEvent>? events, DateTime now, int days, int count, IReadOnlySet<string>? only) =>
        events is null
            ? []
            : events
                .Where(e => e.End > now && e.Start < now.Date.AddDays(days) && (only is null || only.Contains(e.Calendar)))
                .Take(count)
                .ToList();

    private static bool IsOn(CalendarEvent e, DateTime now) => !e.AllDay && e.Start <= now && e.End > now;

    /// <summary>"Today", "Tomorrow", then the date, e.g. "Wednesday 14 October".</summary>
    internal static string DayHeading(DateTime day, DateTime now) => (day.Date - now.Date).Days switch
    {
        <= 0 => "Today",
        1 => "Tomorrow",
        _ => day.ToString("dddd d MMMM", CultureInfo.CurrentCulture),
    };

    /// <summary>All day, the start time, or "Now" with when it ends for something already going.</summary>
    internal static string TimeText(CalendarEvent e, DateTime now, string format)
    {
        if (e.AllDay)
            return "All day";
        if (e.Start <= now && e.End > now)
            return e.Start.Date < now.Date ? $"Until {Format(e.End, format)}" : Format(e.Start, format);
        return Format(e.Start, format);
    }

    private static string Format(DateTime time, string format)
    {
        try
        {
            return time.ToString(format, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return time.ToString("t", CultureInfo.CurrentCulture);
        }
    }

    private void Build(IReadOnlyList<CalendarEvent> events, DateTime now)
    {
        _rows.Children.Clear();
        if (events.Count == 0)
        {
            if (_emptyText.Length > 0)
            {
                var empty = CreateText(_fontSize, Theme.Secondary);
                empty.Text = _emptyText;
                _rows.Children.Add(empty);
            }
            return;
        }

        DateTime? day = null;
        foreach (var e in events)
        {
            // Something that started before today is listed under today.
            var listedOn = e.Start.Date < now.Date ? now.Date : e.Start.Date;
            if (_dayHeaders && listedOn != day)
            {
                var heading = CreateText(_headerSize, Theme.Secondary);
                heading.Text = DayHeading(listedOn, now).ToUpper(CultureInfo.CurrentCulture);
                heading.Margin = new Thickness(0, day is null ? 0 : _spacing * 1.5, 0, _spacing / 2);
                _rows.Children.Add(heading);
            }
            day = listedOn;

            var on = IsOn(e, now);
            var row = new Grid { Margin = new Thickness(0, 0, 0, _spacing) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var time = CreateText(_fontSize * 0.85, on ? _nowColor : Theme.Secondary);
            time.Text = TimeText(e, now, _timeFormat);
            time.MinWidth = _fontSize * 3.4;
            time.Margin = new Thickness(0, _fontSize * 0.1, _fontSize * 0.5, 0);

            var details = new StackPanel();
            var title = CreateText(_fontSize, on ? _nowColor : null);
            title.Text = e.Title;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(title);
            if (_location && e.Location is { Length: > 0 } where)
            {
                var place = CreateText(_fontSize * 0.75, Theme.Secondary);
                place.Text = where.ReplaceLineEndings(", ");
                place.TextTrimming = TextTrimming.CharacterEllipsis;
                details.Children.Add(place);
            }
            Grid.SetColumn(details, 1);
            row.Children.Add(time);
            row.Children.Add(details);
            _rows.Children.Add(row);
        }
    }
}

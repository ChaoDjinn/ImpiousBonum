using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Calendar;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class AgendaTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 9, 45, 0);

    private static readonly CalendarEvent[] Events =
    [
        new("Conference", new DateTime(2026, 10, 10), new DateTime(2026, 10, 13), true, null, "Work"),
        new("Overnight build", new DateTime(2026, 10, 11, 22, 0, 0), new DateTime(2026, 10, 12, 10, 0, 0), false, null, "Work"),
        new("Breakfast", new DateTime(2026, 10, 12, 8, 0, 0), new DateTime(2026, 10, 12, 9, 0, 0), false, null, "Home"),
        new("Stand-up", new DateTime(2026, 10, 12, 9, 30, 0), new DateTime(2026, 10, 12, 10, 0, 0), false, "Room 2", "Work"),
        new("Football", new DateTime(2026, 10, 13, 18, 0, 0), new DateTime(2026, 10, 13, 19, 0, 0), false, "Park\nNorth gate", "Home"),
        new("Dentist", new DateTime(2026, 10, 16, 11, 0, 0), new DateTime(2026, 10, 16, 11, 30, 0), false, null, "Home"),
    ];

    [Fact]
    public void Finished_events_go_and_the_span_and_calendars_are_kept_to()
    {
        Assert.Equal(["Conference", "Overnight build", "Stand-up", "Football"], AgendaWidget.Pick(Events, Now, 3, 10, null).Select(e => e.Title));
        Assert.Equal(["Football", "Dentist"], AgendaWidget.Pick(Events, Now, 7, 10, new HashSet<string>(["home"], StringComparer.OrdinalIgnoreCase)).Select(e => e.Title));
        Assert.Equal(["Conference", "Overnight build"], AgendaWidget.Pick(Events, Now, 7, 2, null).Select(e => e.Title));
        Assert.Empty(AgendaWidget.Pick(null, Now, 7, 10, null));
    }

    [Fact]
    public void Days_and_times_read_naturally()
    {
        Assert.Equal("Today", AgendaWidget.DayHeading(Now.Date, Now));
        Assert.Equal("Tomorrow", AgendaWidget.DayHeading(Now.Date.AddDays(1), Now));
        Assert.Equal(new DateTime(2026, 10, 16).ToString("dddd d MMMM"), AgendaWidget.DayHeading(new DateTime(2026, 10, 16), Now));

        Assert.Equal("All day", AgendaWidget.TimeText(Events[0], Now, "HH:mm"));
        Assert.Equal("Until 10:00", AgendaWidget.TimeText(Events[1], Now, "HH:mm"));
        Assert.Equal("09:30", AgendaWidget.TimeText(Events[3], Now, "HH:mm"));
        Assert.Equal("18:00", AgendaWidget.TimeText(Events[4], Now, "HH:mm"));
    }

    [Fact]
    public void The_widget_lists_events_and_says_when_theres_nothing()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(CalendarProvider.Events, Events);
            foreach (var headers in new[] { true, false })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "agenda", ["dayHeaders"] = headers, ["days"] = 7 }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, Now);
                widget.Refresh(store, Now);
                widget.Refresh(new MetricStore(), Now);
            }
        });
    }

    [Fact]
    public void The_calendar_window_adds_and_checks_links()
    {
        Sta.Run(() =>
        {
            var window = new LinkListWindow([new NamedLink("Work", "https://example.com/work.ics")], "Calendars", "calendar",
                LinkListWindow.CalendarIntro, LinkListWindow.CalendarNote, allowWebcal: true);
            window.LinkBox.Text = "not a link";
            Assert.NotNull(window.Problem());
            window.LinkBox.Text = "https://example.com/work.ics";
            Assert.Contains("already", window.Problem());
            window.LinkBox.Text = "webcal://p01-caldav.icloud.com/published/2/abc";
            Assert.Null(window.Problem());
            window.Add();
            window.NameBox.Text = "Family";
            window.LinkBox.Text = "https://calendar.google.com/calendar/ical/x/private-y/basic.ics";
            window.Add();

            Assert.Equal(
                [new NamedLink("Work", "https://example.com/work.ics"), new NamedLink("p01-caldav.icloud.com", "webcal://p01-caldav.icloud.com/published/2/abc"),
                 new NamedLink("Family", "https://calendar.google.com/calendar/ical/x/private-y/basic.ics")],
                window.Links);
            window.Close();
        });
    }
}

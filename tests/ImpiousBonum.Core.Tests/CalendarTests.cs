using ImpiousBonum.Core.Calendar;

namespace ImpiousBonum.Core.Tests;

public sealed class CalendarTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly DateTime From = new(2026, 10, 11);

    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Concat(events.Select(e => $"BEGIN:VEVENT\r\n{e.Trim()}\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static IReadOnlyList<CalendarEvent> Read(string ics, int days = 7) =>
        ICalendar.Events(ics, "Home", From, From.AddDays(days), London);

    [Fact]
    public void A_single_event_in_utc_comes_out_in_local_time()
    {
        var events = Read(Ics("UID:1\r\nSUMMARY:Dentist\r\nLOCATION:High St\\, No. 4\r\nDTSTART:20261012T090000Z\r\nDTEND:20261012T093000Z"));

        var e = Assert.Single(events);
        Assert.Equal("Dentist", e.Title);
        Assert.Equal("High St, No. 4", e.Location);
        // British Summer Time until the 25th of October.
        Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0), e.Start);
        Assert.Equal(new DateTime(2026, 10, 12, 10, 30, 0), e.End);
        Assert.False(e.AllDay);
        Assert.Equal("Home", e.Calendar);
    }

    [Fact]
    public void Zones_are_read_by_iana_windows_or_outlook_name()
    {
        foreach (var tzid in new[] { "America/New_York", "Eastern Standard Time", "\"America/New_York\"" })
        {
            var e = Assert.Single(Read(Ics($"UID:1\r\nSUMMARY:Call\r\nDTSTART;TZID={tzid}:20261012T090000\r\nDTEND;TZID={tzid}:20261012T100000")));
            Assert.Equal(new DateTime(2026, 10, 12, 14, 0, 0), e.Start);
        }
        Assert.NotNull(ICalendar.FindZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo").DisplayName));
        Assert.Null(ICalendar.FindZone("Customized Time Zone"));
    }

    [Fact]
    public void All_day_events_keep_their_dates()
    {
        var e = Assert.Single(Read(Ics("UID:1\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261013\r\nDTEND;VALUE=DATE:20261015")));
        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 10, 13), e.Start);
        Assert.Equal(new DateTime(2026, 10, 15), e.End);
    }

    [Fact]
    public void Folded_lines_join_and_alarms_dont_count()
    {
        var events = Read(Ics("UID:1\r\nSUMMARY:A very long\r\n  meeting name\r\nDTSTART:20261012T090000Z\r\nBEGIN:VALARM\r\nSUMMARY:Reminder\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nDURATION:PT1H30M"));
        var e = Assert.Single(events);
        Assert.Equal("A very long meeting name", e.Title);
        Assert.Equal(TimeSpan.FromMinutes(90), e.End - e.Start);
    }

    [Fact]
    public void Weekly_repeats_on_the_days_given_and_skips_exdates()
    {
        var events = Read(Ics(
            "UID:gym\r\nSUMMARY:Gym\r\nDTSTART;TZID=Europe/London:20260907T070000\r\nDTEND;TZID=Europe/London:20260907T080000\r\n"
            + "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR\r\nEXDATE;TZID=Europe/London:20261014T070000"), days: 14);

        Assert.Equal(
            [new DateTime(2026, 10, 12, 7, 0, 0), new DateTime(2026, 10, 16, 7, 0, 0), new DateTime(2026, 10, 19, 7, 0, 0),
             new DateTime(2026, 10, 21, 7, 0, 0), new DateTime(2026, 10, 23, 7, 0, 0)],
            events.Select(e => e.Start));
    }

    [Fact]
    public void Repeats_keep_their_wall_clock_time_across_daylight_saving()
    {
        // Set up in summer as 09:00 London time; still 09:00 after the clocks go back on the 25th.
        var events = Read(Ics("UID:1\r\nSUMMARY:Stand-up\r\nDTSTART;TZID=Europe/London:20260701T090000\r\nDTEND;TZID=Europe/London:20260701T091500\r\nRRULE:FREQ=DAILY"), days: 21);
        Assert.All(events, e => Assert.Equal(new TimeSpan(9, 0, 0), e.Start.TimeOfDay));
        Assert.Equal(21, events.Count);
    }

    [Fact]
    public void Count_and_until_end_a_repeat()
    {
        var counted = Read(Ics("UID:1\r\nSUMMARY:Course\r\nDTSTART:20261009T180000Z\r\nRRULE:FREQ=DAILY;COUNT=4"), days: 30);
        // The 9th and 10th are before the span but still count.
        Assert.Equal([new DateTime(2026, 10, 11, 19, 0, 0), new DateTime(2026, 10, 12, 19, 0, 0)], counted.Select(e => e.Start));

        var until = Read(Ics("UID:1\r\nSUMMARY:Trip\r\nDTSTART;VALUE=DATE:20261010\r\nRRULE:FREQ=DAILY;UNTIL=20261013"), days: 30);
        Assert.Equal([new DateTime(2026, 10, 11), new DateTime(2026, 10, 12), new DateTime(2026, 10, 13)], until.Select(e => e.Start));
    }

    [Fact]
    public void Monthly_and_yearly_repeats_follow_their_rules()
    {
        var ics = Ics(
            "UID:a\r\nSUMMARY:Last Friday\r\nDTSTART:20260130T120000Z\r\nRRULE:FREQ=MONTHLY;BYDAY=-1FR",
            "UID:b\r\nSUMMARY:Rent\r\nDTSTART;VALUE=DATE:20260131\r\nRRULE:FREQ=MONTHLY",
            "UID:c\r\nSUMMARY:Second Tuesday\r\nDTSTART:20260113T170000Z\r\nRRULE:FREQ=MONTHLY;INTERVAL=1;BYDAY=2TU",
            "UID:d\r\nSUMMARY:Anniversary\r\nDTSTART;VALUE=DATE:20101103\r\nRRULE:FREQ=YEARLY",
            "UID:e\r\nSUMMARY:Thanksgiving\r\nDTSTART;VALUE=DATE:20201126\r\nRRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=4TH");
        var events = ICalendar.Events(ics, "Home", new DateTime(2026, 10, 1), new DateTime(2026, 12, 1), London);

        Assert.Equal([new DateTime(2026, 10, 30, 12, 0, 0), new DateTime(2026, 11, 27, 12, 0, 0)], events.Where(e => e.Title == "Last Friday").Select(e => e.Start));
        // Months without a 31st are skipped, as the standard says.
        Assert.Equal([new DateTime(2026, 10, 31)], events.Where(e => e.Title == "Rent").Select(e => e.Start));
        Assert.Equal([new DateTime(2026, 10, 13, 18, 0, 0), new DateTime(2026, 11, 10, 17, 0, 0)], events.Where(e => e.Title == "Second Tuesday").Select(e => e.Start));
        Assert.Equal([new DateTime(2026, 11, 3)], events.Where(e => e.Title == "Anniversary").Select(e => e.Start));
        Assert.Equal([new DateTime(2026, 11, 26)], events.Where(e => e.Title == "Thanksgiving").Select(e => e.Start));
    }

    [Fact]
    public void A_moved_occurrence_replaces_the_one_it_names_and_cancelled_events_go()
    {
        var events = Read(Ics(
            "UID:x\r\nSUMMARY:1:1\r\nDTSTART:20261005T100000Z\r\nDTEND:20261005T103000Z\r\nRRULE:FREQ=WEEKLY",
            "UID:x\r\nRECURRENCE-ID:20261012T100000Z\r\nSUMMARY:1:1 (moved)\r\nDTSTART:20261013T140000Z\r\nDTEND:20261013T143000Z",
            "UID:y\r\nSUMMARY:Off\r\nSTATUS:CANCELLED\r\nDTSTART:20261012T100000Z"));

        Assert.Equal(["1:1 (moved)"], events.Select(e => e.Title));
    }

    [Fact]
    public void Something_already_going_shows_and_finished_things_dont()
    {
        var ics = Ics(
            "UID:1\r\nSUMMARY:Conference\r\nDTSTART;VALUE=DATE:20261009\r\nDTEND;VALUE=DATE:20261013",
            "UID:2\r\nSUMMARY:Yesterday\r\nDTSTART:20261010T090000Z\r\nDTEND:20261010T100000Z");
        Assert.Equal(["Conference"], Read(ics).Select(e => e.Title));
    }

    [Fact]
    public void A_rule_that_never_matches_ends()
    {
        var events = Read(Ics("UID:1\r\nSUMMARY:Never\r\nDTSTART;VALUE=DATE:20240201\r\nRRULE:FREQ=MONTHLY;INTERVAL=12;BYMONTHDAY=30"), days: 30);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("PT1H30M", 90)]
    [InlineData("P1D", 1440)]
    [InlineData("P1W", 10080)]
    [InlineData("-PT15M", -15)]
    [InlineData("P1DT2H", 1560)]
    public void Durations_are_read(string text, double minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), ICalendar.ParseDuration(text));

    [Fact]
    public void Text_unescapes_and_quoted_parameters_keep_their_colons()
    {
        Assert.Equal("a, b; c\\d\ne", ICalendar.Unescape("a\\, b\\; c\\\\d\\ne"));
        var (name, parameters, value) = ICalendar.Split("DTSTART;TZID=\"GMT+01:00\";VALUE=DATE-TIME:20261012T090000");
        Assert.Equal("DTSTART", name);
        Assert.Equal("GMT+01:00", parameters["TZID"]);
        Assert.Equal("20261012T090000", value);
    }
}

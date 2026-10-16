using System.Globalization;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

public sealed class HolidaysTests
{
    private static DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private static string? ThemeOn(string date) => Holidays.On(Day(date))?.Theme;

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    [InlineData(2035, "2035-03-25")]
    // The latest Easter can be, and the earliest.
    [InlineData(2038, "2038-04-25")]
    [InlineData(2285, "2285-03-22")]
    public void Easter_is_worked_out(int year, string sunday) =>
        Assert.Equal(Day(sunday), Holidays.Easter(year));

    [Theory]
    // Fixed dates, with a day or a week before.
    [InlineData("2026-02-12", null)]
    [InlineData("2026-02-13", "Valentine")]
    [InlineData("2026-02-14", "Valentine")]
    [InlineData("2026-03-17", "Shamrock")]
    [InlineData("2026-10-23", null)]
    [InlineData("2026-10-24", "Halloween")]
    [InlineData("2026-10-31", "Halloween")]
    [InlineData("2026-11-01", "Day of the Dead")]
    [InlineData("2026-11-02", "Day of the Dead")]
    [InlineData("2026-11-03", null)]
    [InlineData("2026-11-05", "Fireworks")]
    [InlineData("2026-12-31", "Fireworks")]
    [InlineData("2027-01-01", "Fireworks")]
    [InlineData("2027-01-02", null)]
    // Easter from Good Friday to Easter Monday.
    [InlineData("2026-04-02", null)]
    [InlineData("2026-04-03", "Easter")]
    [InlineData("2026-04-06", "Easter")]
    [InlineData("2026-04-07", null)]
    // Lunar New Year on 17 February 2026: from the eve, for a week.
    [InlineData("2026-02-15", null)]
    [InlineData("2026-02-16", "Lunar New Year")]
    [InlineData("2026-02-23", "Lunar New Year")]
    [InlineData("2026-02-24", null)]
    [InlineData("2027-02-06", "Lunar New Year")]
    // Eid al-Fitr on 20 March 2026 and Eid al-Adha on 27 May.
    [InlineData("2026-03-18", null)]
    [InlineData("2026-03-19", "Eid")]
    [InlineData("2026-03-22", "Eid")]
    [InlineData("2026-03-23", null)]
    [InlineData("2026-05-27", "Eid")]
    // Eid al-Fitr comes twice in 2033.
    [InlineData("2033-01-02", "Eid")]
    [InlineData("2033-12-23", "Eid")]
    // Diwali on 8 November 2026, for its five days.
    [InlineData("2026-11-06", "Diwali")]
    [InlineData("2026-11-10", "Diwali")]
    [InlineData("2026-11-11", null)]
    // December 2026: the run-up, then Hanukkah from the eve of 5 December for its eight days, then Christmas.
    [InlineData("2026-12-01", "Gingerbread")]
    [InlineData("2026-12-03", "Gingerbread")]
    [InlineData("2026-12-04", "Hanukkah")]
    [InlineData("2026-12-12", "Hanukkah")]
    [InlineData("2026-12-13", "Gingerbread")]
    [InlineData("2026-12-18", "Christmas")]
    [InlineData("2026-12-30", "Christmas")]
    public void Each_holiday_shows_its_theme_on_and_around_its_day(string date, string? theme) =>
        Assert.Equal(theme, ThemeOn(date));

    [Theory]
    // Diwali on 29 October 2027 shows over Halloween.
    [InlineData("2027-10-26", "Halloween")]
    [InlineData("2027-10-27", "Diwali")]
    [InlineData("2027-10-31", "Diwali")]
    // Hanukkah starts on Christmas Day 2027: Christmas wins its three days, Hanukkah the rest of the week, then New Year's Eve.
    [InlineData("2027-12-23", "Christmas")]
    [InlineData("2027-12-25", "Christmas")]
    [InlineData("2027-12-27", "Hanukkah")]
    [InlineData("2027-12-30", "Hanukkah")]
    [InlineData("2027-12-31", "Fireworks")]
    // February 2029 has Lunar New Year on the 13th, and Valentine's Day and Eid al-Fitr on the 14th: each gets some.
    [InlineData("2029-02-12", "Lunar New Year")]
    [InlineData("2029-02-14", "Valentine")]
    [InlineData("2029-02-15", "Eid")]
    [InlineData("2029-02-17", "Lunar New Year")]
    // Hanukkah from the eve of 17 December 2033, Eid al-Fitr on the 23rd, then Christmas.
    [InlineData("2033-12-21", "Hanukkah")]
    [InlineData("2033-12-22", "Eid")]
    [InlineData("2033-12-24", "Christmas")]
    public void When_holidays_overlap_the_shorter_wins(string date, string theme) =>
        Assert.Equal(theme, ThemeOn(date));

    [Fact]
    public void Diwali_has_a_table_to_2035()
    {
        Assert.Equal("Diwali", ThemeOn("2035-10-30"));
        // After the table, the rest of the year's holidays still come round.
        Assert.Null(Holidays.On(Day("2036-10-20")));
        Assert.Equal("Halloween", ThemeOn("2036-10-31"));
    }

    [Fact]
    public void Every_holiday_theme_is_built_in() =>
        Assert.All(Holidays.Themes, theme => Assert.True(ThemeLibrary.IsBuiltIn(theme), theme));

    [Fact]
    public void Holidays_say_what_they_are_and_what_comes_next()
    {
        Assert.Equal("Fireworks for Bonfire Night", Holidays.On(Day("2026-11-05"))!.Describe());
        Assert.Equal("Halloween", Holidays.On(Day("2026-10-31"))!.Describe());
        Assert.Equal(new Holiday("Halloween", "Halloween", Day("2026-10-24"), Day("2026-10-31")), Holidays.Next(Day("2026-10-16")));
        Assert.Equal("Lunar New Year", Holidays.Next(Day("2027-01-02"))!.Name);
    }

    [Fact]
    public void A_holiday_theme_comes_after_every_rule()
    {
        var halloween = new ScheduleNow(new DateTime(2026, 10, 31, 22, 0, 0), WindowsDark: false);
        ScheduleRule night = new("21:00", "07:00", null, null, "Night Watch");
        ScheduleRule bedside = new("21:00", "07:00", null, "Bedside", null, 30);

        Assert.Equal(new ScheduledLook(null, "Halloween", null), ScheduledLook.From([], halloween, "Halloween"));
        Assert.Equal(new ScheduledLook("Bedside", "Halloween", 30), ScheduledLook.From([bedside], halloween, "Halloween"));
        Assert.Equal(new ScheduledLook(null, "Night Watch", null), ScheduledLook.From([night], halloween, "Halloween"));
        Assert.Null(ScheduledLook.From([], halloween, null));
    }
}

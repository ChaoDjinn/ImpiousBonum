using System.Globalization;

namespace ImpiousBonum.App.Shell;

/// <summary>A holiday's few days, and the built-in theme that suits them.</summary>
/// <param name="Name">"Bonfire Night".</param>
/// <param name="Theme">"Fireworks": one of the built-in themes.</param>
/// <param name="Start">The first day the theme shows.</param>
/// <param name="End">The last day it shows, included.</param>
public sealed record Holiday(string Name, string Theme, DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly day) => day >= Start && day <= End;

    /// <summary>"Halloween", or "Fireworks for Bonfire Night" when the theme has another name.</summary>
    public string Describe() => Name == Theme ? Name : $"{Theme} for {Name}";
}

/// <summary>
/// The holidays that Pages and schedule → Holiday themes shows a built-in theme on and around. Fixed dates are fixed and
/// Easter is worked out. Hanukkah, Eid and Lunar New Year come from .NET's Hebrew, Um al-Qura and Chinese calendars.
/// Diwali follows the Hindu calendar, which .NET doesn't have, so it comes from a table that runs to 2035.
/// </summary>
public static class Holidays
{
    /// <summary>Diwali's main day, Lakshmi Puja. Some places keep it a day either side; the theme shows for all five days.</summary>
    private static readonly DateOnly[] DiwaliDays =
    [
        new(2026, 11, 8), new(2027, 10, 29), new(2028, 10, 17), new(2029, 11, 5), new(2030, 10, 26),
        new(2031, 11, 14), new(2032, 11, 2), new(2033, 10, 22), new(2034, 11, 10), new(2035, 10, 30),
    ];

    /// <summary>A holiday's main days in a year, and how many days before and after them the theme shows.</summary>
    private sealed record Kind(string Name, string Theme, Func<int, IEnumerable<DateOnly>> Days, int Before, int After);

    /// <summary>Through the year. Hanukkah, Eid and Diwali move around it; Lunar New Year and Easter move a little.</summary>
    private static readonly Kind[] Kinds =
    [
        new("Lunar New Year", "Lunar New Year", LunarNewYear, 1, 6),
        new("Valentine's Day", "Valentine", year => [new(year, 2, 14)], 1, 0),
        new("St Patrick's Day", "Shamrock", year => [new(year, 3, 17)], 1, 0),
        new("Easter", "Easter", year => [Easter(year)], 2, 1),
        new("Eid al-Fitr", "Eid", year => Islamic(year, 10, 1), 1, 2),
        new("Eid al-Adha", "Eid", year => Islamic(year, 12, 10), 1, 3),
        new("Diwali", "Diwali", year => DiwaliDays.Where(day => day.Year == year), 2, 2),
        new("Halloween", "Halloween", year => [new(year, 10, 31)], 7, 0),
        new("Day of the Dead", "Day of the Dead", year => [new(year, 11, 2)], 1, 0),
        new("Bonfire Night", "Fireworks", year => [new(year, 11, 5)], 0, 0),
        new("Hanukkah", "Hanukkah", Hanukkah, 1, 7),
        new("Advent", "Gingerbread", year => [new(year, 12, 1)], 0, 16),
        // Christmas week and the week before, and Christmas itself on its own: it's shorter, so it shows over Hanukkah.
        new("Christmas", "Christmas", year => [new(year, 12, 25)], 7, 5),
        new("Christmas", "Christmas", year => [new(year, 12, 25)], 1, 1),
        new("New Year's Eve", "Fireworks", year => [new(year, 12, 31)], 0, 1),
    ];

    /// <summary>Every theme a holiday shows.</summary>
    public static IEnumerable<string> Themes => Kinds.Select(kind => kind.Theme).Distinct();

    /// <summary>
    /// The holiday whose theme shows on <paramref name="day"/>, or null. When two overlap, the shorter shows: Diwali
    /// over Halloween, Christmas Day over Hanukkah, and Hanukkah over the run-up to Christmas.
    /// </summary>
    public static Holiday? On(DateOnly day) =>
        Kinds.SelectMany(kind => Around(kind, day.Year)).Where(holiday => holiday.Contains(day))
            .MinBy(holiday => holiday.End.DayNumber - holiday.Start.DayNumber);

    /// <summary>The next holiday to start after <paramref name="day"/>.</summary>
    public static Holiday? Next(DateOnly day) =>
        Kinds.SelectMany(kind => Around(kind, day.Year)).Where(holiday => holiday.Start > day).MinBy(holiday => holiday.Start);

    /// <summary>Easter Sunday in the Gregorian calendar, by the anonymous algorithm (Meeus, Jones and Butcher).</summary>
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var (b, c) = (year / 100, year % 100);
        var (d, e) = (b / 4, b % 4);
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var (i, k) = (c / 4, c % 4);
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var dayOfMonth = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, dayOfMonth);
    }

    // A holiday in the years either side too, since one can run over New Year (New Year's Eve, a late Hanukkah).
    private static IEnumerable<Holiday> Around(Kind kind, int year) =>
        Enumerable.Range(year - 1, 3).SelectMany(kind.Days)
            .Select(day => new Holiday(kind.Name, kind.Theme, day.AddDays(-kind.Before), day.AddDays(kind.After)));

    // The first day of Hanukkah, 25 Kislev, the third month of the Hebrew year that starts the autumn before.
    private static IEnumerable<DateOnly> Hanukkah(int year) =>
        Day(() => new HebrewCalendar().ToDateTime(year + 3761, 3, 25, 0, 0, 0, 0));

    // Chinese New Year's Day, the first day of the first month.
    private static IEnumerable<DateOnly> LunarNewYear(int year) =>
        Day(() => new ChineseLunisolarCalendar().ToDateTime(year, 1, 1, 0, 0, 0, 0));

    // A day of the Islamic year as Saudi Arabia's Um al-Qura calendar has it, which most printed calendars follow (the
    // day is announced when the new moon is seen, so it may come a day later where you are). The Islamic year is eleven
    // days shorter, so a day can come twice in one of ours, as Eid al-Fitr does in 2033.
    private static IEnumerable<DateOnly> Islamic(int year, int month, int day)
    {
        var calendar = new UmAlQuraCalendar();
        var first = new DateTime(year, 1, 1);
        if (first < calendar.MinSupportedDateTime || first > calendar.MaxSupportedDateTime)
            return [];
        // Our year can touch three of theirs: the end of one, a whole one and the start of the next.
        return Enumerable.Range(calendar.GetYear(first), 3)
            .SelectMany(hijri => Day(() => calendar.ToDateTime(hijri, month, day, 0, 0, 0, 0)))
            .Where(date => date.Year == year)
            .ToList();
    }

    // Nothing for a year outside what a calendar covers (centuries away), rather than an exception every second.
    private static IEnumerable<DateOnly> Day(Func<DateTime> day)
    {
        try
        {
            return [DateOnly.FromDateTime(day())];
        }
        catch (ArgumentOutOfRangeException)
        {
            return [];
        }
    }
}

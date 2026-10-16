using System.Globalization;
using System.Text.RegularExpressions;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The moments a timer's Until and the countdowns count to, as typed: a time of day, a month and day that comes round
/// every year, or a date, with or without a time.
/// </summary>
internal static partial class TargetDate
{
    /// <summary>
    /// The moment <paramref name="text"/> names, as seen from <paramref name="from"/>: a time of day is its next
    /// occurrence (today if it's still to come); a month and day is this year's until that day is over, then next
    /// year's; a date with no time is its midnight. Null when it can't be read.
    /// </summary>
    internal static DateTime? Parse(string text, DateTime from)
    {
        text = text.Trim();
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var time) || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out time))
        {
            var today = from.Date + time.ToTimeSpan();
            return today > from ? today : today.AddDays(1);
        }
        if (ParseYearly(text) is { } yearly)
        {
            // 29 February only comes round in a leap year.
            for (var year = from.Year; year <= from.Year + 8; year++)
            {
                if (yearly.Day > DateTime.DaysInMonth(year, yearly.Month))
                    continue;
                var day = new DateTime(year, yearly.Month, yearly.Day);
                if (from < day.AddDays(1))
                    return day + yearly.Time;
            }
            return null;
        }
        string[] formats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>Whether <paramref name="text"/> is a time of day, which comes round every day: 18:00.</summary>
    internal static bool IsTimeOfDay(string text) =>
        TimeOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, out _) || TimeOnly.TryParse(text.Trim(), CultureInfo.CurrentCulture, out _);

    /// <summary>A month and day with no year, which comes round every year: 12-25, or 12-25 09:00. Null for anything else.</summary>
    internal static (int Month, int Day, TimeSpan Time)? ParseYearly(string text)
    {
        var match = YearlyPattern().Match(text.Trim());
        if (!match.Success)
            return null;
        int Number(int group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        var (month, day, hours, minutes) = (Number(1), Number(2), Number(3), Number(4));
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2028, month) || hours > 23 || minutes > 59)
            return null;
        return (month, day, new TimeSpan(hours, minutes, 0));
    }

    [GeneratedRegex(@"^(\d{1,2})-(\d{1,2})(?:[ T](\d{1,2}):(\d{2}))?$")]
    private static partial Regex YearlyPattern();
}

using System.Globalization;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Weather;
using Microsoft.Win32;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// When to show a layout or theme on a schedule: between two times of day (<see cref="From"/>–<see cref="To"/>, which may
/// cross midnight), while Windows is in dark or light mode, or from sunset to sunrise or sunrise to sunset where the
/// weather is (<see cref="When"/>); on every day, or only between two dates of the year (<see cref="FromDate"/>–<see
/// cref="ToDate"/>, which may cross New Year). Dates alone mean the whole of each of those days. Like a game link, it
/// names a layout, a theme, or both. A game in front wins; the schedule fills in whatever the game's link doesn't set.
/// </summary>
/// <param name="From">"22:00", in local time. Used with <paramref name="To"/>.</param>
/// <param name="When">
/// <c>dark</c> or <c>light</c>: Windows' app colour mode. <c>night</c> (sunset to sunrise) or <c>day</c> (sunrise to
/// sunset): the sun at the weather's location, worked out on the PC; without a location these never apply.
/// </param>
/// <param name="Brightness">0–99 dims the screen to that percentage while the rule applies (0 is black); null leaves it.</param>
/// <param name="FromDate">"10-24": a month and day, every year. Used with <paramref name="ToDate"/>; both days are included.</param>
public sealed record ScheduleRule(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? When,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Theme,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Brightness = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToDate = null)
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Night = "night";
    public const string Day = "day";

    /// <summary>Dates are read in a leap year, so 02-29 is a date (one that only comes round in leap years).</summary>
    private const int LeapYear = 2000;

    /// <summary>
    /// Has a time span, a mode or the sun, or dates alone; dates that make sense if it has any; and something to show
    /// or a brightness to dim to.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable =>
        (IsMode || IsTimeSpan || IsAllDay) && (!HasDates || Dates is not null)
        && (!string.IsNullOrWhiteSpace(Layout) || !string.IsNullOrWhiteSpace(Theme) || Dims);

    /// <summary>Sets a brightness below full.</summary>
    [JsonIgnore]
    public bool Dims => Brightness is >= 0 and < 100;

    /// <summary>Applies from sunset to sunrise or the other way round, so it needs the weather's location to apply at all.</summary>
    [JsonIgnore]
    public bool FollowsSun => Is(When, Night) || Is(When, Day);

    private bool IsMode => Is(When, Dark) || Is(When, Light) || FollowsSun;

    private bool IsTimeSpan => ParseTime(From) is { } from && ParseTime(To) is { } to && from != to;

    private bool IsAllDay => When is null && From is null && To is null && HasDates;

    private bool HasDates => FromDate is not null || ToDate is not null;

    private (DateOnly From, DateOnly To)? Dates => ParseDate(FromDate) is { } from && ParseDate(ToDate) is { } to ? (from, to) : null;

    /// <summary>True when the rule applies at <paramref name="now"/>.</summary>
    public bool Matches(ScheduleNow now)
    {
        if (!IsUsable)
            return false;
        var time = TimeOnly.FromDateTime(now.Time);
        // The day the dates are checked against. A night, or a time span that crosses midnight, belongs to the evening
        // it starts: 01:00 on 1 November is still the night of 31 October.
        var day = DateOnly.FromDateTime(now.Time);
        if (FollowsSun)
        {
            if (now.SunDown is not { } down || down != Is(When, Night))
                return false;
            if (down && time.Hour < 12)
                day = day.AddDays(-1);
        }
        else if (IsMode)
        {
            if (Is(When, Dark) != now.WindowsDark)
                return false;
        }
        else if (IsTimeSpan)
        {
            var from = ParseTime(From)!.Value;
            var to = ParseTime(To)!.Value;
            // 22:00–07:00 crosses midnight: it's on from 22:00, and still on until 07:00.
            if (from < to ? time < from || time >= to : time < from && time >= to)
                return false;
            if (from > to && time < to)
                day = day.AddDays(-1);
        }
        return Dates is not { } dates || Within(new DateOnly(LeapYear, day.Month, day.Day), dates.From, dates.To);
    }

    // 12-20 to 01-06 crosses New Year: it's on from 20 December, and still on until 6 January.
    private static bool Within(DateOnly day, DateOnly from, DateOnly to) =>
        from <= to ? day >= from && day <= to : day >= from || day <= to;

    /// <summary>
    /// "22:00–07:00: theme Night Watch", "Windows dark mode: Gaming, theme Nord" or "24 Oct–1 Nov, sunset to sunrise:
    /// theme Grimoire", for menus and the log.
    /// </summary>
    public string Describe()
    {
        var when = new List<string>();
        if (Dates is { } dates)
            when.Add(dates.From == dates.To ? DescribeDate(dates.From) : $"{DescribeDate(dates.From)}–{DescribeDate(dates.To)}");
        if (IsMode)
        {
            when.Add(Is(When, Dark) ? "Windows dark mode" : Is(When, Light) ? "Windows light mode"
                : Is(When, Night) ? "sunset to sunrise" : "sunrise to sunset");
        }
        else if (!IsAllDay)
        {
            when.Add($"{From}–{To}");
        }
        var text = string.Join(", ", when);
        return $"{char.ToUpperInvariant(text[0])}{text[1..]}: {DescribeLook(Layout, Theme, Dims ? Brightness : null)}";
    }

    /// <summary>"24 Oct": a date with no year, the same in every language so the month can't be taken for the day.</summary>
    internal static string DescribeDate(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>"Gaming, theme Nord, 30% brightness": what a rule or the schedule as a whole shows.</summary>
    internal static string DescribeLook(string? layout, string? theme, int? brightness)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(layout))
            parts.Add(layout);
        if (!string.IsNullOrWhiteSpace(theme))
            parts.Add($"theme {theme}");
        if (brightness is { } percent)
            parts.Add(percent == 0 ? "screen off" : $"{percent}% brightness");
        return string.Join(", ", parts);
    }

    /// <summary>"7:00" or "22:30" (24-hour, as written in settings.json), or null.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    /// <summary>"12-25" or "1-6", a month and day (as written in settings.json), in a leap year; or null.</summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact($"{LeapYear}-{text?.Trim()}", "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static bool Is(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the schedule's rules are checked against: the local date and time, whether Windows is in dark mode, and whether
/// the sun is down at the weather's location (null without one, when rules that follow the sun never apply).
/// </summary>
public sealed record ScheduleNow(DateTime Time, bool WindowsDark, bool? SunDown = null)
{
    /// <summary>Now, with the sun worked out on the PC for <paramref name="place"/>: nothing is fetched.</summary>
    public static ScheduleNow Current(WeatherLocation? place)
    {
        var now = DateTimeOffset.Now;
        return new ScheduleNow(now.LocalDateTime, WindowsColourMode.IsDark(),
            place is null ? null : SunPosition.IsDown(place.Latitude, place.Longitude, now));
    }
}

/// <summary>
/// What the schedule wants right now. Rules combine: the layout comes from the first applying rule that names one, the
/// theme likewise, and the brightness too, so "Night Watch at night" and "30% from 23:00" both take effect.
/// </summary>
public sealed record ScheduledLook(string? Layout, string? Theme, int? Brightness)
{
    /// <summary>The look from every rule that applies now, or null when none does.</summary>
    public static ScheduledLook? From(IEnumerable<ScheduleRule?> rules, ScheduleNow now)
    {
        var active = Active(rules, now);
        if (active.Count == 0)
            return null;
        return new ScheduledLook(
            active.Select(r => r.Layout).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)),
            active.Select(r => r.Theme).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
            active.Where(r => r.Dims).Select(r => r.Brightness).FirstOrDefault());
    }

    /// <summary>The rules that apply now, in order.</summary>
    public static IReadOnlyList<ScheduleRule> Active(IEnumerable<ScheduleRule?> rules, ScheduleNow now) =>
        rules.OfType<ScheduleRule>().Where(rule => rule.Matches(now)).ToList();

    /// <summary>"Gaming, theme Night Watch, 30% brightness", for the log.</summary>
    public string Describe() => ScheduleRule.DescribeLook(Layout, Theme, Brightness);
}

/// <summary>Whether Windows is in dark mode for apps (Settings → Personalisation → Colours).</summary>
public static class WindowsColourMode
{
    /// <summary>Apps are dark (Settings → Personalisation → Colours → app mode).</summary>
    public static bool IsDark() => IsDark("AppsUseLightTheme");

    /// <summary>Windows itself is dark: the taskbar, Start and the notification area, which the tray menu matches.</summary>
    public static bool IsSystemDark() => IsDark("SystemUsesLightTheme");

    private static bool IsDark(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(value) is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}

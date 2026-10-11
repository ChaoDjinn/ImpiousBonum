using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// When to show a layout or theme on a schedule: between two times of day (<see cref="From"/>–<see cref="To"/>, which may
/// cross midnight), or while Windows is in dark or light mode (<see cref="When"/>). Like a game link, it names a layout,
/// a theme, or both. A game in front wins; the schedule fills in whatever the game's link doesn't set.
/// </summary>
/// <param name="From">"22:00", in local time. Used with <paramref name="To"/>.</param>
/// <param name="When"><c>dark</c> or <c>light</c>: Windows' app colour mode.</param>
/// <param name="Brightness">0–99 dims the screen to that percentage while the rule applies (0 is black); null leaves it.</param>
public sealed record ScheduleRule(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? When,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Theme,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Brightness = null)
{
    public const string Dark = "dark";
    public const string Light = "light";

    /// <summary>Has a time span or a mode, and something to show or a brightness to dim to.</summary>
    [JsonIgnore]
    public bool IsUsable =>
        (IsTimeSpan || When is not null && (Is(When, Dark) || Is(When, Light)))
        && (!string.IsNullOrWhiteSpace(Layout) || !string.IsNullOrWhiteSpace(Theme) || Dims);

    /// <summary>Sets a brightness below full.</summary>
    [JsonIgnore]
    public bool Dims => Brightness is >= 0 and < 100;

    private bool IsTimeSpan => ParseTime(From) is { } from && ParseTime(To) is { } to && from != to;

    /// <summary>True when the rule applies at <paramref name="now"/> (local time) with Windows in dark mode or not.</summary>
    public bool Matches(TimeOnly now, bool windowsDark)
    {
        if (!IsUsable)
            return false;
        if (When is not null)
            return Is(When, Dark) == windowsDark;
        var from = ParseTime(From)!.Value;
        var to = ParseTime(To)!.Value;
        // 22:00–07:00 crosses midnight: it's on from 22:00, and still on until 07:00.
        return from < to ? now >= from && now < to : now >= from || now < to;
    }

    /// <summary>"22:00–07:00: theme Night Watch" or "Windows dark mode: Gaming, theme Nord", for menus and the log.</summary>
    public string Describe()
    {
        var when = When is null ? $"{From}–{To}" : Is(When, Dark) ? "Windows dark mode" : "Windows light mode";
        return $"{when}: {DescribeLook(Layout, Theme, Dims ? Brightness : null)}";
    }

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

    private static bool Is(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the schedule wants right now. Rules combine: the layout comes from the first applying rule that names one, the
/// theme likewise, and the brightness too, so "Night Watch at night" and "30% from 23:00" both take effect.
/// </summary>
public sealed record ScheduledLook(string? Layout, string? Theme, int? Brightness)
{
    /// <summary>The look from every rule that applies now, or null when none does.</summary>
    public static ScheduledLook? From(IEnumerable<ScheduleRule?> rules, TimeOnly now, bool windowsDark)
    {
        var active = Active(rules, now, windowsDark);
        if (active.Count == 0)
            return null;
        return new ScheduledLook(
            active.Select(r => r.Layout).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)),
            active.Select(r => r.Theme).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
            active.Where(r => r.Dims).Select(r => r.Brightness).FirstOrDefault());
    }

    /// <summary>The rules that apply now, in order.</summary>
    public static IReadOnlyList<ScheduleRule> Active(IEnumerable<ScheduleRule?> rules, TimeOnly now, bool windowsDark) =>
        rules.OfType<ScheduleRule>().Where(rule => rule.Matches(now, windowsDark)).ToList();

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

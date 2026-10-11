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
public sealed record ScheduleRule(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? When,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Theme)
{
    public const string Dark = "dark";
    public const string Light = "light";

    /// <summary>Has a time span or a mode, and something to show.</summary>
    [JsonIgnore]
    public bool IsUsable =>
        (IsTimeSpan || When is not null && (Is(When, Dark) || Is(When, Light)))
        && (!string.IsNullOrWhiteSpace(Layout) || !string.IsNullOrWhiteSpace(Theme));

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
        var what = (Layout, Theme) switch
        {
            ({ Length: > 0 } layout, { Length: > 0 } theme) => $"{layout}, theme {theme}",
            (_, { Length: > 0 } theme) => $"theme {theme}",
            _ => Layout ?? string.Empty,
        };
        return $"{when}: {what}";
    }

    /// <summary>The first usable rule that applies now, or null.</summary>
    public static ScheduleRule? Match(IEnumerable<ScheduleRule?> rules, TimeOnly now, bool windowsDark) =>
        rules.FirstOrDefault(rule => rule is not null && rule.Matches(now, windowsDark));

    /// <summary>"7:00" or "22:30" (24-hour, as written in settings.json), or null.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static bool Is(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Whether Windows is in dark mode for apps (Settings → Personalisation → Colours).</summary>
public static class WindowsColourMode
{
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}

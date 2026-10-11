using System.Globalization;
using System.Text;

namespace ImpiousBonum.Core.Calendar;

/// <summary>One event, or one occurrence of a repeating one, in this PC's local time.</summary>
/// <param name="End">When it ends; for an all-day event, midnight after its last day.</param>
/// <param name="Calendar">The name of the calendar it came from.</param>
public sealed record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Location, string Calendar);

/// <summary>
/// Reads iCalendar (.ics) files, as Google, Outlook and Apple calendars publish them, into the events in a span of days.
/// Covers what those produce: time zones (by IANA, Windows or Outlook's display name), all-day events, and repeats
/// (daily, weekly, monthly and yearly, with INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY and BYMONTH) with skipped
/// (EXDATE) and moved (RECURRENCE-ID) occurrences. Rarer repeat rules (BYSETPOS, BYWEEKNO, …) are read as if absent.
/// </summary>
public static class ICalendar
{
    /// <summary>Every event in <paramref name="text"/> that overlaps <paramref name="from"/> to <paramref name="to"/> (local times), soonest first.</summary>
    /// <param name="local">This PC's time zone; tests pass their own.</param>
    public static IReadOnlyList<CalendarEvent> Events(string text, string calendar, DateTime from, DateTime to, TimeZoneInfo? local = null)
    {
        local ??= TimeZoneInfo.Local;
        var raw = Parse(text);
        // Moved or changed occurrences of a repeating event replace the one they name.
        var moved = raw.Where(e => e.RecurrenceId is not null).Select(e => (e.Uid, e.RecurrenceId!.Value.Wall)).ToHashSet();

        var events = new List<CalendarEvent>();
        foreach (var e in raw)
        {
            if (e.Cancelled)
                continue;
            foreach (var start in Occurrences(e, ToZone(from, local, e.Start.Zone) - e.Duration, ToZone(to, local, e.Start.Zone)))
            {
                if (e.RecurrenceId is null && e.Rule is not null && moved.Contains((e.Uid, start)))
                    continue;
                var localStart = e.Start.AllDay ? start : ToLocal(start, e.Start.Zone, local);
                var localEnd = e.Start.AllDay ? start + e.Duration : ToLocal(start + e.Duration, e.Start.Zone, local);
                if (localEnd > from && localStart < to || localStart >= from && localStart < to)
                    events.Add(new CalendarEvent(e.Title, localStart, localEnd, e.Start.AllDay, e.Location, calendar));
            }
        }
        return events.OrderBy(e => e.Start).ThenBy(e => !e.AllDay).ThenBy(e => e.Title, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>A date or time as written: its wall-clock time, and the zone that's in (null for "floating" local time or all-day dates).</summary>
    internal readonly record struct When(DateTime Wall, TimeZoneInfo? Zone, bool AllDay);

    internal sealed record Raw(
        string Uid, string Title, string? Location, When Start, TimeSpan Duration, Rule? Rule, HashSet<DateTime> Skipped, When? RecurrenceId, bool Cancelled);

    internal sealed record Rule(string Frequency, int Interval, int? Count, DateTime? Until, IReadOnlyList<(int Ordinal, DayOfWeek Day)> ByDay, IReadOnlyList<int> ByMonthDay, IReadOnlyList<int> ByMonth);

    internal static List<Raw> Parse(string text)
    {
        var events = new List<Raw>();
        Dictionary<string, List<(Dictionary<string, string> Parameters, string Value)>>? current = null;
        var depth = 0;
        foreach (var line in Unfold(text))
        {
            var (name, parameters, value) = Split(line);
            if (name == "BEGIN")
            {
                if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current is null)
                {
                    current = new Dictionary<string, List<(Dictionary<string, string>, string)>>(StringComparer.OrdinalIgnoreCase);
                    depth = 0;
                }
                else if (current is not null)
                    depth++;
                continue;
            }
            if (name == "END" && current is not null)
            {
                if (depth > 0)
                    depth--;
                else
                {
                    if (Build(current) is { } built)
                        events.Add(built);
                    current = null;
                }
                continue;
            }
            // Alarms inside an event have their own DESCRIPTION, TRIGGER, …: not the event's.
            if (current is null || depth > 0)
                continue;
            if (!current.TryGetValue(name, out var values))
                current[name] = values = [];
            values.Add((parameters, value));
        }
        return events;
    }

    private static Raw? Build(Dictionary<string, List<(Dictionary<string, string> Parameters, string Value)>> p)
    {
        if (!p.TryGetValue("DTSTART", out var starts) || ParseWhen(starts[0].Value, starts[0].Parameters) is not { } start)
            return null;

        TimeSpan duration;
        if (p.TryGetValue("DTEND", out var ends) && ParseWhen(ends[0].Value, ends[0].Parameters) is { } end)
            duration = end.AllDay || start.AllDay ? end.Wall.Date - start.Wall.Date : Wall(end, start.Zone) - start.Wall;
        else if (p.TryGetValue("DURATION", out var durations) && ParseDuration(durations[0].Value) is { } d)
            duration = d;
        else
            duration = start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        var skipped = new HashSet<DateTime>();
        if (p.TryGetValue("EXDATE", out var exdates))
        {
            foreach (var (parameters, value) in exdates)
            {
                foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (ParseWhen(part, parameters) is { } skip)
                        skipped.Add(start.AllDay || skip.AllDay ? skip.Wall.Date : Wall(skip, start.Zone));
                }
            }
        }

        When? recurrenceId = null;
        if (p.TryGetValue("RECURRENCE-ID", out var ids) && ParseWhen(ids[0].Value, ids[0].Parameters) is { } id)
            recurrenceId = new When(id.AllDay ? id.Wall.Date : Wall(id, start.Zone), start.Zone, id.AllDay);

        return new Raw(
            Text(p, "UID") ?? Guid.NewGuid().ToString(),
            Text(p, "SUMMARY") is { Length: > 0 } title ? title : "(no title)",
            Text(p, "LOCATION") is { Length: > 0 } location ? location : null,
            start,
            duration,
            p.TryGetValue("RRULE", out var rules) ? ParseRule(rules[0].Value, start) : null,
            skipped,
            recurrenceId,
            Text(p, "STATUS")?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? Text(Dictionary<string, List<(Dictionary<string, string> Parameters, string Value)>> p, string name) =>
        p.TryGetValue(name, out var values) ? Unescape(values[0].Value).Trim() : null;

    /// <summary>The start of each occurrence (wall-clock, in the event's zone) from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    internal static IEnumerable<DateTime> Occurrences(Raw e, DateTime from, DateTime to)
    {
        var start = e.Start.Wall;
        if (e.Rule is not { } rule)
        {
            if (start >= from && start < to)
                yield return start;
            yield break;
        }

        var count = 0;
        // Plenty for daily repeats from years back; stops a broken rule from running forever.
        const int MaxCandidates = 200_000;
        var candidates = 0;
        foreach (var candidate in Candidates(rule, start))
        {
            if (++candidates > MaxCandidates || candidate >= to)
                yield break;
            if (rule.Until is { } until && candidate > until)
                yield break;
            if (candidate < start)
                continue;
            count++;
            if (rule.Count is { } limit && count > limit)
                yield break;
            if (e.Skipped.Contains(e.Start.AllDay ? candidate.Date : candidate))
                continue;
            if (candidate >= from)
                yield return candidate;
        }
    }

    /// <summary>
    /// Every date the rule allows from the first period on, in order, at the start's time of day. Stops after
    /// <see cref="MaxPeriods"/> periods (or near year 9999), so a rule that can never match (the 30th of every February)
    /// ends instead of looping.
    /// </summary>
    private static IEnumerable<DateTime> Candidates(Rule rule, DateTime start)
    {
        var time = start.TimeOfDay;
        switch (rule.Frequency)
        {
            case "DAILY":
            {
                var day = start.Date;
                for (var k = 0; k < MaxPeriods && day.Year < 9000; k++, day = day.AddDays(rule.Interval))
                {
                    if (rule.ByMonth.Count == 0 || rule.ByMonth.Contains(day.Month))
                        yield return day + time;
                }
                yield break;
            }

            case "WEEKLY":
            {
                var days = rule.ByDay.Count > 0 ? rule.ByDay.Select(d => d.Day).Distinct().ToList() : [start.DayOfWeek];
                // Weeks start on Monday (the default WKST).
                var week = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
                for (var k = 0; k < MaxPeriods && week.Year < 9000; k++, week = week.AddDays(7 * rule.Interval))
                {
                    foreach (var day in days.Select(d => week.AddDays(((int)d + 6) % 7)).Order())
                        yield return day + time;
                }
                yield break;
            }

            case "MONTHLY":
            {
                var month = new DateTime(start.Year, start.Month, 1);
                for (var k = 0; k < MaxPeriods && month.Year < 9000; k++, month = month.AddMonths(rule.Interval))
                {
                    foreach (var day in DaysInMonth(rule, month, start.Day))
                        yield return day + time;
                }
                yield break;
            }

            case "YEARLY":
            {
                var months = rule.ByMonth.Count > 0 ? rule.ByMonth.Distinct().Order().ToList() : [start.Month];
                for (var year = start.Year; year < 9000; year += rule.Interval)
                {
                    foreach (var m in months)
                    {
                        // Nothing but BYMONTH keeps the start's day (and the 29th of February only comes in leap years).
                        foreach (var day in DaysInMonth(rule, new DateTime(year, m, 1), start.Day))
                            yield return day + time;
                    }
                }
                yield break;
            }

            default:
                yield return start;
                yield break;
        }
    }

    private const int MaxPeriods = 100_000;

    private static readonly Dictionary<string, string> NoParameters = new();

    private static IEnumerable<DateTime> DaysInMonth(Rule rule, DateTime month, int defaultDay)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        var days = new SortedSet<int>();
        foreach (var n in rule.ByMonthDay)
        {
            var day = n > 0 ? n : length + n + 1;
            if (day >= 1 && day <= length)
                days.Add(day);
        }
        foreach (var (ordinal, weekday) in rule.ByDay)
        {
            var matching = Enumerable.Range(1, length).Where(d => month.AddDays(d - 1).DayOfWeek == weekday).ToList();
            if (ordinal == 0)
                days.UnionWith(matching);
            else if (Math.Abs(ordinal) <= matching.Count)
                days.Add(ordinal > 0 ? matching[ordinal - 1] : matching[^-ordinal]);
        }
        if (rule.ByMonthDay.Count == 0 && rule.ByDay.Count == 0 && defaultDay <= length)
            days.Add(defaultDay);
        return days.Select(d => month.AddDays(d - 1));
    }

    internal static Rule? ParseRule(string text, When start)
    {
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First()[1].Trim());
        if (!parts.TryGetValue("FREQ", out var frequency))
            return null;
        frequency = frequency.ToUpperInvariant();
        if (frequency is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY"))
            return null;

        var interval = parts.TryGetValue("INTERVAL", out var i) && int.TryParse(i, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
        int? count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, CultureInfo.InvariantCulture, out var cn) && cn > 0 ? cn : null;
        DateTime? until = null;
        if (parts.TryGetValue("UNTIL", out var u) && ParseWhen(u, NoParameters) is { } end)
            until = end.AllDay ? end.Wall.Date.AddDays(1).AddTicks(-1) : Wall(end, start.Zone);

        var byDay = new List<(int, DayOfWeek)>();
        if (parts.TryGetValue("BYDAY", out var days))
        {
            foreach (var day in days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (day.Length < 2 || !Weekdays.TryGetValue(day[^2..].ToUpperInvariant(), out var weekday))
                    continue;
                var ordinal = day.Length > 2 && int.TryParse(day[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var o) ? o : 0;
                byDay.Add((ordinal, weekday));
            }
        }

        return new Rule(frequency, interval, count, until, byDay, Numbers(parts, "BYMONTHDAY"), Numbers(parts, "BYMONTH").Where(m => m is >= 1 and <= 12).ToList());
    }

    private static List<int> Numbers(Dictionary<string, string> parts, string key) =>
        parts.TryGetValue(key, out var text)
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .Where(v => v != 0)
                .ToList()
            : [];

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday, ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday, ["SU"] = DayOfWeek.Sunday,
    };

    /// <summary>A DATE (20261011) or DATE-TIME (20261011T090000, with Z for UTC or a TZID parameter).</summary>
    internal static When? ParseWhen(string value, IReadOnlyDictionary<string, string> parameters)
    {
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new When(date, null, true);
        var utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(utc ? value[..^1] : value, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return null;
        if (utc)
            return new When(wall, TimeZoneInfo.Utc, false);
        return new When(wall, parameters.TryGetValue("TZID", out var tzid) ? FindZone(tzid) : null, false);
    }

    /// <summary>A TZID as calendars write it: an IANA name, a Windows name, or Outlook's display name. Null (local time) if unknown.</summary>
    internal static TimeZoneInfo? FindZone(string tzid)
    {
        tzid = tzid.Trim().Trim('"').TrimStart('/');
        if (tzid.Length == 0)
            return null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(tzid, out var zone))
            return zone;
        return TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.DisplayName.Equals(tzid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>P1D, PT1H30M, P1W, -PT15M: the parts iCalendar durations use.</summary>
    internal static TimeSpan? ParseDuration(string text)
    {
        text = text.Trim().ToUpperInvariant();
        var negative = text.StartsWith('-');
        text = text.TrimStart('+', '-');
        if (!text.StartsWith('P'))
            return null;
        var total = TimeSpan.Zero;
        var number = 0;
        var any = false;
        foreach (var ch in text[1..])
        {
            if (char.IsAsciiDigit(ch))
            {
                number = number * 10 + (ch - '0');
                continue;
            }
            switch (ch)
            {
                case 'W': total += TimeSpan.FromDays(7 * number); break;
                case 'D': total += TimeSpan.FromDays(number); break;
                case 'H': total += TimeSpan.FromHours(number); break;
                case 'M': total += TimeSpan.FromMinutes(number); break;
                case 'S': total += TimeSpan.FromSeconds(number); break;
                case 'T': break;
                default: return null;
            }
            any |= ch != 'T';
            number = 0;
        }
        return any ? negative ? -total : total : null;
    }

    /// <summary>A time's wall-clock in <paramref name="zone"/> (null = local/floating, kept as written).</summary>
    private static DateTime Wall(When when, TimeZoneInfo? zone) =>
        when.Zone is null || zone is null || when.Zone == zone ? when.Wall : TimeZoneInfo.ConvertTime(when.Wall, when.Zone, zone);

    private static DateTime ToLocal(DateTime wall, TimeZoneInfo? zone, TimeZoneInfo local) =>
        zone is null ? wall : TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), zone, local);

    private static DateTime ToZone(DateTime localTime, TimeZoneInfo local, TimeZoneInfo? zone) =>
        zone is null ? localTime : TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), local, zone);

    /// <summary>Lines with their folded continuations (a leading space or tab) joined back on.</summary>
    internal static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }
            if (current.Length > 0)
                yield return current.ToString();
            current.Clear().Append(line);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>NAME;PARAM=VALUE;…:value, with quoted parameter values that may hold : and ;.</summary>
    internal static (string Name, Dictionary<string, string> Parameters, string Value) Split(string line)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon < 0)
            return (line.Trim().ToUpperInvariant(), parameters, string.Empty);

        var head = line[..colon];
        var pieces = new List<string>();
        var piece = new StringBuilder();
        quoted = false;
        foreach (var ch in head)
        {
            if (ch == '"')
                quoted = !quoted;
            if (ch == ';' && !quoted)
            {
                pieces.Add(piece.ToString());
                piece.Clear();
            }
            else
                piece.Append(ch);
        }
        pieces.Add(piece.ToString());
        foreach (var p in pieces.Skip(1))
        {
            var equals = p.IndexOf('=');
            if (equals > 0)
                parameters[p[..equals].Trim()] = p[(equals + 1)..].Trim().Trim('"');
        }
        return (pieces[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    /// <summary>Text values escape commas, semicolons, backslashes and line breaks.</summary>
    internal static string Unescape(string text)
    {
        if (!text.Contains('\\'))
            return text;
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[++i];
                result.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
                result.Append(text[i]);
        }
        return result.ToString();
    }
}

using System.Globalization;
using System.Text;
using ImpiousBonum.Core.Calendar;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>A calendar's secret iCal address (Google's "Secret address in iCal format", Outlook's published ICS link, …).</summary>
public sealed record CalendarLink(string Name, string Url);

/// <summary>
/// What's coming up in your calendars: each link fetched every 15 minutes, the next event and what's on now as metrics,
/// and every event from today to two weeks ahead (an <c>IReadOnlyList&lt;CalendarEvent&gt;</c>) as an attachment for
/// the agenda widget. The metrics cover all the calendars, and each calendar has its own as well (see <see cref="IdFor"/>).
/// Nothing is fetched until a link is added.
/// </summary>
/// <param name="links">The calendars; read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets a link's .ics text; HTTP unless a test swaps it.</param>
/// <param name="local">This PC's time zone; tests pass their own.</param>
public sealed class CalendarProvider(
    Func<IReadOnlyList<CalendarLink>> links,
    Func<string, CancellationToken, Task<string>>? fetch = null,
    TimeProvider? time = null,
    TimeZoneInfo? local = null) : IMetricProvider
{
    public const string NextTitle = "calendar.next.title";
    public const string NextTime = "calendar.next.time";
    public const string NextLocation = "calendar.next.location";
    public const string NextMinutes = "calendar.next.minutes";
    public const string NowTitle = "calendar.now.title";
    public const string Today = "calendar.today";
    public const string Status = "calendar.status";

    /// <summary>The events, an <c>IReadOnlyList&lt;CalendarEvent&gt;</c> soonest first, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Events = "calendar.events";

    public const int DaysAhead = 14;

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    /// <summary>After a failed fetch, how long until trying that link again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>How long a calendar's events stay when new ones can't be fetched.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<string, CancellationToken, Task<string>> _fetch = fetch ?? GetAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeZoneInfo _local = local ?? TimeZoneInfo.Local;
    private readonly Dictionary<string, Fetched> _calendars = new();
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
    private string _registeredFor = string.Empty;
    private IReadOnlyList<CalendarEvent> _events = [];
    private DateTime _expandedFor;
    private bool _changed;

    private sealed class Fetched
    {
        public string? Text;
        public DateTimeOffset At;
        public DateTimeOffset NextTry;
        public string? Problem;
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(NextTitle, "Calendar: next event", "Calendar", MetricUnit.Text));
        store.Register(new MetricDefinition(NextTime, "Calendar: when the next event starts, e.g. 14:30 or Tomorrow 09:00", "Calendar", MetricUnit.Text));
        store.Register(new MetricDefinition(NextLocation, "Calendar: where the next event is", "Calendar", MetricUnit.Text));
        store.Register(new MetricDefinition(NextMinutes, "Calendar: minutes until the next event", "Calendar", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(NowTitle, "Calendar: what's on now", "Calendar", MetricUnit.Text));
        store.Register(new MetricDefinition(Today, "Calendar: events left today", "Calendar", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Status, "Calendar: status", "Calendar", MetricUnit.Text));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var utc = _time.GetUtcNow();
        var now = TimeZoneInfo.ConvertTime(utc, _local).DateTime;
        var wanted = links().Where(l => !string.IsNullOrWhiteSpace(l.Url)).ToList();

        // Forget calendars that were removed.
        foreach (var gone in _calendars.Keys.Where(k => wanted.All(l => Key(l) != k)).ToList())
        {
            _calendars.Remove(gone);
            _changed = true;
        }

        foreach (var link in wanted)
        {
            if (!_calendars.TryGetValue(Key(link), out var calendar))
                _calendars[Key(link)] = calendar = new Fetched { NextTry = utc };
            if (utc < calendar.NextTry)
                continue;
            try
            {
                calendar.Text = await _fetch(WebAddress(link.Url), cancellationToken).ConfigureAwait(false);
                calendar.At = utc;
                calendar.Problem = calendar.Text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) ? null : "that link isn't a calendar";
                calendar.NextTry = utc + RefreshEvery;
                _changed = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException or IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                calendar.Problem = ex is TaskCanceledException ? "it took too long to answer" : ex.Message;
                calendar.NextTry = utc + RetryAfter;
            }
            if (calendar.Text is not null && utc - calendar.At > KeepFor)
            {
                calendar.Text = null;
                _changed = true;
            }
        }

        if (_changed || now.Date != _expandedFor)
        {
            _changed = false;
            _expandedFor = now.Date;
            _events = Expand(wanted, now.Date);
        }
        Publish(store, wanted, now);
    }

    /// <summary>
    /// The id of one calendar's own metric: <c>calendar.work.next.title</c> for <paramref name="metric"/>
    /// <c>next.title</c> of a calendar named "Work", or <c>calendar.family-birthdays.today</c> for "Family Birthdays".
    /// Null for a name with no letters or digits.
    /// </summary>
    public static string? IdFor(string calendarName, string metric) =>
        Slug(calendarName) is { Length: > 0 } slug ? $"calendar.{slug}.{metric}" : null;

    /// <summary>Lower case, with each run of anything but letters and digits a single hyphen.</summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        return builder.ToString().TrimEnd('-');
    }

    /// <summary>The calendars with metrics of their own; of two whose names give the same id, only the first.</summary>
    private static List<CalendarLink> WithOwnMetrics(IReadOnlyList<CalendarLink> wanted) =>
        wanted.Where(l => Slug(l.Name).Length > 0).DistinctBy(l => Slug(l.Name)).ToList();

    /// <summary>Registers each calendar's own metrics, and removes those of calendars that are gone or renamed.</summary>
    private void Sync(MetricStore store, IReadOnlyList<CalendarLink> own)
    {
        var names = string.Join('\n', own.Select(l => l.Name));
        if (names == _registeredFor)
            return;
        _registeredFor = names;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in own)
        {
            var category = $"Calendar: {link.Name}";
            Register(NextTitle, "next event");
            Register(NextTime, "when the next event starts");
            Register(NextLocation, "where the next event is");
            Register(NextMinutes, "minutes until the next event", "0");
            Register(NowTitle, "what's on now");
            Register(Today, "events left today", "0");

            void Register(string all, string what, string? format = null)
            {
                var id = IdFor(link.Name, all["calendar.".Length..])!;
                ids.Add(id);
                store.Register(new MetricDefinition(id, $"{link.Name}: {what}", category, format is null ? MetricUnit.Text : MetricUnit.None, Format: format));
            }
        }
        foreach (var gone in _registered.Except(ids, StringComparer.OrdinalIgnoreCase).ToList())
            store.Unregister(gone);
        _registered.Clear();
        _registered.UnionWith(ids);
    }

    private IReadOnlyList<CalendarEvent> Expand(IReadOnlyList<CalendarLink> wanted, DateTime today)
    {
        var events = new List<CalendarEvent>();
        foreach (var link in wanted)
        {
            if (_calendars.TryGetValue(Key(link), out var calendar) && calendar.Text is { } text)
                events.AddRange(ICalendar.Events(text, link.Name, today, today.AddDays(DaysAhead + 1), _local));
        }
        return events.OrderBy(e => e.Start).ThenBy(e => !e.AllDay).ToList();
    }

    private void Publish(MetricStore store, IReadOnlyList<CalendarLink> wanted, DateTime now)
    {
        var any = wanted.Count > 0 && _calendars.Values.Any(c => c.Text is not null);
        Publish(store, id => $"calendar.{id}", any ? _events : null, now);
        store.SetText(Status, Describe(wanted));
        store.SetAttachment(Events, any ? _events : null);

        var own = WithOwnMetrics(wanted);
        Sync(store, own);
        foreach (var link in own)
        {
            var read = _calendars.TryGetValue(Key(link), out var calendar) && calendar.Text is not null;
            Publish(store, id => IdFor(link.Name, id)!, read ? _events.Where(e => e.Calendar == link.Name).ToList() : null, now);
        }
    }

    /// <summary>The next event, what's on now and how many are left today from <paramref name="events"/>; null clears them.</summary>
    private static void Publish(MetricStore store, Func<string, string> id, IReadOnlyList<CalendarEvent>? events, DateTime now)
    {
        var next = events?.FirstOrDefault(e => !e.AllDay && e.Start >= now);
        var current = events?.Where(e => !e.AllDay && e.Start <= now && e.End > now).OrderByDescending(e => e.Start).FirstOrDefault();

        store.SetText(id("next.title"), next?.Title);
        store.SetText(id("next.time"), next is null ? null : When(next.Start, now));
        store.SetText(id("next.location"), next?.Location);
        store.Set(id("next.minutes"), next is null ? null : Math.Floor((next.Start - now).TotalMinutes));
        store.SetText(id("now.title"), current?.Title);
        store.Set(id("today"), events?.Count(e => e.End > now && e.Start < now.Date.AddDays(1)));
    }

    private string? Describe(IReadOnlyList<CalendarLink> wanted)
    {
        if (wanted.Count == 0)
            return null;
        var problems = wanted
            .Where(l => _calendars.TryGetValue(Key(l), out var c) && c.Problem is not null)
            .Select(l => $"couldn't read {l.Name}: {_calendars[Key(l)].Problem}")
            .ToList();
        if (problems.Count > 0)
            return char.ToUpper(problems[0][0], CultureInfo.CurrentCulture) + string.Join("; ", problems)[1..];
        if (_calendars.Values.All(c => c.Text is null))
            return "Fetching…";
        return wanted.Count == 1 ? "1 calendar" : $"{wanted.Count} calendars";
    }

    /// <summary>"14:30" today, "Tomorrow 09:00", "Wed 09:00" within a week, else "14 Oct 09:00".</summary>
    internal static string When(DateTime start, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        var time = start.ToString("t", culture);
        var days = (start.Date - now.Date).Days;
        return days switch
        {
            0 => time,
            1 => $"Tomorrow {time}",
            < 7 => $"{start.ToString("ddd", culture)} {time}",
            _ => $"{start.ToString("d MMM", culture)} {time}",
        };
    }

    /// <summary>webcal:// links are https:// underneath.</summary>
    public static string WebAddress(string url)
    {
        url = url.Trim();
        return url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + url["webcal://".Length..] : url;
    }

    private static string Key(CalendarLink link) => link.Url.Trim();

    private static Task<string> GetAsync(string url, CancellationToken cancellationToken) => Http.GetStringAsync(url, cancellationToken);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }

    public void Dispose()
    {
    }
}

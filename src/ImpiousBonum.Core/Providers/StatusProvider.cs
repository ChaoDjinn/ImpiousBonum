using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.StatusChecks;

namespace ImpiousBonum.Core.Providers;

/// <summary>One check's latest state, as the status widget shows it.</summary>
/// <param name="Name">The check's name, e.g. NAS.</param>
/// <param name="Up">Up or down; null until its first check has finished.</param>
/// <param name="Milliseconds">How long its answer took, while it's up.</param>
/// <param name="Since">How long it's been up or down, e.g. "12 min" (see <see cref="StatusProvider.Duration"/>).</param>
/// <param name="Detail">Why it's down, e.g. "No answer"; null while it's up.</param>
public sealed record CheckStatus(string Name, bool? Up, double? Milliseconds, string? Since, string? Detail);

/// <summary>
/// Whether your own servers and services are up (see <see cref="StatusCheck"/>): each checked on its own schedule and in
/// the background, so a slow one never holds up the rest. One that fails is checked again after <see cref="Recheck"/>,
/// and only counts as down if that fails too. Each check has its own readings, how many are up and down and a summary
/// cover them all, and the list of them (an <c>IReadOnlyList&lt;CheckStatus&gt;</c>, in the Status checks page's
/// order) is an attachment for the status widget. Nothing is checked until a check is added.
/// </summary>
/// <param name="checks">The checks; read every second, so changes apply quickly. An edit gives a new list rather than changing this one.</param>
/// <param name="check">Checks one; <see cref="StatusChecker"/> unless a test swaps it.</param>
public sealed class StatusProvider(
    Func<IReadOnlyList<StatusCheck>> checks,
    Func<StatusCheck, CancellationToken, Task<CheckResult>>? check = null,
    TimeProvider? time = null) : IMetricProvider
{
    public const string UpCount = "status.up";
    public const string DownCount = "status.down";
    public const string Summary = "status.summary";

    /// <summary>The checks' latest states, an <c>IReadOnlyList&lt;CheckStatus&gt;</c>, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Checks = "status.checks";

    /// <summary>How soon a check that has just failed is tried again, before it counts as down.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(StatusCheck.MinimumEvery);

    private const string Category = "Status checks";

    private readonly Func<StatusCheck, CancellationToken, Task<CheckResult>> _check = check ?? StatusChecker.CheckAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private IReadOnlyList<StatusCheck>? _seen;
    private List<StatusCheck> _usable = [];
    private string _published = "\0";

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(UpCount, "Status checks: how many are up", Category, MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(DownCount, "Status checks: how many are down", Category, MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Summary, "Status checks: All up, or which are down, e.g. 1 down: NAS", Category, MetricUnit.Text));
        Follow(store);
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var usable = Follow(store);
        var now = _time.GetUtcNow();
        foreach (var each in usable)
        {
            var state = _states[each.Slug];
            if (state.Busy || now < state.Due)
                continue;
            state.Busy = true;
            state.Due = now + each.Interval;
            _ = RunAsync(each, state);
        }

        // What's come back since the last sample: up or down from now.
        foreach (var each in usable)
        {
            var state = _states[each.Slug];
            if (state.Latest is not { } result || ReferenceEquals(result, state.Applied) || ReferenceEquals(result, state.Doubted))
                continue;
            if (!result.Up && state.Applied?.Up != false && state.Doubted is null)
            {
                // Down, when it was up or hadn't been checked: checked again soon, and down only if that fails too, so
                // one lost answer doesn't count.
                state.Doubted = result;
                state.Due = Min(state.Due, now + Recheck);
                continue;
            }
            state.Doubted = null;
            if (state.Applied?.Up != result.Up)
                state.Changed = now;
            state.Applied = result;
        }
        Publish(store, usable, now);
        return ValueTask.CompletedTask;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>Checks one now and returns what it found, for the check window's Check now button.</summary>
    public static Task<CheckResult> TestAsync(StatusCheck check, CancellationToken cancellationToken) => StatusChecker.CheckAsync(check, cancellationToken);

    private async Task RunAsync(StatusCheck each, State state)
    {
        try
        {
            var result = await _check(each, _stop.Token).ConfigureAwait(false);
            // A check changed or removed while it was being checked: drop the result.
            if (!state.Replaced)
                state.Latest = result;
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            state.Busy = false;
        }
    }

    /// <summary>Sets the readings when something on show has changed: a check's result, or how long it's been up or down.</summary>
    private void Publish(MetricStore store, IReadOnlyList<StatusCheck> usable, DateTimeOffset now)
    {
        var statuses = usable.Select(each =>
        {
            var applied = _states[each.Slug].Applied;
            return new CheckStatus(each.Name.Trim(), applied?.Up, applied is { Up: true } ? applied.Milliseconds : null,
                applied is null ? null : Duration(now - _states[each.Slug].Changed), applied?.Detail);
        }).ToList();
        var key = string.Join('\n', statuses);
        if (key == _published)
            return;
        _published = key;
        Publish(store, statuses);
    }

    /// <summary>Sets every reading from <paramref name="statuses"/>, in the Status checks page's order. Their metrics must be registered (<see cref="Register"/>).</summary>
    public static void Publish(MetricStore store, IReadOnlyList<CheckStatus> statuses)
    {
        foreach (var status in statuses)
        {
            store.Set(StatusCheck.IdFor(status.Name, StatusCheck.Up)!, status.Up is { } up ? up ? 1 : 0 : null);
            store.Set(StatusCheck.IdFor(status.Name, StatusCheck.Ms)!, status.Milliseconds);
            store.SetText(StatusCheck.IdFor(status.Name, StatusCheck.Since)!, status.Since);
        }
        var any = statuses.Count > 0;
        store.Set(UpCount, any ? statuses.Count(s => s.Up == true) : null);
        store.Set(DownCount, any ? statuses.Count(s => s.Up == false) : null);
        store.SetText(Summary, Summarise(statuses));
        store.SetAttachment(Checks, any ? statuses : null);
    }

    /// <summary>"All up", "1 down: NAS", "2 down: NAS, Plex", or "Checking…" until every check has been; null without any.</summary>
    public static string? Summarise(IReadOnlyList<CheckStatus> statuses)
    {
        if (statuses.Count == 0)
            return null;
        var down = statuses.Where(s => s.Up == false).Select(s => s.Name).ToList();
        if (down.Count > 0)
            return $"{down.Count} down: {string.Join(", ", down)}";
        return statuses.Any(s => s.Up is null) ? "Checking…" : "All up";
    }

    /// <summary>How long something's been up or down: "&lt;1 min", "12 min", "3 h", "1 day", "4 days".</summary>
    public static string Duration(TimeSpan span) =>
        span.TotalMinutes < 1 ? "<1 min"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h"
        : (int)span.TotalDays == 1 ? "1 day"
        : $"{(int)span.TotalDays} days";

    /// <summary>Registers one check's readings, listed in the metric picker under <c>Status: NAS</c> and so on.</summary>
    public static IReadOnlyList<string> Register(MetricStore store, string name)
    {
        var category = $"Status: {name}";
        MetricDefinition[] definitions =
        [
            new(StatusCheck.IdFor(name, StatusCheck.Up)!, $"{name}: up (1) or down (0)", category, MetricUnit.None, 1, Format: "0"),
            new(StatusCheck.IdFor(name, StatusCheck.Ms)!, $"{name}: response time", category, MetricUnit.Milliseconds),
            new(StatusCheck.IdFor(name, StatusCheck.Since)!, $"{name}: how long it's been up or down, e.g. 3 h", category, MetricUnit.Text),
        ];
        foreach (var definition in definitions)
            store.Register(definition);
        return [.. definitions.Select(d => d.Id)];
    }

    /// <summary>
    /// The checks that can be used; of two whose names give the same id, only the first. The settings replace the list
    /// as a whole when it's edited, so the same list as last time needs no second look.
    /// </summary>
    private List<StatusCheck> Follow(MetricStore store)
    {
        var current = checks();
        if (ReferenceEquals(current, _seen))
            return _usable;
        _seen = current;
        _usable = current.Where(c => c is not null && c.Problem() is null).DistinctBy(c => c.Slug, StringComparer.OrdinalIgnoreCase).ToList();
        Sync(store, _usable);
        return _usable;
    }

    /// <summary>Registers the readings of new and changed checks and removes those of checks that are gone.</summary>
    private void Sync(MetricStore store, IReadOnlyList<StatusCheck> usable)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var each in usable)
        {
            if (!_states.TryGetValue(each.Slug, out var state) || state.Key != each.Key)
            {
                // New or edited: checked straight away, and up or down afresh.
                if (state is not null)
                    state.Replaced = true;
                _states[each.Slug] = new State(each.Key) { Due = DateTimeOffset.MinValue };
                Register(store, each.Name.Trim());
            }
            wanted.UnionWith([each.IdFor(StatusCheck.Up), each.IdFor(StatusCheck.Ms), each.IdFor(StatusCheck.Since)]);
        }

        foreach (var gone in _registered.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList())
            store.Unregister(gone);
        foreach (var slug in _states.Keys.Where(slug => !usable.Any(c => c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _states[slug].Replaced = true;
            _states.Remove(slug);
        }
        _registered.Clear();
        _registered.UnionWith(wanted);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private sealed class State(string key)
    {
        /// <summary>The check's settings when this state was made (see <see cref="StatusCheck.Key"/>).</summary>
        public string Key { get; } = key;

        public DateTimeOffset Due { get; set; }

        public volatile bool Busy;

        /// <summary>The check was edited or removed: one still in flight is dropped.</summary>
        public volatile bool Replaced;

        /// <summary>What the last check to finish found, written from the background.</summary>
        public volatile CheckResult? Latest;

        /// <summary>The result on show, from <see cref="Latest"/> at the last sample.</summary>
        public CheckResult? Applied { get; set; }

        /// <summary>A first failure, waiting for the check again to say whether it's really down.</summary>
        public CheckResult? Doubted { get; set; }

        /// <summary>When it last went up or down, or was first checked.</summary>
        public DateTimeOffset Changed { get; set; }
    }
}

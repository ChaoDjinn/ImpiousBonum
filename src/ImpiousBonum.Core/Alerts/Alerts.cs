using System.Globalization;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Alerts;

/// <summary>
/// Tells you when a reading has been out of bounds for a while, e.g. GPU hot spot above 95 °C for 10 seconds:
/// a Windows notification, and optionally a banner flashing on the dashboard.
/// </summary>
/// <param name="Metric">The metric to watch, e.g. <c>gpu.hotspot</c>.</param>
/// <param name="For">Seconds the condition must hold before the alert goes off, so a brief spike doesn't count.</param>
/// <param name="Message">What the alert says; a template, so <c>{gpu.hotspot}</c> shows the reading. Empty describes the rule.</param>
/// <param name="Notify">Show a Windows notification.</param>
/// <param name="Flash">Flash a banner on the dashboard until the reading is back to normal or it's tapped.</param>
public sealed record AlertRule(
    string Metric,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Above = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Below = null,
    int For = 10,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    bool Notify = true,
    bool Flash = false)
{
    /// <summary>Watches a metric, has a limit, and does something.</summary>
    [JsonIgnore]
    public bool IsUsable => !string.IsNullOrWhiteSpace(Metric) && (Above is not null || Below is not null) && (Notify || Flash) && For >= 0;

    /// <summary>True when the reading is out of bounds; false when it's within them or there's no reading.</summary>
    public bool IsBreached(MetricSample sample) =>
        sample.Value is double value && !double.IsNaN(value) && (value > Above || value < Below);

    /// <summary>"gpu.hotspot above 95 for 10 s", for menus and the log.</summary>
    public string Describe()
    {
        var limits = string.Join(" or ", new[]
        {
            Above is { } above ? $"above {Number(above)}" : null,
            Below is { } below ? $"below {Number(below)}" : null,
        }.OfType<string>());
        return $"{Metric} {limits}{(For > 0 ? $" for {For} s" : "")}";
    }

    /// <summary>The alert's text: the message (with live values), or the metric's name, limit and reading.</summary>
    public string Text(MetricStore store)
    {
        if (!string.IsNullOrWhiteSpace(Message))
            return ValueTemplate.Parse(Message).Render(store);
        if (!store.TryGet(Metric, out var sample))
            return Describe();
        var name = sample.Definition.Name;
        var limit = sample.Value > Above ? $"above {Number(Above!.Value)}" : $"below {Number(Below ?? 0)}";
        return $"{name} {limit}: {MetricFormatter.Format(sample)}";
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}

/// <summary>An alert going off, or its reading back to normal.</summary>
public sealed record AlertEvent(AlertRule Rule, bool Started, string Text);

/// <summary>
/// Watches the alert rules each second. A rule goes off once its condition has held for its <see cref="AlertRule.For"/>
/// seconds, and only once: it goes off again only after the reading has been back to normal for <see cref="RearmAfter"/>,
/// so a value hovering around the limit doesn't send a stream of notifications.
/// </summary>
public sealed class AlertMonitor
{
    public static readonly TimeSpan RearmAfter = TimeSpan.FromSeconds(30);

    private readonly Dictionary<AlertRule, State> _states = [];

    /// <summary>The rules going off right now.</summary>
    public IReadOnlyList<AlertRule> Active => _states.Where(s => s.Value.Firing).Select(s => s.Key).ToList();

    public IReadOnlyList<AlertEvent> Update(IReadOnlyList<AlertRule> rules, MetricStore store, DateTime now)
    {
        var events = new List<AlertEvent>();
        foreach (var gone in _states.Keys.Except(rules).ToList())
            _states.Remove(gone);

        foreach (var rule in rules.Where(r => r is not null && r.IsUsable))
        {
            if (!_states.TryGetValue(rule, out var state))
                _states[rule] = state = new State();

            var breached = store.TryGet(rule.Metric, out var sample) && rule.IsBreached(sample);
            if (breached)
            {
                state.BreachedSince ??= now;
                state.NormalSince = null;
                if (!state.Firing && now - state.BreachedSince >= TimeSpan.FromSeconds(rule.For))
                {
                    state.Firing = true;
                    events.Add(new AlertEvent(rule, Started: true, rule.Text(store)));
                }
            }
            else
            {
                state.BreachedSince = null;
                state.NormalSince ??= now;
                if (state.Firing && now - state.NormalSince >= RearmAfter)
                {
                    state.Firing = false;
                    events.Add(new AlertEvent(rule, Started: false, rule.Text(store)));
                }
            }
        }
        return events;
    }

    private sealed class State
    {
        public DateTime? BreachedSince { get; set; }

        public DateTime? NormalSince { get; set; }

        public bool Firing { get; set; }
    }
}

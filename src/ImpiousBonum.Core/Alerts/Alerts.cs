using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Alerts;

/// <summary>
/// Tells you when a reading has been out of bounds for a while, e.g. GPU hot spot above 95 °C for 10 seconds:
/// a Windows notification, and optionally a banner flashing on the dashboard, a sound, a switch to another layout,
/// a command run, or a web request (see <see cref="AlertWebhook"/>). All but the banner happen once, when it goes off.
/// </summary>
/// <param name="Metric">The metric to watch, e.g. <c>gpu.hotspot</c>.</param>
/// <param name="For">Seconds the condition must hold before the alert goes off, so a brief spike doesn't count.</param>
/// <param name="Message">What the alert says; a template, so <c>{gpu.hotspot}</c> shows the reading. Empty describes the rule.</param>
/// <param name="Notify">Show a Windows notification.</param>
/// <param name="Flash">Flash a banner on the dashboard until the reading is back to normal or it's tapped.</param>
/// <param name="Sound">A sound to play: one of <see cref="SystemSounds"/>, or a .wav file.</param>
/// <param name="Layout">The layout to switch the dashboard to.</param>
/// <param name="Command">A program or script to run, with the alert in its environment (see <see cref="Environment"/>).</param>
/// <param name="Arguments">What to pass to <paramref name="Command"/>.</param>
/// <param name="Webhook">An http(s) address to send the alert to (see <see cref="AlertWebhook"/>).</param>
/// <param name="WebhookText">Send just the alert's text rather than JSON, as ntfy wants.</param>
public sealed record AlertRule(
    string Metric,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Above = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Below = null,
    int For = 10,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    bool Notify = true,
    bool Flash = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sound = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layout = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Command = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Arguments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Webhook = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool WebhookText = false)
{
    /// <summary>The Windows sounds an alert can play by name; anything else in <see cref="Sound"/> is a file.</summary>
    public static readonly IReadOnlyList<string> SystemSounds = ["Exclamation", "Asterisk", "Hand", "Question", "Beep"];

    /// <summary>The environment variables a <see cref="Command"/> gets: the alert, its text, the metric and its reading.</summary>
    public const string AlertVariable = "IB_ALERT", MessageVariable = "IB_ALERT_MESSAGE", MetricVariable = "IB_ALERT_METRIC", ValueVariable = "IB_ALERT_VALUE";

    /// <summary>Watches a metric, has a limit, and does something.</summary>
    [JsonIgnore]
    public bool IsUsable => !string.IsNullOrWhiteSpace(Metric) && (Above is not null || Below is not null) && Does().Count > 0 && For >= 0;

    /// <summary>What it does when it goes off, for the Alerts page: "notify", "play a sound", "switch to Gaming"….</summary>
    public IReadOnlyList<string> Does() => new[]
    {
        Notify ? "notify" : null,
        Flash ? "flash on the dashboard" : null,
        Has(Sound) ? "play a sound" : null,
        Has(Layout) ? $"switch to {Layout!.Trim()}" : null,
        Has(Command) ? $"run {Path.GetFileName(Command!.Trim())}" : null,
        AlertWebhook.Address(Webhook) is { } address ? $"send to {address.Host}" : null,
    }.OfType<string>().ToList();

    /// <summary>
    /// The variables a <see cref="Command"/> is run with, so a script can use the alert's text and reading without them
    /// being put in a command line, where a headline or a calendar event's title could add to the command.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment(string text, MetricStore store) => new Dictionary<string, string>
    {
        [AlertVariable] = Describe(),
        [MessageVariable] = text,
        [MetricVariable] = Metric,
        [ValueVariable] = store.TryGet(Metric, out var sample) && sample.Value is { } value ? value.ToString(CultureInfo.InvariantCulture) : string.Empty,
    };

    private static bool Has(string? text) => !string.IsNullOrWhiteSpace(text);

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

/// <summary>
/// The web request an alert sends: a POST of JSON (<c>{"alert": "gpu.hotspot above 95 for 10 s", "message": "…",
/// "metric": "gpu.hotspot", "value": 97.2, "time": "2026-10-14T21:30:00Z"}</c>, for Home Assistant's webhooks and the
/// like), or of just the text (for ntfy, which shows the body as the notification). <see cref="WebRequestSender"/>
/// sends it.
/// </summary>
public static class AlertWebhook
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The address, if it's an absolute http or https one.</summary>
    public static Uri? Address(string? webhook) => WebRequestSender.Address(webhook);

    public static HttpContent Content(AlertRule rule, string text, MetricStore store, DateTimeOffset time)
    {
        if (rule.WebhookText)
            return new StringContent(text, Encoding.UTF8, "text/plain");
        var body = new JsonObject
        {
            ["alert"] = rule.Describe(),
            ["message"] = text,
            ["metric"] = rule.Metric,
            ["value"] = store.TryGet(rule.Metric, out var sample) && sample.Value is { } value && double.IsFinite(value) ? value : null,
            ["time"] = time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    /// <summary>Sends it: null when it worked, or what went wrong.</summary>
    /// <param name="send">Sends the request; HTTP unless a test swaps it.</param>
    public static async Task<string?> SendAsync(AlertRule rule, string text, MetricStore store, DateTimeOffset time,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null, CancellationToken cancellationToken = default)
    {
        if (Address(rule.Webhook) is not { } address)
            return $"\"{rule.Webhook}\" isn't a web address";
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = Content(rule, text, store, time) };
        return await WebRequestSender.SendAsync(request, Timeout, send, cancellationToken).ConfigureAwait(false);
    }
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

using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class AlertTests
{
    private static readonly DateTime Start = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly AlertRule HotSpot = new("gpu.hotspot", Above: 95, For: 10);

    private static MetricStore Store()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("gpu.hotspot", "GPU hot spot temperature", "Sensors", MetricUnit.Celsius));
        store.Register(new MetricDefinition("fps", "Frames per second", "Sensors", MetricUnit.FramesPerSecond));
        return store;
    }

    /// <summary>Feeds one reading a second and returns, for each second, "+" when an alert started, "-" when one ended, "" otherwise.</summary>
    private static List<string> Run(AlertMonitor monitor, IReadOnlyList<AlertRule> rules, MetricStore store, string metric, params double?[] readings)
    {
        var marks = new List<string>();
        for (var i = 0; i < readings.Length; i++)
        {
            store.Set(metric, readings[i]);
            var events = monitor.Update(rules, store, Start.AddSeconds(i));
            marks.Add(string.Concat(events.Select(e => e.Started ? "+" : "-")));
        }
        return marks;
    }

    [Fact]
    public void An_alert_goes_off_once_the_reading_has_held_for_long_enough()
    {
        var monitor = new AlertMonitor();
        var readings = Enumerable.Repeat<double?>(97, 12).ToArray();
        var marks = Run(monitor, [HotSpot], Store(), "gpu.hotspot", readings);
        Assert.Equal(10, marks.IndexOf("+"));
        Assert.Single(marks, "+");
        Assert.Equal([HotSpot], monitor.Active);
    }

    [Fact]
    public void A_brief_spike_does_not_count()
    {
        var readings = new double?[] { 97, 97, 97, 90, 97, 97, 97, 97, 97, 97, 97, 97, 97, 90 };
        Assert.DoesNotContain("+", Run(new AlertMonitor(), [HotSpot], Store(), "gpu.hotspot", readings));
    }

    [Fact]
    public void It_ends_and_rearms_only_after_being_normal_for_a_while()
    {
        var monitor = new AlertMonitor();
        var store = Store();
        var hot = Enumerable.Repeat<double?>(97, 11);
        // Dips below the limit for 5 s (not enough to end it), then hot again: no second notification.
        var hovering = hot.Concat(Enumerable.Repeat<double?>(90, 5)).Concat(Enumerable.Repeat<double?>(97, 12)).ToArray();
        var marks = Run(monitor, [HotSpot], store, "gpu.hotspot", hovering);
        Assert.Single(marks, "+");
        Assert.DoesNotContain("-", marks);

        // Normal for 30 s ends it; then it can go off again.
        monitor = new AlertMonitor();
        var recovered = hot.Concat(Enumerable.Repeat<double?>(80, 31)).Concat(Enumerable.Repeat<double?>(97, 11)).ToArray();
        marks = Run(monitor, [HotSpot], store, "gpu.hotspot", recovered);
        Assert.Equal(2, marks.Count(m => m == "+"));
        Assert.Single(marks, "-");
    }

    [Fact]
    public void Below_limits_and_missing_readings()
    {
        var lowFps = new AlertRule("fps", Below: 30, For: 0);
        var store = Store();
        Assert.Equal(["+"], Run(new AlertMonitor(), [lowFps], store, "fps", 20));
        // No reading (no game running) isn't "below 30".
        Assert.Equal([""], Run(new AlertMonitor(), [lowFps], store, "fps", [null]));
    }

    [Fact]
    public void Alerts_say_what_happened()
    {
        var store = Store();
        store.Set("gpu.hotspot", 97.2);
        Assert.Equal("GPU hot spot temperature above 95: 97.2 °C", HotSpot.Text(store));
        Assert.Equal("GPU is cooking: 97.2 °C", (HotSpot with { Message = "GPU is cooking: {gpu.hotspot}" }).Text(store));
        Assert.Equal("gpu.hotspot above 95 for 10 s", HotSpot.Describe());
        Assert.Equal("fps above 200 or below 30", new AlertRule("fps", Above: 200, Below: 30, For: 0).Describe());
    }

    [Fact]
    public void Rules_need_a_metric_a_limit_and_something_to_do()
    {
        Assert.True(HotSpot.IsUsable);
        Assert.False((HotSpot with { Above = null }).IsUsable);
        Assert.False((HotSpot with { Notify = false, Flash = false }).IsUsable);
        Assert.False((HotSpot with { Metric = " " }).IsUsable);
    }

    [Fact]
    public void An_action_alone_is_enough_to_do()
    {
        var quiet = HotSpot with { Notify = false };
        Assert.False(quiet.IsUsable);
        Assert.True((quiet with { Sound = "Exclamation" }).IsUsable);
        Assert.True((quiet with { Webhook = "https://ntfy.sh/my-pc" }).IsUsable);
        Assert.False((quiet with { Webhook = "ntfy.sh/my-pc" }).IsUsable);
        Assert.False((quiet with { Command = "  " }).IsUsable);

        var all = HotSpot with { Flash = true, Sound = "Beep", Layout = " Gaming ", Command = @"C:\Scripts\fans.bat", Webhook = "http://homeassistant.local:8123/api/webhook/pc" };
        Assert.Equal(["notify", "flash on the dashboard", "play a sound", "switch to Gaming", "run fans.bat", "send to homeassistant.local"], all.Does());
    }

    [Fact]
    public void A_command_gets_the_alert_in_its_environment()
    {
        var store = Store();
        store.Set("gpu.hotspot", 97.25);
        var environment = HotSpot.Environment("Hot & bothered", store);
        Assert.Equal("gpu.hotspot above 95 for 10 s", environment[AlertRule.AlertVariable]);
        Assert.Equal("Hot & bothered", environment[AlertRule.MessageVariable]);
        Assert.Equal("gpu.hotspot", environment[AlertRule.MetricVariable]);
        Assert.Equal("97.25", environment[AlertRule.ValueVariable]);
    }

    [Fact]
    public async Task A_webhook_posts_json_or_just_the_text()
    {
        var store = Store();
        store.Set("gpu.hotspot", 97.2);
        var time = new DateTimeOffset(2026, 10, 14, 22, 30, 0, TimeSpan.FromHours(1));
        var rule = HotSpot with { Webhook = "https://example.com/hook" };
        HttpRequestMessage? sent = null;
        string? body = null;
        async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token)
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }

        Assert.Null(await AlertWebhook.SendAsync(rule, "GPU is cooking", store, time, Send));
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("https://example.com/hook", sent.RequestUri!.ToString());
        Assert.Equal("application/json", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"alert":"gpu.hotspot above 95 for 10 s","message":"GPU is cooking","metric":"gpu.hotspot","value":97.2,"time":"2026-10-14T21:30:00Z"}""", body);

        Assert.Null(await AlertWebhook.SendAsync(rule with { WebhookText = true }, "GPU is cooking", store, time, Send));
        Assert.Equal("text/plain", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("GPU is cooking", body);
    }

    [Fact]
    public async Task A_webhook_that_fails_says_why()
    {
        var store = Store();
        var rule = HotSpot with { Webhook = "https://example.com/hook" };
        var time = DateTimeOffset.UtcNow;

        Assert.Equal("example.com answered 404 Not Found", await AlertWebhook.SendAsync(rule, "", store, time,
            (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" })));
        Assert.Equal("couldn't reach example.com: No such host", await AlertWebhook.SendAsync(rule, "", store, time,
            (_, _) => throw new HttpRequestException("No such host")));
        Assert.Equal("example.com took too long to answer", await AlertWebhook.SendAsync(rule, "", store, time,
            (_, _) => throw new TaskCanceledException()));
        Assert.Equal("\"example.com\" isn't a web address", await AlertWebhook.SendAsync(rule with { Webhook = "example.com" }, "", store, time));
    }

    [Fact]
    public void Removed_rules_stop_counting()
    {
        var monitor = new AlertMonitor();
        var store = Store();
        Run(monitor, [HotSpot], store, "gpu.hotspot", Enumerable.Repeat<double?>(97, 11).ToArray());
        Assert.Single(monitor.Active);
        monitor.Update([], store, Start.AddMinutes(1));
        Assert.Empty(monitor.Active);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.StatusChecks;

namespace ImpiousBonum.Core.Tests;

public sealed class StatusCheckTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly StatusCheck Nas = new("NAS", "nas.local");

    [Theory]
    [InlineData("NAS", "status.nas.up")]
    [InlineData("Pi-hole", "status.pi-hole.up")]
    [InlineData("Home Assistant", "status.home-assistant.up")]
    [InlineData("  Plex  ", "status.plex.up")]
    public void Each_checks_readings_are_named_after_it(string name, string id)
    {
        Assert.Equal(id, new StatusCheck(name, "nas.local").IdFor(StatusCheck.Up));
        Assert.Equal(id, StatusCheck.IdFor(name, StatusCheck.Up));
        Assert.Equal(id[..^2] + "since", StatusCheck.IdFor(name, StatusCheck.Since));
    }

    [Theory]
    [InlineData("nas.local")]
    [InlineData("router")]
    [InlineData("192.168.1.10")]
    [InlineData("fe80::1")]
    [InlineData("http://pi.hole/admin")]
    [InlineData("HTTPS://plex.local:32400/web")]
    public void Host_names_ip_addresses_and_web_addresses_can_be_checked(string address) =>
        Assert.Null(new StatusCheck("Server", address).Problem());

    [Theory]
    [InlineData("", "nas.local", "a name")]
    [InlineData("!!", "nas.local", "a name")]
    [InlineData("NAS, upstairs", "nas.local", "comma")]
    [InlineData(" All ", "nas.local", "can't be called all")]
    [InlineData("NAS", " ", "Give it an address")]
    [InlineData("NAS", "ftp://nas.local", "http:// or https://")]
    [InlineData("NAS", "nas.local:5000", "http://nas.local:5000")]
    [InlineData("NAS", "nas.local/admin", "http://nas.local/admin")]
    [InlineData("NAS", "nas local", "host name or IP address")]
    [InlineData("NAS", "https://", "isn't complete")]
    public void A_check_that_cant_be_used_says_what_to_change(string name, string address, string says) =>
        Assert.Contains(says, new StatusCheck(name, address).Problem());

    [Fact]
    public void Checks_are_every_ten_seconds_or_more()
    {
        Assert.Contains("every 10 seconds or more", (Nas with { Every = 5 }).Problem());
        Assert.Null((Nas with { Every = 10 }).Problem());
        Assert.Equal(TimeSpan.FromSeconds(60), Nas.Interval);
        Assert.Equal(TimeSpan.FromSeconds(10), (Nas with { Every = 1 }).Interval);
        Assert.False(Nas.IsWeb);
        Assert.True(new StatusCheck("Pi-hole", " http://pi.hole/admin").IsWeb);
    }

    [Theory]
    [InlineData(30, "<1 min")]
    [InlineData(12 * 60 + 40, "12 min")]
    [InlineData(3 * 3600 + 1200, "3 h")]
    [InlineData(26 * 3600, "1 day")]
    [InlineData(6 * 86400 + 3600, "6 days")]
    public void How_long_reads_like_the_news_ages(int seconds, string expected) =>
        Assert.Equal(expected, StatusProvider.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void The_summary_says_which_are_down()
    {
        Assert.Null(StatusProvider.Summarise([]));
        Assert.Equal("Checking…", StatusProvider.Summarise([new("NAS", true, 4, "<1 min", null), new("Plex", null, null, null, null)]));
        Assert.Equal("All up", StatusProvider.Summarise([new("NAS", true, 4, "<1 min", null)]));
        Assert.Equal("2 down: NAS, Plex", StatusProvider.Summarise(
            [new("NAS", false, null, "3 h", "No answer"), new("Router", true, 2, "1 day", null), new("Plex", false, null, "12 min", "Couldn't connect")]));
    }

    [Fact]
    public async Task Nothing_is_checked_until_a_check_is_added()
    {
        IReadOnlyList<StatusCheck> checks = [];
        var asked = 0;
        using var provider = new StatusProvider(() => checks, (_, _) => { asked++; return Task.FromResult(CheckResult.Answered(4)); }, new FakeTime(Noon));
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, asked);
        Assert.Equal(MetricFormatter.Missing, Format(store, StatusProvider.UpCount));
        Assert.Equal(MetricFormatter.Missing, Format(store, StatusProvider.Summary));
        Assert.Null(store.GetAttachment(StatusProvider.Checks));

        // Nor one that can't be used.
        checks = [new StatusCheck("NAS", "ftp://nas.local")];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, asked);
        Assert.False(store.TryGet("status.nas.up", out _));
    }

    [Fact]
    public async Task Each_check_runs_on_its_own_schedule_and_has_its_own_readings()
    {
        var time = new FakeTime(Noon);
        var checkedNas = 0;
        var checkedRouter = 0;
        IReadOnlyList<StatusCheck> checks = [Nas with { Every = 30 }, new StatusCheck("Router", "192.168.1.1")];
        using var provider = new StatusProvider(() => checks, (check, _) =>
        {
            if (check.Name == "NAS")
                checkedNas++;
            else
                checkedRouter++;
            return Task.FromResult(CheckResult.Answered(check.Name == "NAS" ? 4 : 2));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        Assert.Equal("Status: NAS", store.Definitions.Single(d => d.Id == "status.nas.ms").Category);
        Assert.Equal(MetricUnit.Milliseconds, store.Definitions.Single(d => d.Id == "status.nas.ms").Unit);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal((1, 1), (checkedNas, checkedRouter));
        Assert.Equal("1", Format(store, "status.nas.up"));
        Assert.Equal("4 ms", Format(store, "status.nas.ms"));
        Assert.Equal("<1 min", Format(store, "status.nas.since"));
        Assert.Equal("2 ms", Format(store, "status.router.ms"));
        Assert.Equal("2", Format(store, StatusProvider.UpCount));
        Assert.Equal("0", Format(store, StatusProvider.DownCount));
        Assert.Equal("All up", Format(store, StatusProvider.Summary));
        var statuses = (IReadOnlyList<CheckStatus>)store.GetAttachment(StatusProvider.Checks)!;
        Assert.Equal(["NAS", "Router"], statuses.Select(s => s.Name));

        // NAS every 30 seconds, Router every 60.
        time.Advance(TimeSpan.FromSeconds(29));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal((1, 1), (checkedNas, checkedRouter));
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal((2, 1), (checkedNas, checkedRouter));
        time.Advance(TimeSpan.FromSeconds(30));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal((3, 2), (checkedNas, checkedRouter));

        // Up for a while.
        time.Advance(TimeSpan.FromMinutes(200));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("3 h", Format(store, "status.nas.since"));
    }

    [Fact]
    public async Task A_check_is_down_only_when_it_fails_twice_in_a_row()
    {
        var time = new FakeTime(Noon);
        var answers = new Queue<CheckResult>();
        var asked = 0;
        IReadOnlyList<StatusCheck> checks = [Nas];
        using var provider = new StatusProvider(() => checks, (_, _) =>
        {
            asked++;
            return Task.FromResult(answers.Count > 0 ? answers.Dequeue() : CheckResult.Answered(4));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(90));

        // One lost answer: still up, and checked again 10 seconds later rather than in a minute.
        answers.Enqueue(CheckResult.Down("No answer"));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("1", Format(store, "status.nas.up"));
        time.Advance(TimeSpan.FromSeconds(10));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, asked);
        // It answered that time, so it's been up all along.
        Assert.Equal("1", Format(store, "status.nas.up"));
        Assert.Equal("1 h", Format(store, "status.nas.since"));

        // Two in a row: down, from the second.
        time.Advance(TimeSpan.FromSeconds(60));
        answers.Enqueue(CheckResult.Down("No answer"));
        answers.Enqueue(CheckResult.Down("No answer"));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("1", Format(store, "status.nas.up"));
        time.Advance(TimeSpan.FromSeconds(10));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("0", Format(store, "status.nas.up"));
        Assert.Equal(MetricFormatter.Missing, Format(store, "status.nas.ms"));
        Assert.Equal("<1 min", Format(store, "status.nas.since"));
        Assert.Equal("1 down: NAS", Format(store, StatusProvider.Summary));
        Assert.Equal("1", Format(store, StatusProvider.DownCount));
        Assert.Equal("No answer", ((IReadOnlyList<CheckStatus>)store.GetAttachment(StatusProvider.Checks)!)[0].Detail);

        // Down stays down without waiting for a second failure, and counts how long.
        time.Advance(TimeSpan.FromMinutes(12));
        answers.Enqueue(CheckResult.Down("No answer"));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("0", Format(store, "status.nas.up"));
        Assert.Equal("12 min", Format(store, "status.nas.since"));

        // Back: up straight away, and counting afresh.
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("1", Format(store, "status.nas.up"));
        Assert.Equal("<1 min", Format(store, "status.nas.since"));
        Assert.Equal("All up", Format(store, StatusProvider.Summary));
    }

    [Fact]
    public async Task Edited_checks_start_afresh_and_removed_ones_take_their_readings_with_them()
    {
        var time = new FakeTime(Noon);
        var asked = 0;
        IReadOnlyList<StatusCheck> checks = [Nas];
        using var provider = new StatusProvider(() => checks, (_, _) => { asked++; return Task.FromResult(CheckResult.Answered(4)); }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, asked);

        // A new address is checked straight away.
        time.Advance(TimeSpan.FromSeconds(5));
        checks = [Nas with { Address = "192.168.1.20" }];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, asked);

        // A new name has new readings.
        checks = [Nas with { Name = "Synology" }];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.False(store.TryGet("status.nas.up", out _));
        Assert.Equal("1", Format(store, "status.synology.up"));
        Assert.Equal("Synology: response time", store.Definitions.Single(d => d.Id == "status.synology.ms").Name);

        // Of two whose names give the same readings, only the first is checked.
        checks = [Nas with { Name = "Synology" }, new StatusCheck("synology", "10.0.0.2")];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single((IReadOnlyList<CheckStatus>)store.GetAttachment(StatusProvider.Checks)!);

        checks = [];
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.False(store.TryGet("status.synology.up", out _));
        Assert.Null(store.GetAttachment(StatusProvider.Checks));
        Assert.Equal(MetricFormatter.Missing, Format(store, StatusProvider.Summary));
    }

    [Fact]
    public async Task A_slow_check_holds_up_neither_the_others_nor_itself()
    {
        var time = new FakeTime(Noon);
        var slow = new TaskCompletionSource<CheckResult>();
        var asked = 0;
        IReadOnlyList<StatusCheck> checks = [Nas with { Every = 10 }, new StatusCheck("Router", "192.168.1.1")];
        using var provider = new StatusProvider(() => checks, (check, _) =>
        {
            if (check.Name != "NAS")
                return Task.FromResult(CheckResult.Answered(2));
            asked++;
            return slow.Task;
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("1", Format(store, "status.router.up"));
        Assert.Equal("Checking…", Format(store, StatusProvider.Summary));

        // Still waiting when it's due again: not asked twice at once.
        time.Advance(TimeSpan.FromSeconds(20));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, asked);

        slow.SetResult(CheckResult.Answered(40));
        // Its answer arrives in the background.
        await Task.Delay(50);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("40 ms", Format(store, "status.nas.ms"));
        Assert.Equal("All up", Format(store, StatusProvider.Summary));
    }

    [Fact]
    public void The_demo_has_the_servers_at_home_with_plex_down()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, new DateTime(2026, 10, 16, 21, 47, 0));
        Assert.Equal(["Router", "NAS", "Pi-hole", "Plex"], ((IReadOnlyList<CheckStatus>)store.GetAttachment(StatusProvider.Checks)!).Select(c => c.Name));
        Assert.Equal("1 down: Plex", Format(store, StatusProvider.Summary));
        Assert.Equal("3", Format(store, StatusProvider.UpCount));
        Assert.Equal("2 ms", Format(store, "status.router.ms"));
        Assert.Equal("1", Format(store, "status.pi-hole.up"));
        Assert.Equal("0", Format(store, "status.plex.up"));
        Assert.Equal("12 min", Format(store, "status.plex.since"));
    }

    // ---- Web addresses ------------------------------------------------------------------------------

    [Fact]
    public async Task A_page_is_up_when_it_loads_and_says_the_word()
    {
        var plex = new StatusCheck("Plex", "http://plex.local:32400/web", Expect: "plex");
        Assert.True((await Get(plex, Page(HttpStatusCode.OK, "<title>Plex</title>"))).Up);
        var missing = await Get(plex, Page(HttpStatusCode.OK, "<title>Something else</title>"));
        Assert.False(missing.Up);
        Assert.Equal("The page doesn't say \"plex\"", missing.Detail);
        // Without a word, any page that loads.
        Assert.True((await Get(plex with { Expect = null }, Page(HttpStatusCode.NoContent, ""))).Up);

        var error = await Get(plex, Page(HttpStatusCode.ServiceUnavailable, "plex"));
        Assert.False(error.Up);
        Assert.Equal("HTTP 503 Service Unavailable", error.Detail);
        Assert.Equal("HTTP 401 Unauthorized", (await Get(plex, Page(HttpStatusCode.Unauthorized, ""))).Detail);
    }

    [Fact]
    public async Task Moves_within_the_site_are_followed_and_moves_elsewhere_are_not()
    {
        var asked = new List<Uri>();
        Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> send = (request, _, _) =>
        {
            asked.Add(request.RequestUri!);
            Assert.True(request.Headers.ConnectionClose);
            return Task.FromResult(request.RequestUri!.AbsoluteUri switch
            {
                "http://plex.local:32400/" => Moved("/web/index.html"),
                "http://plex.local:32400/web/index.html" => Moved("https://PLEX.local:32443/web/index.html"),
                "http://plex.local:32400/elsewhere" => Moved("https://plex.tv/sign-in"),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Welcome to Plex") },
            });
        };

        var plex = new StatusCheck("Plex", "http://plex.local:32400/", Expect: "Plex");
        Assert.True((await StatusChecker.GetAsync(plex, send, CancellationToken.None)).Up);
        Assert.Equal(["http://plex.local:32400/", "http://plex.local:32400/web/index.html", "https://plex.local:32443/web/index.html"], asked.Select(u => u.AbsoluteUri));

        // Another site isn't contacted: the move itself counts as up, but there's no page to find the word on.
        asked.Clear();
        var elsewhere = plex with { Address = "http://plex.local:32400/elsewhere" };
        Assert.True((await StatusChecker.GetAsync(elsewhere with { Expect = null }, send, CancellationToken.None)).Up);
        var word = await StatusChecker.GetAsync(elsewhere, send, CancellationToken.None);
        Assert.False(word.Up);
        Assert.Equal("It sends you on to plex.tv, which isn't checked", word.Detail);
        Assert.All(asked, uri => Assert.Equal("plex.local", uri.Host));

        // Round in circles: given up on after a few.
        asked.Clear();
        Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> loop = (request, _, _) =>
        {
            asked.Add(request.RequestUri!);
            return Task.FromResult(Moved(request.RequestUri!.AbsolutePath == "/a" ? "/b" : "/a"));
        };
        Assert.True((await StatusChecker.GetAsync(plex with { Address = "http://plex.local/a", Expect = null }, loop, CancellationToken.None)).Up);
        Assert.Equal(StatusChecker.MostRedirects + 1, asked.Count);
    }

    [Fact]
    public async Task A_server_that_cant_be_reached_says_why()
    {
        var nas = new StatusCheck("NAS", "https://nas.local:5001");
        Assert.Equal("Address not found", (await Get(nas, Throws(new HttpRequestException(HttpRequestError.NameResolutionError)))).Detail);
        Assert.Equal("Couldn't connect", (await Get(nas, Throws(new HttpRequestException(HttpRequestError.ConnectionError)))).Detail);
        var certificate = await Get(nas, Throws(new HttpRequestException(HttpRequestError.SecureConnectionError, "SSL", new System.Security.Authentication.AuthenticationException("UntrustedRoot"))));
        Assert.Contains("Accept its own certificate", certificate.Detail);
    }

    [Fact]
    public async Task A_web_check_asks_the_address_typed_and_no_other()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? request = null;

        async Task Serve(string body)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            request = Encoding.ASCII.GetString(buffer, 0, read);
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        }

        var check = new StatusCheck("Pi-hole", $"http://127.0.0.1:{port}/admin/", Expect: "pi-hole");
        var served = Serve("<h1>Pi-hole</h1>");
        var result = await StatusChecker.CheckAsync(check, CancellationToken.None);
        await served;
        Assert.True(result.Up, result.Detail);
        Assert.InRange(result.Milliseconds!.Value, 0, StatusChecker.WebTimeout.TotalMilliseconds);
        Assert.StartsWith("GET /admin/ HTTP/1.1", request);
        Assert.Contains(WebIdentity.UserAgent, request);
    }

    // ---- Pings --------------------------------------------------------------------------------------

    [Fact]
    public async Task This_pc_answers_a_ping_and_a_made_up_name_isnt_found()
    {
        var here = await StatusChecker.CheckAsync(new StatusCheck("This PC", "127.0.0.1"), CancellationToken.None);
        Assert.True(here.Up, here.Detail);
        Assert.NotNull(here.Milliseconds);
        Assert.Null(here.Detail);

        var nowhere = await StatusChecker.CheckAsync(new StatusCheck("Nowhere", "impious-bonum-test.invalid"), CancellationToken.None);
        Assert.False(nowhere.Up);
        Assert.Equal("Address not found", nowhere.Detail);
    }

    private static Task<CheckResult> Get(StatusCheck check, Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> send) =>
        StatusChecker.GetAsync(check, send, CancellationToken.None);

    private static Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> Page(HttpStatusCode code, string body) =>
        (_, _, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    private static Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> Throws(Exception ex) =>
        (_, _, _) => Task.FromException<HttpResponseMessage>(ex);

    private static HttpResponseMessage Moved(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

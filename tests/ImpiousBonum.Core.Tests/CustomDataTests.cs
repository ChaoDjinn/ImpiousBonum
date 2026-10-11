using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class CustomDataTests
{
    private const string HomeAssistant = """
        { "entity_id": "sensor.outside", "state": "12.4", "attributes": { "unit": "°C", "humidity": 81, "on": true,
          "readings": [ { "value": 3 }, { "value": 7 } ], "odd key": "yes" } }
        """;

    private static readonly CustomSource Outside = new(
        "outside", Url: "http://homeassistant.local:8123/api/states/sensor.outside",
        Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer abc" },
        Fields:
        [
            new CustomField("temp", "state", "celsius"),
            new CustomField("humidity", "attributes.humidity", "percent"),
            new CustomField("on", "attributes.on"),
            new CustomField("second", "attributes.readings[1].value"),
            new CustomField("odd", "attributes[\"odd key\"]"),
            new CustomField("entity", "entity_id"),
            new CustomField("missing", "attributes.nope"),
        ]);

    [Fact]
    public void Fields_are_found_in_json_by_path()
    {
        var readings = Outside.Read(HomeAssistant).ToDictionary(r => r.Field.Name);
        Assert.Equal(12.4, readings["temp"].Value);
        Assert.Equal(81, readings["humidity"].Value);
        Assert.Equal(1, readings["on"].Value);
        Assert.Equal(7, readings["second"].Value);
        Assert.Equal("yes", readings["odd"].Text);
        Assert.Equal("sensor.outside", readings["entity"].Text);
        Assert.Null(readings["missing"].Value);
        Assert.Null(readings["missing"].Text);
    }

    [Fact]
    public void Plain_output_is_the_value_and_text_stays_text()
    {
        var bare = new CustomSource("ups", Command: "status.exe");
        Assert.Equal(98.5, Assert.Single(bare.Read(" 98.5\r\n")).Value);
        Assert.Equal("On battery", Assert.Single(bare.Read("On battery\n")).Text);

        var asText = bare with { Fields = [new CustomField("value", null, "text")] };
        Assert.Equal("42", Assert.Single(asText.Read("42")).Text);
    }

    [Theory]
    [InlineData("$.a.b[2]", new object[] { "a", "b", 2 })]
    [InlineData("a['x.y'].c", new object[] { "a", "x.y", "c" })]
    [InlineData("[0].name", new object[] { 0, "name" })]
    public void Paths_split_into_names_and_indexes(string path, object[] steps) =>
        Assert.Equal(steps, JsonPath.Steps(path));

    [Fact]
    public void Paths_match_names_in_any_case_and_miss_safely()
    {
        var json = JsonNode.Parse("""{ "Main": { "Temp": 5 }, "list": [1] }""")!;
        Assert.Equal(5, JsonPath.Find(json, "main.temp")!.GetValue<int>());
        Assert.Null(JsonPath.Find(json, "list[3]"));
        Assert.Null(JsonPath.Find(json, "list.name"));
        Assert.Null(JsonPath.Find(json, "main[0]"));
    }

    [Theory]
    [InlineData("bad name", "http://x", null, "name")]
    [InlineData("ok", null, null, "web address or a command")]
    [InlineData("ok", "http://x", "cmd", "web address or a command")]
    [InlineData("ok", "ftp://x", null, "http")]
    public void Unusable_sources_say_why(string name, string? url, string? command, string expected) =>
        Assert.Contains(expected, new CustomSource(name, url, command).Problem());

    [Fact]
    public void Fields_must_have_good_names_and_units()
    {
        Assert.Null(Outside.Problem());
        Assert.Contains("unit", (Outside with { Fields = [new CustomField("a", null, "parsecs")] }).Problem());
        Assert.Contains("status", (Outside with { Fields = [new CustomField("status")] }).Problem());
        Assert.Contains("same name", (Outside with { Fields = [new CustomField("a"), new CustomField("A")] }).Problem());
    }

    [Fact]
    public void Fields_and_headers_round_trip_through_their_text_form()
    {
        var text = CustomSourceText.FormatFields(Outside.Fields);
        Assert.Equal(Outside.Fields, CustomSourceText.ParseFields(text, out var problem));
        Assert.Null(problem);

        var parsed = CustomSourceText.ParseFields("# comment\nvalue = : text\nplain\ntemp = main.temp : CELSIUS", out _)!;
        Assert.Equal(new[] { new CustomField("value", null, "text"), new CustomField("plain"), new CustomField("temp", "main.temp", "celsius") }, parsed);
        Assert.Null(CustomSourceText.ParseFields("= main.temp", out problem));
        Assert.Contains("Line 1", problem);

        var headers = CustomSourceText.ParseHeaders("Authorization: Bearer a:b\nAccept: application/json", out _)!;
        Assert.Equal("Bearer a:b", headers["Authorization"]);
        Assert.Null(CustomSourceText.ParseHeaders("no colon here", out problem));
    }

    [Fact]
    public async Task The_provider_registers_reads_on_schedule_and_follows_edits()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var fetches = 0;
        var source = Outside with { Every = 30 };
        IReadOnlyList<CustomSource> sources = [source];
        using var provider = new CustomDataProvider(() => sources, (_, _) => { fetches++; return Task.FromResult(HomeAssistant); }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        Assert.True(store.TryGet("custom.outside.temp", out var temp));
        Assert.Equal(MetricUnit.Celsius, temp.Definition.Unit);
        Assert.Equal(100, store.Definitions.Single(d => d.Id == "custom.outside.humidity").Max);

        await SampleAndSettle(provider, store);
        Assert.Equal(1, fetches);
        Assert.Equal("12.4 °C", Format(store, "custom.outside.temp"));
        Assert.Equal("OK", Format(store, "custom.outside.status"));

        time.Advance(TimeSpan.FromSeconds(10));
        await SampleAndSettle(provider, store);
        Assert.Equal(1, fetches);
        time.Advance(TimeSpan.FromSeconds(25));
        await SampleAndSettle(provider, store);
        Assert.Equal(2, fetches);

        // An edit is read straight away, and fields that went away are unregistered.
        sources = [source with { Fields = [new CustomField("temp", "state", "celsius")] }];
        await SampleAndSettle(provider, store);
        Assert.Equal(3, fetches);
        Assert.False(store.TryGet("custom.outside.humidity", out _));

        sources = [];
        await SampleAndSettle(provider, store);
        Assert.False(store.TryGet("custom.outside.temp", out _));
    }

    [Fact]
    public async Task A_failed_read_clears_the_fields_and_says_why()
    {
        IReadOnlyList<CustomSource> sources = [Outside];
        var fail = false;
        using var provider = new CustomDataProvider(() => sources,
            (_, _) => fail ? Task.FromException<string>(new CustomSourceException("HTTP 401 Unauthorized")) : Task.FromResult(HomeAssistant),
            new FakeTime(DateTimeOffset.UnixEpoch));
        var store = new MetricStore();
        provider.Initialize(store);
        await SampleAndSettle(provider, store);
        Assert.Equal("12.4 °C", Format(store, "custom.outside.temp"));

        fail = true;
        sources = [Outside with { Every = 6 }];
        await SampleAndSettle(provider, store);
        Assert.Equal(MetricFormatter.Missing, Format(store, "custom.outside.temp"));
        Assert.Equal("HTTP 401 Unauthorized", Format(store, "custom.outside.status"));
    }

    [Fact]
    public async Task Commands_run_without_a_window_and_report_failures()
    {
        var json = new CustomSource("cmd", Command: "cmd.exe", Arguments: "/c echo {\"load\": 42}", Fields: [new CustomField("load", "load", "percent")]);
        var (readings, status) = await CustomDataProvider.TestAsync(json, CancellationToken.None);
        Assert.Equal("OK", status);
        Assert.Equal(42, Assert.Single(readings).Value);

        var failing = new CustomSource("cmd", Command: "cmd.exe", Arguments: "/c echo oops 1>&2 & exit 3");
        (readings, status) = await CustomDataProvider.TestAsync(failing, CancellationToken.None);
        Assert.Empty(readings);
        Assert.Equal("Exited with code 3: oops", status);

        var missing = new CustomSource("cmd", Command: "definitely-not-a-program-ib.exe");
        (_, status) = await CustomDataProvider.TestAsync(missing, CancellationToken.None);
        Assert.StartsWith("Couldn't start", status);
    }

    [Fact]
    public async Task Web_sources_send_their_headers_and_report_http_errors()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();

        async Task Serve(string status, string body)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            requests.Add(Encoding.ASCII.GetString(buffer, 0, read));
            var response = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        }

        var source = Outside with { Url = $"http://127.0.0.1:{port}/api/states/sensor.outside" };
        var served = Serve("200 OK", HomeAssistant);
        var (readings, status) = await CustomDataProvider.TestAsync(source, CancellationToken.None);
        await served;
        Assert.Equal("OK", status);
        Assert.Equal(12.4, readings.First(r => r.Field.Name == "temp").Value);
        Assert.Contains("Authorization: Bearer abc", requests[0]);

        served = Serve("401 Unauthorized", "{}");
        (_, status) = await CustomDataProvider.TestAsync(source, CancellationToken.None);
        await served;
        Assert.Equal("HTTP 401 Unauthorized", status);
    }

    /// <summary>Samples, then waits for the background reads the sample started.</summary>
    private static async Task SampleAndSettle(CustomDataProvider provider, MetricStore store)
    {
        await provider.SampleAsync(store, CancellationToken.None);
        await Task.Delay(50);
    }

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

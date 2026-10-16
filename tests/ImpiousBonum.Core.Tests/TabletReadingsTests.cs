using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.Core.Tests;

public sealed class TabletReadingsTests
{
    private const string Token = "s3cret-token_value";

    private static MetricStore Store()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("cpu.load", "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition("cpu.temp", "CPU temperature", "CPU", MetricUnit.Celsius));
        store.Register(new MetricDefinition("media.title", "Title", "Media", MetricUnit.Text));
        store.Register(new MetricDefinition("gpu.fan", "GPU fan", "GPU", MetricUnit.Rpm));
        store.Register(new MetricDefinition("net.down", "Download", "Network", MetricUnit.BytesPerSecond));
        store.Set("cpu.load", 37.5);
        store.Set("cpu.temp", 54);
        store.SetText("media.title", "Gymnopédie No. 1");
        store.Set("net.down", double.NaN);
        return store;
    }

    private static TabletServer Server() => new(0, Token) { Readings = Store() };

    [Fact]
    public void Off_by_default_the_readings_dont_exist()
    {
        using var server = new TabletServer(0, Token);
        Assert.Equal(404, server.Respond(Request($"/api/metrics?k={Token}")).Status);
        Assert.Equal(404, server.Respond(Request("/api/metrics/cpu.load", $"Authorization: Bearer {Token}")).Status);
    }

    [Theory]
    [InlineData("/api/metrics", null)]
    [InlineData("/api/metrics?k=wrong", null)]
    [InlineData("/api/metrics?k=" + Token + "x", null)]
    [InlineData("/api/metrics/cpu.load", null)]
    [InlineData("/api/metrics", "Authorization: Bearer wrong")]
    [InlineData("/api/metrics", "Authorization: Basic " + Token)]
    [InlineData("/api/metrics", "Authorization: " + Token)]
    // A wrong header isn't rescued by the right secret in the address.
    [InlineData("/api/metrics?k=" + Token, "Authorization: Bearer wrong")]
    public void Without_the_secret_the_readings_dont_exist(string target, string? header)
    {
        using var server = Server();
        var response = server.Respond(header is null ? Request(target) : Request(target, header));
        Assert.Equal(404, response.Status);
        Assert.Equal("Not Found", Encoding.UTF8.GetString(response.Body));
    }

    [Theory]
    [InlineData("/api/metrics?k=" + Token, null)]
    [InlineData("/api/metrics", "Authorization: Bearer " + Token)]
    [InlineData("/api/metrics", "authorization: bearer " + Token)]
    [InlineData("/api/metrics?k=wrong", "Authorization: Bearer " + Token)]
    public void The_secret_can_be_in_the_address_or_a_bearer_header(string target, string? header)
    {
        using var server = Server();
        var response = server.Respond(header is null ? Request(target) : Request(target, header));
        Assert.Equal(200, response.Status);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
    }

    [Fact]
    public void A_new_link_locks_out_apps_using_the_old_secret()
    {
        using var server = Server();
        server.Token = "another";
        Assert.Equal(404, server.Respond(Request($"/api/metrics?k={Token}")).Status);
        Assert.Equal(200, server.Respond(Request("/api/metrics", "Authorization: Bearer another")).Status);
    }

    [Fact]
    public void Every_reading_has_its_id_name_category_unit_value_text_and_formatted_value()
    {
        using var server = Server();
        var response = server.Respond(Request($"/api/metrics?k={Token}"));
        using var json = JsonDocument.Parse(response.Body);
        var metrics = json.RootElement.GetProperty("metrics").EnumerateArray().ToList();

        // By category, then id, as the metric picker lists them.
        Assert.Equal(["cpu.load", "cpu.temp", "gpu.fan", "media.title", "net.down"], metrics.Select(m => m.GetProperty("id").GetString()));
        Assert.All(metrics, m => Assert.Equal(["id", "name", "category", "unit", "value", "text", "formatted"], m.EnumerateObject().Select(p => p.Name)));

        var load = metrics[0];
        Assert.Equal("CPU load", load.GetProperty("name").GetString());
        Assert.Equal("CPU", load.GetProperty("category").GetString());
        Assert.Equal("percent", load.GetProperty("unit").GetString());
        Assert.Equal(37.5, load.GetProperty("value").GetDouble());
        Assert.Equal(JsonValueKind.Null, load.GetProperty("text").ValueKind);
        Assert.Equal(MetricFormatter.Format(37.5, MetricUnit.Percent), load.GetProperty("formatted").GetString());

        // Text readings have text and no value; one with no reading yet (or NaN) has neither, and shows a dash.
        var title = metrics[3];
        Assert.Equal("text", title.GetProperty("unit").GetString());
        Assert.Equal(JsonValueKind.Null, title.GetProperty("value").ValueKind);
        Assert.Equal("Gymnopédie No. 1", title.GetProperty("text").GetString());
        Assert.Equal("Gymnopédie No. 1", title.GetProperty("formatted").GetString());
        foreach (var missing in new[] { metrics[2], metrics[4] })
        {
            Assert.Equal(JsonValueKind.Null, missing.GetProperty("value").ValueKind);
            Assert.Equal(MetricFormatter.Missing, missing.GetProperty("formatted").GetString());
        }
        Assert.Equal("bytesPerSecond", metrics[4].GetProperty("unit").GetString());
    }

    [Fact]
    public void Units_and_text_come_as_they_are_not_escaped()
    {
        using var server = Server();
        var body = Encoding.UTF8.GetString(server.Respond(Request($"/api/metrics/cpu.temp?k={Token}")).Body);
        Assert.Contains(" °C\"", body);
        Assert.DoesNotContain("\\u", body);
    }

    [Fact]
    public void One_reading_by_its_id()
    {
        using var server = Server();
        var response = server.Respond(Request("/api/metrics/CPU.Load", $"Authorization: Bearer {Token}"));
        Assert.Equal(200, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("cpu.load", json.RootElement.GetProperty("id").GetString());
        Assert.Equal(37.5, json.RootElement.GetProperty("value").GetDouble());

        var missing = server.Respond(Request($"/api/metrics/cpu.lod?k={Token}"));
        Assert.Equal(404, missing.Status);
        using var error = JsonDocument.Parse(missing.Body);
        Assert.Equal("There's no reading called \"cpu.lod\".", error.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/api/metrics/")]
    [InlineData("/api/")]
    [InlineData("/api/settings")]
    [InlineData("/api/metricsx")]
    [InlineData("/api/metrics/../settings.json")]
    public void Nothing_but_the_readings_is_served(string path)
    {
        using var server = Server();
        var response = server.Respond(Request($"{path}?k={Token}"));
        Assert.Equal(404, response.Status);
        Assert.DoesNotContain("cpu.load", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void Readings_are_never_cached_and_cost_no_drawing()
    {
        using var server = Server();
        var response = server.Respond(Request($"/api/metrics?k={Token}"));
        Assert.Contains("Cache-Control: no-store", response.Head());
        Assert.DoesNotContain("ETag", response.Head());
        Assert.Equal(405, server.Respond(HttpRequestHead.Parse($"POST /api/metrics?k={Token} HTTP/1.1")).Status);
        Assert.False(server.IsWatched);
        Assert.Empty(server.Layouts);
    }

    [Fact]
    public void Turning_sharing_off_takes_them_away_straight_away()
    {
        using var server = Server();
        Assert.Equal(200, server.Respond(Request($"/api/metrics?k={Token}")).Status);
        server.Readings = null;
        Assert.Equal(404, server.Respond(Request($"/api/metrics?k={Token}")).Status);
    }

    [Fact]
    public async Task Serves_readings_over_a_real_socket_with_a_bearer_header()
    {
        var port = FreePort();
        using var server = new TabletServer(port, Token) { Readings = Store() };
        server.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/metrics/cpu.load");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(37.5, json.RootElement.GetProperty("value").GetDouble());

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{port}/api/metrics")).StatusCode);
    }

    private static HttpRequestHead Request(string target, params string[] headers) =>
        HttpRequestHead.Parse(string.Join("\r\n", [$"GET {target} HTTP/1.1", "Host: pc", .. headers]))!;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

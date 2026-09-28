using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.Core.Tests;

public sealed class TabletServerTests
{
    private const string Token = "s3cret-token_value";

    [Theory]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.3.4", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("::ffff:192.168.1.20", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void Only_local_network_addresses_are_answered(string address, bool local) =>
        Assert.Equal(local, TabletAccess.IsLocalNetwork(IPAddress.Parse(address)));

    [Fact]
    public void Tokens_are_long_url_safe_and_different_each_time()
    {
        var a = TabletAccess.NewToken();
        var b = TabletAccess.NewToken();
        Assert.Equal(22, a.Length);
        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
    }

    [Theory]
    [InlineData(Token, true)]
    [InlineData("s3cret-token_valuE", false)]
    [InlineData("s3cret", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Token_must_match_exactly(string? given, bool matches) =>
        Assert.Equal(matches, TabletAccess.TokenMatches(given, Token));

    [Fact]
    public void An_empty_expected_token_matches_nothing() =>
        Assert.False(TabletAccess.TokenMatches("", ""));

    [Fact]
    public void Parses_request_line_query_and_headers()
    {
        var head = HttpRequestHead.Parse("GET /frame?k=abc%2Bd&x=1 HTTP/1.1\r\nHost: 192.168.1.2:8787\r\nif-none-match: \"7\"")!;
        Assert.Equal("GET", head.Method);
        Assert.Equal("/frame", head.Path);
        Assert.Equal("abc+d", head.Query["k"]);
        Assert.Equal("1", head.Query["x"]);
        Assert.Equal("\"7\"", head.Headers["If-None-Match"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GET")]
    [InlineData("GET / HTTP/2")]
    [InlineData("GET http://evil/ HTTP/1.1")]
    [InlineData("GET / HTTP/1.1\r\nno colon here")]
    public void Malformed_requests_are_refused(string head) =>
        Assert.Null(HttpRequestHead.Parse(head));

    [Fact]
    public async Task Reading_stops_at_the_blank_line_and_refuses_huge_heads()
    {
        using var ok = new MemoryStream(Encoding.ASCII.GetBytes("GET /?k=a HTTP/1.1\r\nHost: x\r\n\r\nbody"));
        Assert.Equal("/", (await HttpRequestHead.ReadAsync(ok, CancellationToken.None))!.Path);

        using var huge = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nX: " + new string('a', HttpRequestHead.MaxLength) + "\r\n\r\n"));
        Assert.Null(await HttpRequestHead.ReadAsync(huge, CancellationToken.None));

        using var cut = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost"));
        Assert.Null(await HttpRequestHead.ReadAsync(cut, CancellationToken.None));
    }

    [Fact]
    public void Without_the_token_nothing_exists()
    {
        using var server = new TabletServer(0, Token) { Frame = new TabletFrame([1, 2, 3], "image/jpeg", 1) };
        Assert.Equal(404, server.Respond(Request("/")).Status);
        Assert.Equal(404, server.Respond(Request("/frame")).Status);
        Assert.Equal(404, server.Respond(Request("/frame?k=wrong")).Status);
        Assert.False(server.IsWatched);
    }

    [Fact]
    public void Serves_the_page_and_the_frame_with_the_token()
    {
        using var server = new TabletServer(0, Token) { Frame = new TabletFrame([1, 2, 3], "image/jpeg", 4) };

        var page = server.Respond(Request($"/?k={Token}"));
        Assert.Equal(200, page.Status);
        Assert.StartsWith("text/html", page.ContentType);
        Assert.Contains("Content-Security-Policy:", page.Head());
        Assert.Contains("Referrer-Policy: no-referrer", page.Head());

        var frame = server.Respond(Request($"/frame?k={Token}"));
        Assert.Equal(200, frame.Status);
        Assert.Equal("image/jpeg", frame.ContentType);
        Assert.Equal([1, 2, 3], frame.Body);
        Assert.Equal("\"4\"", frame.ETag);
        Assert.True(server.IsWatched);
    }

    [Fact]
    public void An_unchanged_frame_is_not_sent_again()
    {
        using var server = new TabletServer(0, Token) { Frame = new TabletFrame([1, 2, 3], "image/jpeg", 4) };
        Assert.Equal(304, server.Respond(Request($"/frame?k={Token}", "If-None-Match: \"4\"")).Status);
        Assert.Equal(200, server.Respond(Request($"/frame?k={Token}", "If-None-Match: \"3\"")).Status);
    }

    [Fact]
    public void Before_the_first_frame_the_tablet_is_asked_to_retry_and_counts_as_watching()
    {
        using var server = new TabletServer(0, Token);
        var response = server.Respond(Request($"/frame?k={Token}"));
        Assert.Equal(503, response.Status);
        Assert.Contains("Retry-After: 1", response.Head());
        Assert.True(server.IsWatched);
    }

    [Fact]
    public void Only_get_is_allowed_and_unknown_paths_are_not_found()
    {
        using var server = new TabletServer(0, Token);
        Assert.Equal(405, server.Respond(HttpRequestHead.Parse($"POST /?k={Token} HTTP/1.1")).Status);
        Assert.Equal(404, server.Respond(Request($"/settings.json?k={Token}")).Status);
        Assert.Equal(400, server.Respond(null).Status);
    }

    [Fact]
    public void A_new_token_locks_out_the_old_link()
    {
        using var server = new TabletServer(0, Token);
        server.Token = "another";
        Assert.Equal(404, server.Respond(Request($"/?k={Token}")).Status);
        Assert.Equal(200, server.Respond(Request("/?k=another")).Status);
    }

    [Fact]
    public async Task Serves_over_a_real_socket_and_stops_listening_when_disposed()
    {
        var port = FreePort();
        var server = new TabletServer(port, Token) { Frame = new TabletFrame([9, 8, 7], "image/jpeg", 1) };
        server.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var page = await http.GetAsync($"http://127.0.0.1:{port}/?k={Token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Impious Bonum", await page.Content.ReadAsStringAsync());

        Assert.Equal([9, 8, 7], await http.GetByteArrayAsync($"http://127.0.0.1:{port}/frame?k={Token}"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{port}/frame")).StatusCode);

        server.Dispose();
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"http://127.0.0.1:{port}/?k={Token}"));
    }

    [Fact]
    public void A_port_in_use_throws_on_start()
    {
        using var first = new TabletServer(FreePort(), Token);
        first.Start();
        using var second = new TabletServer(first.Port, Token);
        Assert.Throws<SocketException>(second.Start);
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

using System.Net;
using System.Net.Sockets;
using System.Text;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.Core.Tests;

public sealed class TabletLayoutTests
{
    private const string Token = "s3cret-token_value";

    private static readonly string[] Saved = ["Weather Station", "Streamer", "A+B & C #1 100%", "Café"];

    /// <summary>The PC's saved layouts, found as the app finds them: ignoring case, giving back the name as saved.</summary>
    private static string? Find(string name) => Saved.FirstOrDefault(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));

    private static TabletServer Server(TimeProvider? time = null) => new(0, Token, time) { FindLayout = Find };

    [Theory]
    [InlineData("Weather%20Station", "Weather Station")]
    [InlineData("Weather+Station", "Weather Station")]
    [InlineData("weather%20STATION", "Weather Station")]
    [InlineData("A%2BB%20%26%20C%20%231%20100%25", "A+B & C #1 100%")]
    [InlineData("Caf%C3%A9", "Café")]
    public void A_link_can_name_a_saved_layout(string asked, string saved)
    {
        using var server = Server();
        var page = server.Respond(Request($"/?k={Token}&layout={asked}"));
        Assert.Equal(200, page.Status);
        Assert.Equal(TabletPage.ContentSecurityPolicy, page.ContentSecurityPolicy);
        Assert.Equal([saved], server.Layouts);
    }

    [Fact]
    public void Links_carry_any_layout_name_there_and_back()
    {
        var address = IPAddress.Parse("192.168.1.20");
        Assert.Equal("http://192.168.1.20:8787/?k=abc", TabletAccess.Link(address, 8787, "abc"));
        Assert.Equal("http://192.168.1.20:8787/?k=abc&layout=Weather%20Station", TabletAccess.Link(address, 8787, "abc", "Weather Station"));
        foreach (var name in Saved)
        {
            var target = TabletAccess.Link(address, 8787, Token, name)["http://192.168.1.20:8787".Length..];
            Assert.Equal(name, HttpRequestHead.Parse($"GET {target} HTTP/1.1")!.Query["layout"]);
        }
    }

    [Fact]
    public void An_unknown_layout_gets_a_page_saying_so_rather_than_the_main_dashboard()
    {
        using var server = Server();
        var page = server.Respond(Request($"/?k={Token}&layout=Nope"));
        Assert.Equal(404, page.Status);
        Assert.StartsWith("text/html", page.ContentType);
        Assert.Equal(TabletPage.MessagePolicy, page.ContentSecurityPolicy);
        var html = Encoding.UTF8.GetString(page.Body);
        Assert.Contains("no layout called &quot;Nope&quot;", html);
        Assert.DoesNotContain("<script", html);
        Assert.Empty(server.Layouts);

        // A page already open on it (renamed or deleted since) is told it's gone, and loads again to say so.
        Assert.Equal(410, server.Respond(Request($"/frame?k={Token}&layout=Nope")).Status);
    }

    [Fact]
    public void A_name_in_a_link_cannot_add_anything_to_the_page()
    {
        using var server = Server();
        var html = Encoding.UTF8.GetString(server.Respond(Request($"/?k={Token}&layout=%3Cscript%3Ealert(1)%3C%2Fscript%3E")).Body);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("/?layout=Streamer")]
    [InlineData("/?k=wrong&layout=Streamer")]
    [InlineData("/?k=wrong&layout=Nope")]
    [InlineData("/frame?layout=Streamer")]
    [InlineData("/frame?k=wrong&layout=Streamer")]
    [InlineData("/frame?k=wrong&layout=Nope")]
    [InlineData("/?k=" + Token + "x&layout=Streamer")]
    public void Without_the_token_a_layout_link_finds_nothing(string target)
    {
        using var server = Server();
        var response = server.Respond(Request(target));
        Assert.Equal(404, response.Status);
        Assert.Equal("Not Found", Encoding.UTF8.GetString(response.Body));
        Assert.Empty(server.Layouts);
    }

    [Fact]
    public void Until_layouts_can_be_found_links_only_show_the_main_dashboard()
    {
        using var server = new TabletServer(0, Token);
        Assert.Equal(404, server.Respond(Request($"/?k={Token}&layout=Streamer")).Status);
        Assert.Equal(200, server.Respond(Request($"/?k={Token}&layout=")).Status);
        Assert.Empty(server.Layouts);
    }

    [Fact]
    public void Each_layout_has_its_own_pictures()
    {
        using var server = Server();
        server.Frame = new TabletFrame([1], "image/jpeg", 1);

        var first = server.Respond(Request($"/frame?k={Token}&layout=Streamer"));
        Assert.Equal(503, first.Status);
        Assert.True(server.IsLayoutWatched("streamer"));
        Assert.False(server.IsLayoutWatched("Weather Station"));
        Assert.False(server.IsWatched);

        server.SetFrame("Streamer", new TabletFrame([2], "image/jpeg", 2));
        Assert.Equal([2], server.Respond(Request($"/frame?k={Token}&layout=Streamer")).Body);
        Assert.Equal(304, server.Respond(Request($"/frame?k={Token}&layout=Streamer", "If-None-Match: \"2\"")).Status);
        Assert.Equal([1], server.Respond(Request($"/frame?k={Token}")).Body);

        // A layout no tablet has asked for has nowhere to put a picture.
        server.SetFrame("Weather Station", new TabletFrame([3], "image/jpeg", 3));
        Assert.Null(server.FrameOf("Weather Station"));
    }

    [Fact]
    public void A_layout_no_tablet_has_asked_for_in_a_while_is_forgotten()
    {
        var time = new FakeTime();
        using var server = Server(time);
        server.Respond(Request($"/frame?k={Token}&layout=Streamer"));
        server.SetFrame("Streamer", new TabletFrame([2], "image/jpeg", 2));

        // Not watched once the tablet stops asking, but kept for a while in case it's back.
        time.Advance(TabletServer.WatchWindow);
        Assert.False(server.IsLayoutWatched("Streamer"));
        server.ForgetIdleLayouts();
        Assert.Equal(["Streamer"], server.Layouts);

        time.Advance(TabletServer.LayoutLifetime);
        server.ForgetIdleLayouts();
        Assert.Empty(server.Layouts);
        Assert.Null(server.FrameOf("Streamer"));

        // Asked for again, it starts afresh.
        Assert.Equal(503, server.Respond(Request($"/frame?k={Token}&layout=Streamer")).Status);
        Assert.Equal(["Streamer"], server.Layouts);
    }

    [Fact]
    public void Only_so_many_layouts_show_at_once()
    {
        using var server = new TabletServer(0, Token) { FindLayout = name => name };
        for (var i = 0; i < TabletServer.MaxLayouts; i++)
            Assert.Equal(200, server.Respond(Request($"/?k={Token}&layout=L{i}")).Status);

        var page = server.Respond(Request($"/?k={Token}&layout=One%20more"));
        Assert.Equal(503, page.Status);
        Assert.Contains("Too many layouts", Encoding.UTF8.GetString(page.Body));
        Assert.Equal(503, server.Respond(Request($"/frame?k={Token}&layout=One%20more")).Status);
        Assert.Equal(TabletServer.MaxLayouts, server.Layouts.Count);

        // The main dashboard and the layouts already showing carry on.
        Assert.Equal(200, server.Respond(Request($"/?k={Token}")).Status);
        Assert.Equal(200, server.Respond(Request($"/?k={Token}&layout=L0")).Status);
    }

    [Fact]
    public void The_page_asks_for_its_own_layout_and_loads_again_once_it_has_gone()
    {
        Assert.Contains("params.get(\"layout\")", TabletPage.Html);
        Assert.Contains("fetch(\"frame\" + query", TabletPage.Html);
        Assert.Contains("fetch(\"tap\" + query", TabletPage.Html);
        Assert.Contains("response.status === 410", TabletPage.Html);
    }

    [Fact]
    public async Task Serves_a_layout_link_over_a_real_socket()
    {
        var port = FreePort();
        using var server = new TabletServer(port, Token) { FindLayout = Find };
        server.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var link = TabletAccess.Link(IPAddress.Loopback, port, Token, "A+B & C #1 100%");
        var page = await http.GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(["A+B & C #1 100%"], server.Layouts);

        var missing = await http.GetAsync(TabletAccess.Link(IPAddress.Loopback, port, Token, "Nope"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("There&#39;s no layout called &quot;Nope&quot;", await missing.Content.ReadAsStringAsync());
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

    /// <summary>A clock that only moves when told to.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private long _ticks = TimeSpan.TicksPerDay;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}

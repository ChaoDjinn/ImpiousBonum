using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.Core.Tests;

public sealed class TabletTapTests
{
    private const string Token = "s3cret-token_value";

    private static HttpRequestHead Request(string method, string target, string? origin = null) =>
        HttpRequestHead.Parse($"{method} {target} HTTP/1.1\r\nHost: 192.168.1.10:8787\r\n{(origin is null ? "" : $"Origin: {origin}\r\n")}\r\n")!;

    /// <summary>The PC's saved layouts, found as the app finds them: ignoring case, giving back the name as saved.</summary>
    private static string? Find(string name) =>
        new[] { "Weather Station", "Streamer" }.FirstOrDefault(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Taps_dont_exist_until_allowed()
    {
        var server = new TabletServer(0, Token);
        Assert.Equal(404, server.Respond(Request("POST", $"/tap?k={Token}&x=0.5&y=0.5")).Status);
    }

    [Fact]
    public void An_allowed_tap_presses_what_is_there()
    {
        var taps = new List<(string?, double, double)>();
        var server = new TabletServer(0, Token) { Tap = (layout, x, y) => { taps.Add((layout, x, y)); return x < 0.5; } };

        Assert.Equal(200, server.Respond(Request("POST", $"/tap?k={Token}&x=0.25&y=0.75")).Status);
        Assert.Equal(204, server.Respond(Request("POST", $"/tap?k={Token}&x=0.9&y=0.1", origin: "http://192.168.1.10:8787")).Status);
        Assert.Equal([(null, 0.25, 0.75), (null, 0.9, 0.1)], taps);
    }

    [Fact]
    public void A_tap_on_a_layout_of_its_own_presses_that_layout()
    {
        var taps = new List<(string?, double, double)>();
        var server = new TabletServer(0, Token) { FindLayout = Find, Tap = (layout, x, y) => { taps.Add((layout, x, y)); return true; } };

        Assert.Equal(200, server.Respond(Request("POST", $"/tap?k={Token}&layout=weather%20station&x=0.25&y=0.75")).Status);
        Assert.Equal(200, server.Respond(Request("POST", $"/tap?k={Token}&layout=&x=0.5&y=0.5")).Status);
        Assert.Equal([("Weather Station", 0.25, 0.75), (null, 0.5, 0.5)], taps);
    }

    [Theory]
    [InlineData("POST", "/tap?k=wrong&x=0.5&y=0.5", null, 404)]
    [InlineData("POST", "/tap?x=0.5&y=0.5", null, 404)]
    [InlineData("GET", "/tap?k=" + Token + "&x=0.5&y=0.5", null, 404)]
    [InlineData("POST", "/tap?k=" + Token + "&x=1.5&y=0.5", null, 400)]
    [InlineData("POST", "/tap?k=" + Token + "&x=abc&y=0.5", null, 400)]
    [InlineData("POST", "/tap?k=" + Token + "&y=0.5", null, 400)]
    [InlineData("POST", "/tap?k=" + Token + "&x=0.5&y=0.5", "https://evil.example", 403)]
    [InlineData("POST", "/tap?k=" + Token + "&x=0.5&y=0.5", "null", 403)]
    [InlineData("POST", "/frame?k=" + Token, null, 405)]
    [InlineData("POST", "/tap?k=wrong&layout=Streamer&x=0.5&y=0.5", null, 404)]
    [InlineData("POST", "/tap?layout=Streamer&x=0.5&y=0.5", null, 404)]
    [InlineData("POST", "/tap?k=" + Token + "&layout=Gone&x=0.5&y=0.5", null, 410)]
    [InlineData("POST", "/tap?k=" + Token + "&layout=Streamer&x=0.5&y=0.5", "https://evil.example", 403)]
    public void Other_taps_are_refused(string method, string target, string? origin, int status)
    {
        var pressed = false;
        var server = new TabletServer(0, Token) { FindLayout = Find, Tap = (_, _, _) => pressed = true };
        Assert.Equal(status, server.Respond(Request(method, target, origin)).Status);
        Assert.False(pressed);
    }

    [Fact]
    public void The_page_sends_taps_and_its_policy_allows_them()
    {
        Assert.Contains("method: \"POST\"", TabletPage.Html);
        Assert.Contains("connect-src 'self'", TabletPage.ContentSecurityPolicy);
    }
}

using System.Net;
using System.Text;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.Core.Tests;

/// <summary>Links naming a tablet screen (<c>&amp;screen=</c>), which show whichever layout that screen has.</summary>
public sealed class TabletScreenLinkTests
{
    private const string Token = "s3cret-token_value";

    /// <summary>The PC's tablet screens and the layouts they show, found as the app finds them: ignoring case.</summary>
    private readonly Dictionary<string, string> _screens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Galaxy Tab"] = "Galaxy Tab",
        ["Kitchen Fire"] = "Weather Station",
    };

    private TabletServer Server() => new(0, Token)
    {
        FindLayout = name => name == "Weather Station" ? name : null,
        FindScreen = name => _screens.GetValueOrDefault(name),
    };

    [Fact]
    public void A_link_can_name_a_tablet_screen_and_shows_the_layout_it_has()
    {
        using var server = Server();
        var page = server.Respond(Request($"/?k={Token}&screen=galaxy%20tab"));
        Assert.Equal(200, page.Status);
        Assert.Equal(TabletPage.ContentSecurityPolicy, page.ContentSecurityPolicy);
        Assert.Equal(["Galaxy Tab"], server.Layouts);

        // Its pictures are its layout's: a link naming the layout itself gets the same ones.
        server.SetFrame("Galaxy Tab", new TabletFrame([7], "image/jpeg", 7));
        Assert.Equal([7], server.Respond(Request($"/frame?k={Token}&screen=Galaxy%20Tab")).Body);
        server.Respond(Request($"/frame?k={Token}&screen=Kitchen%20Fire"));
        server.SetFrame("Weather Station", new TabletFrame([8], "image/jpeg", 8));
        Assert.Equal([8], server.Respond(Request($"/frame?k={Token}&layout=Weather%20Station")).Body);
        Assert.Equal(["Galaxy Tab", "Weather Station"], server.Layouts.Order());
    }

    [Fact]
    public void A_screen_given_another_layout_carries_on_with_that_one()
    {
        using var server = Server();
        server.Respond(Request($"/frame?k={Token}&screen=Galaxy%20Tab"));
        server.SetFrame("Galaxy Tab", new TabletFrame([1], "image/jpeg", 1));

        _screens["Galaxy Tab"] = "Weather Station";
        // The page needn't load again: its next frames are the other layout's, once there is one.
        Assert.Equal(503, server.Respond(Request($"/frame?k={Token}&screen=Galaxy%20Tab")).Status);
        Assert.True(server.IsLayoutWatched("Weather Station"));
        server.SetFrame("Weather Station", new TabletFrame([2], "image/jpeg", 2));
        Assert.Equal([2], server.Respond(Request($"/frame?k={Token}&screen=Galaxy%20Tab")).Body);
    }

    [Fact]
    public void A_screen_link_wins_over_a_layout_in_the_same_link()
    {
        using var server = Server();
        Assert.Equal(200, server.Respond(Request($"/?k={Token}&screen=Galaxy%20Tab&layout=Weather%20Station")).Status);
        Assert.Equal(["Galaxy Tab"], server.Layouts);
    }

    [Fact]
    public void An_unknown_screen_gets_a_page_saying_so()
    {
        using var server = Server();
        var page = server.Respond(Request($"/?k={Token}&screen=%3Cb%3ENope"));
        Assert.Equal(404, page.Status);
        Assert.Equal(TabletPage.MessagePolicy, page.ContentSecurityPolicy);
        var html = Encoding.UTF8.GetString(page.Body);
        Assert.Contains("no tablet screen called &quot;&lt;b&gt;Nope&quot;", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<script", html);
        Assert.Empty(server.Layouts);

        // A page already open on one that's been removed is told it's gone, and loads again to say so.
        Assert.Equal(410, server.Respond(Request($"/frame?k={Token}&screen=Nope")).Status);
    }

    [Theory]
    [InlineData("/?screen=Galaxy%20Tab")]
    [InlineData("/?k=wrong&screen=Galaxy%20Tab")]
    [InlineData("/frame?k=wrong&screen=Galaxy%20Tab")]
    [InlineData("/frame?k=wrong&screen=Nope")]
    public void Without_the_token_a_screen_link_finds_nothing(string target)
    {
        using var server = Server();
        var response = server.Respond(Request(target));
        Assert.Equal(404, response.Status);
        Assert.Equal("Not Found", Encoding.UTF8.GetString(response.Body));
        Assert.Empty(server.Layouts);
    }

    [Fact]
    public void Until_screens_can_be_found_a_screen_link_finds_none()
    {
        using var server = new TabletServer(0, Token) { FindLayout = name => name };
        Assert.Equal(404, server.Respond(Request($"/?k={Token}&screen=Galaxy%20Tab")).Status);
        Assert.Equal(200, server.Respond(Request($"/?k={Token}&screen=")).Status);
        Assert.Empty(server.Layouts);
    }

    [Fact]
    public void Taps_on_a_screen_press_its_layouts_buttons()
    {
        var taps = new List<string?>();
        using var server = Server();
        server.Tap = (layout, _, _) =>
        {
            taps.Add(layout);
            return true;
        };
        Assert.Equal(200, server.Respond(Request($"/tap?k={Token}&screen=Kitchen%20Fire&x=0.5&y=0.5", "POST")).Status);
        Assert.Equal(410, server.Respond(Request($"/tap?k={Token}&screen=Nope&x=0.5&y=0.5", "POST")).Status);
        Assert.Equal(["Weather Station"], taps);
    }

    [Fact]
    public void Links_carry_any_screen_name_there_and_back()
    {
        var address = IPAddress.Parse("192.168.1.20");
        Assert.Equal("http://192.168.1.20:8787/?k=abc&screen=Galaxy%20Tab", TabletAccess.Link(address, 8787, "abc", screen: "Galaxy Tab"));
        // A screen's link is the screen's, whatever layout's named with it.
        Assert.Equal("http://192.168.1.20:8787/?k=abc&screen=Galaxy%20Tab", TabletAccess.Link(address, 8787, "abc", "Weather Station", "Galaxy Tab"));
        foreach (var name in new[] { "Galaxy Tab", "A+B & C #1 100%", "Café", "Kid's tablet" })
        {
            var target = TabletAccess.Link(address, 8787, Token, screen: name)["http://192.168.1.20:8787".Length..];
            Assert.Equal(name, HttpRequestHead.Parse($"GET {target} HTTP/1.1")!.Query["screen"]);
        }
    }

    [Fact]
    public void The_page_asks_for_its_screens_pictures_and_sends_its_taps()
    {
        Assert.Contains("params.get(\"screen\")", TabletPage.Html);
        Assert.Contains("\"&screen=\" + encodeURIComponent(screen)", TabletPage.Html);
    }

    private static HttpRequestHead Request(string target, string method = "GET") =>
        HttpRequestHead.Parse($"{method} {target} HTTP/1.1\r\nHost: pc")!;
}

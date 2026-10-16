using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.App.Tests;

public sealed class TabletButtonsTests
{
    private static LayoutDocument Layout() => new()
    {
        Width = 800,
        Height = 200,
        Widgets =
        [
            new JsonObject
            {
                ["type"] = "buttons", ["x"] = 0, ["y"] = 0, ["width"] = 400, ["height"] = 200,
                ["buttons"] = new JsonArray(new JsonObject { ["label"] = "Mute", ["icon"] = "mute", ["action"] = "mute" }),
            },
            new JsonObject { ["type"] = "timer", ["x"] = 400, ["y"] = 0, ["width"] = 400, ["height"] = 200 },
        ],
    };

    [Fact]
    public void A_tap_presses_a_button_only_when_allowed()
    {
        Sta.Run(() =>
        {
            using var tablet = new TabletView();
            var actions = new List<WidgetAction>();
            tablet.ActionRequested += (_, action) => actions.Add(action);
            tablet.Build(Layout(), new ThemeSettings());
            tablet.Draw(new MetricStore(), DateTime.Now);

            Assert.False(tablet.PressAt(0.25, 0.5));
            Assert.Empty(actions);

            tablet.AllowTaps = true;
            Assert.True(tablet.PressAt(0.25, 0.5));
            Assert.Equal(WidgetAction.Mute, Assert.Single(actions).Kind);

            // The timer can be tapped on the PC, but it isn't a button: tablets can't.
            Assert.False(tablet.PressAt(0.75, 0.4));
            Assert.Single(actions);
        });
    }

    [Fact]
    public void A_tap_presses_the_button_on_the_layout_that_tablet_shows()
    {
        Sta.Run(() =>
        {
            const string token = "s3cret-token_value";
            // The same place on another layout has a different button.
            var media = Layout();
            media.Widgets[0]["buttons"] = new JsonArray(new JsonObject { ["label"] = "Play", ["action"] = "play-pause" });
            using var tablet = new TabletView
            {
                AllowTaps = true,
                FindLayout = name => string.Equals(name, "Media", StringComparison.OrdinalIgnoreCase) ? "Media" : null,
                LoadLayout = _ => (media, new ThemeSettings()),
            };
            var actions = new List<string>();
            tablet.ActionRequested += (_, action) => actions.Add(action.Kind);
            using var server = new TabletServer(0, token);
            tablet.Serve(server);
            tablet.Build(Layout(), new ThemeSettings());
            var store = new MetricStore();

            server.Respond(Get($"/frame?k={token}"));
            server.Respond(Get($"/frame?k={token}&layout=media"));
            tablet.Refresh(store, DateTime.Now);

            Assert.Equal(200, server.Respond(Tap($"/tap?k={token}&layout=media&x=0.25&y=0.5")).Status);
            Assert.Equal(200, server.Respond(Tap($"/tap?k={token}&x=0.25&y=0.5")).Status);
            Assert.Equal([WidgetAction.PlayPause, WidgetAction.Mute], actions);

            // A layout that isn't drawn for a tablet has nothing to press, and one that isn't there isn't asked.
            Assert.False(tablet.PressAt("Streamer", 0.25, 0.5));
            Assert.Equal(410, server.Respond(Tap($"/tap?k={token}&layout=Streamer&x=0.25&y=0.5")).Status);
            Assert.Equal(2, actions.Count);
        });

        static HttpRequestHead Get(string target) => HttpRequestHead.Parse($"GET {target} HTTP/1.1\r\nHost: pc")!;

        static HttpRequestHead Tap(string target) => HttpRequestHead.Parse($"POST {target} HTTP/1.1\r\nHost: pc")!;
    }
}

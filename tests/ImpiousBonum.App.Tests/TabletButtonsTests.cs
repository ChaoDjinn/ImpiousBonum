using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

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
}

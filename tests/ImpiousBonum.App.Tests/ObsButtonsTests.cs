using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Obs;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class ObsButtonsTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    [Theory]
    [InlineData(WidgetAction.ObsRecord, "", "ToggleRecord", null)]
    [InlineData(WidgetAction.ObsStream, "", "ToggleStream", null)]
    [InlineData(WidgetAction.ObsReplay, "", "ToggleReplayBuffer", null)]
    [InlineData(WidgetAction.ObsSaveReplay, "", "SaveReplayBuffer", null)]
    [InlineData(WidgetAction.ObsScene, "BRB", "SetCurrentProgramScene", "{\"sceneName\":\"BRB\"}")]
    [InlineData(WidgetAction.ObsMute, "Mic/Aux", "ToggleInputMute", "{\"inputName\":\"Mic/Aux\"}")]
    public void Each_OBS_button_asks_OBS_for_its_own_request(string kind, string target, string type, string? data)
    {
        var call = ActionRunner.ObsCallFor(new WidgetAction(kind, target));

        Assert.NotNull(call);
        Assert.Equal(type, call.Type);
        Assert.Equal(data, call.Data?.ToJsonString());
    }

    [Fact]
    public void A_scene_or_mute_button_without_a_target_says_what_it_needs()
    {
        var runner = new ActionRunner(_ => null) { Obs = new ObsConnection(() => null) };

        Assert.Null(ActionRunner.ObsCallFor(new WidgetAction(WidgetAction.ObsScene)));
        Assert.Contains("no OBS scene", runner.Run(new WidgetAction(WidgetAction.ObsScene)));
        Assert.Contains("no OBS audio source", runner.Run(new WidgetAction(WidgetAction.ObsMute)));
    }

    [Fact]
    public void A_tap_while_OBS_is_off_in_settings_says_so_and_without_a_connection_does_nothing()
    {
        var off = new ActionRunner(_ => null) { Obs = new ObsConnection(() => null) };
        Assert.Equal("OBS buttons need OBS turned on, on the OBS page in settings.", off.Run(new WidgetAction(WidgetAction.ObsRecord)));

        // The demo has no connection: nothing to say, as nothing would happen anyway.
        Assert.Null(new ActionRunner(_ => null).Run(new WidgetAction(WidgetAction.ObsRecord)));
    }

    [Fact]
    public void Record_stream_replay_and_scene_buttons_take_the_on_colour_while_they_re_on()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "buttons",
                ["buttons"] = new JsonArray(
                    new JsonObject { ["label"] = "Rec", ["icon"] = "record", ["action"] = WidgetAction.ObsRecord },
                    new JsonObject { ["label"] = "Live", ["icon"] = "broadcast", ["action"] = WidgetAction.ObsStream },
                    new JsonObject { ["label"] = "Game", ["icon"] = "layout", ["action"] = WidgetAction.ObsScene, ["target"] = "game" },
                    new JsonObject { ["label"] = "Clip", ["icon"] = "replay", ["action"] = WidgetAction.ObsSaveReplay }),
            }, Plain);
            var icons = Descendants<Path>(widget).ToList();
            var labels = Descendants<TextBlock>(widget).ToList();
            var store = new MetricStore();
            using var provider = new ObsProvider(null);
            provider.Publish(store, ObsState.Connected,
            [
                new(true, new JsonObject { ["outputActive"] = true, ["outputTimecode"] = "00:00:05.000" }),
                new(true, new JsonObject { ["outputActive"] = false }),
                new(true, new JsonObject { ["outputActive"] = false }),
                new(true, new JsonObject { ["currentProgramSceneName"] = "Game" }),
                new(true, new JsonObject { ["activeFps"] = 60.0 }),
            ]);

            widget.Refresh(store, DateTime.Now);

            Assert.Equal([Plain.Critical, Plain.Accent, Plain.Critical, Plain.Accent], icons.Select(i => i.Stroke));
            Assert.Equal([Plain.Critical, Plain.Foreground, Plain.Critical, Plain.Foreground], labels.Select(l => l.Foreground));

            // OBS closes: nothing's on.
            provider.Publish(store, ObsState.NotRunning, null);
            widget.Refresh(store, DateTime.Now);
            Assert.All(icons, i => Assert.Same(Plain.Accent, i.Stroke));
        });
    }

    [Fact]
    public void The_buttons_say_what_they_do()
    {
        Assert.Equal("Start or stop recording in OBS", ButtonsWidget.Describe(new WidgetAction(WidgetAction.ObsRecord)));
        Assert.Equal("Shows the \"BRB\" scene in OBS", ButtonsWidget.Describe(new WidgetAction(WidgetAction.ObsScene, "BRB")));
        Assert.Equal("Mutes no OBS source yet: set a target", ButtonsWidget.Describe(new WidgetAction(WidgetAction.ObsMute)));
        Assert.All(["record", "broadcast", "replay"], name => Assert.NotNull(Icons.Get(name)));
    }

    [Theory]
    [InlineData(false, null, null, null)]
    [InlineData(true, null, null, "ws://localhost:4455/")]
    [InlineData(true, "192.168.1.20", "", "ws://192.168.1.20:4455/")]
    [InlineData(true, "http://nope", "secret", null)]
    public void Settings_say_where_to_connect_and_with_what_password(bool on, string? address, string? password, string? expected)
    {
        var settings = new AppSettings { Obs = on, ObsAddress = address, ObsPassword = password };

        var target = settings.ObsTarget();

        Assert.Equal(expected, target?.Address.ToString());
        Assert.Null(target?.Password);
        if (!on)
            Assert.Equal("Off", ObsPage.Status(null, settings));
    }

    [Fact]
    public void The_OBS_page_shows_how_connecting_is_going()
    {
        var settings = new AppSettings { Obs = true, ObsPassword = "secret" };
        Assert.Equal("secret", settings.ObsTarget()?.Password);
        Assert.Equal("Connecting to OBS", ObsPage.Status(new MetricStore(), settings));

        var store = new MetricStore();
        using var provider = new ObsProvider(null);
        provider.Publish(store, ObsState.WrongPassword, null);
        Assert.StartsWith("OBS turned the password away", ObsPage.Status(store, settings));
        Assert.Equal(SettingsPage.Obs, PageInfo.Of(SettingsPage.Obs).Page);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}

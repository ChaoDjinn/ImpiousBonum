using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Obs;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class ObsWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    /// <summary>A store with OBS's readings as the provider writes them: recording or streaming, and the scene.</summary>
    private static MetricStore Store(ObsState state, bool recording = false, bool streaming = false, string scene = "Gaming")
    {
        var store = new MetricStore();
        using var provider = new ObsProvider(null);
        provider.Publish(store, state, state != ObsState.Connected ? null :
        [
            new(true, new JsonObject { ["outputActive"] = recording, ["outputTimecode"] = "00:12:05.000" }),
            new(true, new JsonObject { ["outputActive"] = streaming, ["outputTimecode"] = "01:30:00.000" }),
            new(true, new JsonObject { ["outputActive"] = false }),
            new(true, new JsonObject { ["currentProgramSceneName"] = scene }),
            new(true, new JsonObject { ["activeFps"] = 60.0 }),
        ]);
        return store;
    }

    private static WidgetSettings Settings(JsonObject json)
    {
        json["type"] = "obs";
        return new WidgetSettings(json, WidgetFactory.Descriptors.Single(d => d.Type == "obs").Settings);
    }

    [Fact]
    public void The_status_line_says_what_OBS_is_doing_or_why_it_can_t_say()
    {
        Assert.Equal((ObsWidget.OffText, false), ObsWidget.Status(new MetricStore()));
        Assert.Equal(("OBS isn't running", false), ObsWidget.Status(Store(ObsState.NotRunning)));
        Assert.Equal(("Ready", false), ObsWidget.Status(Store(ObsState.Connected)));
        Assert.Equal(("● REC 12:05", true), ObsWidget.Status(Store(ObsState.Connected, recording: true)));
        Assert.Equal(("● LIVE 1:30:00 · REC 12:05", true), ObsWidget.Status(Store(ObsState.Connected, recording: true, streaming: true)));
    }

    [Fact]
    public void Its_buttons_are_the_ones_ticked_then_a_button_for_each_scene()
    {
        var buttons = ObsWidget.Buttons(Settings(new JsonObject { ["replay"] = true, ["stream"] = false, ["scenes"] = " Gaming,BRB , gaming,, Just chatting" }));

        Assert.Equal(
            [
                (WidgetAction.ObsRecord, "", "Record"), (WidgetAction.ObsReplay, "", "Replay"), (WidgetAction.ObsSaveReplay, "", "Save replay"),
                (WidgetAction.ObsScene, "Gaming", "Gaming"), (WidgetAction.ObsScene, "BRB", "BRB"), (WidgetAction.ObsScene, "Just chatting", "Just chatting"),
            ],
            buttons.Select(b => ((string)b["action"]!, (string)b["target"]!, (string)b["label"]!)));

        var plain = ObsWidget.Buttons(Settings(new JsonObject { ["labels"] = false }));
        Assert.All(plain, b => Assert.Equal("", (string)b["label"]!));
        Assert.Equal(3, plain.Count);
    }

    [Fact]
    public void While_recording_the_status_line_and_its_buttons_take_the_on_colour()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "obs", ["scenes"] = "Gaming, BRB", ["shrinkToFit"] = false }, Plain);
            widget.Refresh(Store(ObsState.Connected, recording: true), DateTime.Now);

            var texts = Descendants<TextBlock>(widget).ToList();
            var status = texts.Single(t => t.Text.StartsWith('●'));
            Assert.Equal("● REC 12:05", status.Text);
            Assert.Same(Plain.Critical, status.Foreground);
            Assert.Contains(texts, t => t.Text == "Gaming" && ReferenceEquals(t.Foreground, Plain.Secondary));
            // Record and the Gaming scene are on; Stream, Save replay and BRB aren't.
            Assert.Equal([Plain.Critical, Plain.Accent, Plain.Accent, Plain.Critical, Plain.Accent],
                Descendants<System.Windows.Shapes.Path>(widget).Select(p => p.Stroke));

            widget.Refresh(Store(ObsState.NotRunning), DateTime.Now);
            Assert.Equal("OBS isn't running", status.Text);
            Assert.Same(Plain.Secondary, status.Foreground);
        });
    }

    [Fact]
    public void Its_buttons_ask_for_OBS_s_actions_and_can_be_pressed_from_a_tablet()
    {
        Sta.Run(() =>
        {
            var layout = new LayoutDocument
            {
                Width = 520,
                Height = 150,
                Widgets = [new JsonObject { ["type"] = "obs", ["x"] = 0, ["y"] = 0, ["width"] = 520, ["height"] = 150, ["status"] = false, ["stream"] = false, ["saveReplay"] = false }],
            };
            using var tablet = new TabletView { AllowTaps = true };
            var actions = new List<WidgetAction>();
            tablet.ActionRequested += (_, action) => actions.Add(action);
            tablet.Build(layout, new ThemeSettings());
            tablet.Draw(Store(ObsState.Connected), DateTime.Now);

            Assert.True(tablet.PressAt(0.5, 0.5));
            Assert.Equal(WidgetAction.ObsRecord, Assert.Single(actions).Kind);
        });
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

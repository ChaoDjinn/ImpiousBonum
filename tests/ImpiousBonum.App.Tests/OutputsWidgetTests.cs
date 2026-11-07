using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class OutputsWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    private static readonly SoundOutput Headphones = new("{a}", "Headphones", "Arctis Nova 7X", OutputKind.Headphones, Default: true);
    private static readonly SoundOutput Speakers = new("{b}", "Speakers", "Realtek(R) Audio", OutputKind.Speakers, Default: false);
    private static readonly SoundOutput SteamSpeakers = new("{c}", "Speakers", "Steam Streaming Speakers", OutputKind.Speakers, Default: false);
    private static readonly SoundOutput Tv = new("{d}", "Beyond TV", "AMD High Definition Audio Device", OutputKind.Display, Default: false);

    private static MetricStore Store(params SoundOutput[] outputs)
    {
        var store = new MetricStore();
        using var provider = new SoundProvider(_ => [], () => null);
        provider.PublishOutputs(store, outputs);
        return store;
    }

    [Fact]
    public void Each_output_is_a_button_by_its_kind_named_by_its_hardware_when_names_clash()
    {
        var buttons = OutputsWidget.Buttons([Headphones, Speakers, SteamSpeakers, Tv], labels: true);

        Assert.Equal(["Headphones", "Realtek(R) Audio", "Steam Streaming Speakers", "Beyond TV"], buttons.Select(b => (string)b["label"]!));
        Assert.Equal(["headphones", "volume-up", "volume-up", "tv"], buttons.Select(b => (string)b["icon"]!));
        Assert.All(buttons, b => Assert.Equal(WidgetAction.SoundOutput, (string)b["action"]!));
        Assert.Equal(["{a}", "{b}", "{c}", "{d}"], buttons.Select(b => (string)b["target"]!));
        // Chosen apart, the speakers are just Speakers.
        Assert.Equal(["Speakers", "Beyond TV"], OutputsWidget.Buttons([Speakers, Tv], labels: true).Select(b => (string)b["label"]!));
    }

    [Fact]
    public void All_the_outputs_or_the_ones_chosen_that_are_on_in_the_order_chosen()
    {
        IReadOnlyList<SoundOutput> outputs = [Headphones, Speakers, Tv];

        Assert.Equal(outputs, OutputsWidget.Pick(outputs, PickList.Parse(PickList.AllValue, true)));
        Assert.Equal([Tv, Headphones], OutputsWidget.Pick(outputs,
            PickList.Parse("Beyond TV (AMD High Definition Audio Device), Bluetooth (Gone), headphones (arctis nova 7x)", true)));
    }

    [Fact]
    public void The_output_playing_is_lit_and_a_tap_on_another_switches_to_it()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "outputs", ["shrinkToFit"] = false }, Plain);
            var store = new MetricStore();
            widget.Refresh(store, DateTime.Now);
            // Not read yet: nothing to show, but the outputs are asked for.
            Assert.Empty(Descendants<TapTarget>(widget));
            Assert.NotNull(store.GetAttachment("sound.outputs.wanted"));

            widget.Refresh(Store(Headphones, Speakers, Tv), DateTime.Now);
            var icons = Descendants<System.Windows.Shapes.Path>(widget).ToList();
            Assert.Equal([Plain.Accent, Plain.Secondary, Plain.Secondary], icons.Select(i => i.Stroke));

            var actions = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => actions.Add(e.Action)));
            Descendants<TapTarget>(widget).Last().Tap();
            Assert.Equal((WidgetAction.SoundOutput, "{d}"), (actions.Single().Kind, actions.Single().Target));

            // Windows has switched: the TV lights up instead.
            widget.Refresh(Store(Headphones with { Default = false }, Speakers, Tv with { Default = true }), DateTime.Now);
            Assert.Equal([Plain.Secondary, Plain.Secondary, Plain.Accent], Descendants<System.Windows.Shapes.Path>(widget).Select(i => i.Stroke));
            Assert.True(widget.PressableFromTablet);
        });
    }

    [Fact]
    public void A_button_of_your_own_lights_while_its_output_is_playing()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "buttons",
                ["buttons"] = new JsonArray(
                    new JsonObject { ["label"] = "Phones", ["icon"] = "headphones", ["action"] = WidgetAction.SoundOutput, ["target"] = "Headphones" },
                    new JsonObject { ["label"] = "Next", ["icon"] = "volume-up", ["action"] = WidgetAction.SoundOutput, ["target"] = "" }),
            }, Plain);
            var store = Store(Headphones, Speakers);

            widget.Refresh(store, DateTime.Now);

            Assert.Equal([Plain.Critical, Plain.Accent], Descendants<System.Windows.Shapes.Path>(widget).Select(i => i.Stroke));
            Assert.Equal("Plays sound through the next output", ButtonsWidget.Describe(new WidgetAction(WidgetAction.SoundOutput)));
            Assert.Equal("Plays sound through Headphones", ButtonsWidget.Describe(new WidgetAction(WidgetAction.SoundOutput, "Headphones")));
        });
    }

    [Fact]
    public void Switching_happens_in_the_background_and_a_problem_goes_to_the_log()
    {
        var switched = new List<string>();
        var logged = new List<string>();
        var runner = new ActionRunner(_ => null)
        {
            SwitchOutput = target =>
            {
                lock (switched)
                    switched.Add(target);
                return target == "Bluetooth" ? "No sound output is called \"Bluetooth\"." : null;
            },
            Failed = message =>
            {
                lock (logged)
                    logged.Add(message);
            },
        };

        Assert.Null(runner.Run(new WidgetAction(WidgetAction.SoundOutput, "Headphones")));
        Assert.Null(runner.Run(new WidgetAction(WidgetAction.SoundOutput, "Bluetooth")));
        for (var i = 0; i < 100 && (switched.Count < 2 || logged.Count < 1); i++)
            Thread.Sleep(20);

        Assert.Equal(["Bluetooth", "Headphones"], switched.Order());
        Assert.Equal(["No sound output is called \"Bluetooth\"."], logged);
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

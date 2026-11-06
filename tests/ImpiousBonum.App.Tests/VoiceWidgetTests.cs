using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class VoiceWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    private static Widget Create(JsonObject? settings = null)
    {
        settings ??= [];
        settings["type"] = "voice";
        settings["shrinkToFit"] = false;
        return WidgetFactory.Create(settings, Plain);
    }

    private static MetricStore Store(VoiceState? state)
    {
        var store = new MetricStore();
        store.SetAttachment(VoiceChatProvider.Live, state);
        return store;
    }

    private static IEnumerable<T> All<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T found)
            yield return found;
        if (parent is Panel panel)
        {
            foreach (var child in panel.Children.OfType<DependencyObject>().SelectMany(All<T>))
                yield return child;
        }
    }

    private static List<Brush> Lights(Widget widget) => [.. All<Ellipse>(widget).Select(e => e.Fill)];

    private static List<string> Labels(Widget widget) => [.. All<TextBlock>(widget).Select(t => t.Text)];

    [Fact]
    public void Someone_talking_lights_the_first_and_you_the_second()
    {
        Sta.Run(() =>
        {
            var widget = Create();
            Assert.Equal(["Talking", "You"], Labels(widget));

            widget.Refresh(Store(new VoiceState(true, true, false, 0.3, 0)), DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
            Assert.Equal([Plain.Accent, Plain.Track], Lights(widget));
            Assert.Equal([Plain.Foreground, Plain.Secondary], All<TextBlock>(widget).Select(t => t.Foreground));

            widget.Refresh(Store(new VoiceState(true, false, true, 0, 0.2)), DateTime.Now);
            Assert.Equal([Plain.Track, Plain.Critical], Lights(widget));
        });
    }

    [Fact]
    public void It_hides_outside_a_call_unless_asked_and_while_Discord_is_closed()
    {
        Sta.Run(() =>
        {
            var quiet = new VoiceState(false, false, false, 0, 0);
            var widget = Create();
            widget.Refresh(Store(quiet), DateTime.Now);
            Assert.Equal(Visibility.Collapsed, widget.Visibility);

            var shown = Create(new JsonObject { ["outsideCall"] = true });
            shown.Refresh(Store(quiet), DateTime.Now);
            Assert.Equal(Visibility.Visible, shown.Visibility);
            Assert.Equal([Plain.Track, Plain.Track], Lights(shown));

            shown.Refresh(Store(null), DateTime.Now);
            Assert.Equal(Visibility.Collapsed, shown.Visibility);
        });
    }

    [Fact]
    public void Lights_and_labels_can_be_left_out_or_renamed_and_recoloured()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["talking"] = false, ["youLabel"] = "MIC", ["youColor"] = "#FF00FF00" });
            widget.Refresh(Store(new VoiceState(true, true, true, 0.3, 0.2)), DateTime.Now);

            Assert.Equal(["MIC"], Labels(widget));
            Assert.Equal(Colors.Lime, Assert.IsType<SolidColorBrush>(Assert.Single(Lights(widget))).Color);

            var dots = Create(new JsonObject { ["talkingLabel"] = "", ["youLabel"] = "" });
            Assert.Empty(Labels(dots));
            Assert.Equal(2, Lights(dots).Count);
        });
    }
}

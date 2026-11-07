using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The playback devices as buttons, the one Windows plays to lit: a tap plays sound through another, as the volume
/// flyout's output picker does. The buttons are a <see cref="ButtonsWidget"/> inside, built again when a device comes or
/// goes, so they look like any buttons and a tablet can press them.
/// </summary>
public sealed class OutputsWidget : Widget
{
    /// <summary>The names the editor offers for <c>outputs</c>: the outputs seen since the app started.</summary>
    public const string OutputNames = "soundOutputs";

    public const string Empty = "No sound outputs";

    public static WidgetDescriptor Descriptor { get; } = new(
        "outputs", "Sound output",
        "Your speakers, headphones and TV as buttons, the one sound plays through lit: tap another to switch, for games, music and calls alike.",
        420, 110,
        [
            Setting.Picks("outputs", "Outputs", PickList.AllValue, source: OutputNames, all: "All outputs",
                none: "No sound outputs have been seen since Impious Bonum started.",
                help: "All the outputs that are on, or just the ones you switch between, in the order they're listed. Ones that are off or unplugged are left out until they're back."),
            Setting.Toggle("labels", "Labels", true, group: Setting.Content,
                help: "Each output's name under its icon. Outputs with the same name, such as two Speakers, go by their hardware's instead."),
            Setting.Number("columns", "Columns", 0, 0, 20, group: Setting.Appearance, help: "0 puts every output in one row."),
            Setting.Number("fontSize", "Label size", 18, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("iconSize", "Icon size", 36, 0, 400, group: Setting.Appearance),
            Setting.Number("spacing", "Spacing", 10, 0, 200, group: Setting.Appearance),
            Setting.Choice("style", "Style", "tile", ["tile", "plain"], group: Setting.Appearance, help: "tile gives each output a background."),
            Setting.Number("cornerRadius", "Corner radius", 10, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Icon colour", "secondary", help: "The outputs sound isn't playing through."),
            Setting.Color("activeColor", "Playing colour", "accent", help: "The output sound is playing through: its icon and label."),
            Setting.Color("tileColor", "Tile colour", "track", help: "Each output's background, with the tile style."),
            Setting.Color("labelColor", "Label colour", "secondary"),
        ],
        (settings, theme) => new OutputsWidget(settings, theme));

    private readonly PickList _chosen;
    private readonly bool _labels;
    private ButtonsWidget? _buttons;
    private TextBlock? _empty;
    private string? _shownKey;

    public OutputsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _chosen = settings.Picks("outputs");
        _labels = settings.Bool("labels");
    }

    public override bool PressableFromTablet => true;

    public override void Refresh(MetricStore store, DateTime now)
    {
        SoundProvider.WantOutputs(store, DateTimeOffset.UtcNow);
        // Nothing until the outputs have been read once, a second or so after the widget first asks.
        if (store.GetAttachment(SoundProvider.Outputs) is not IReadOnlyList<SoundOutput> all)
            return;
        var outputs = Pick(all, _chosen);
        var key = string.Join("\n", outputs.Select(o => o.Id));
        if (key != _shownKey)
        {
            _shownKey = key;
            Build(outputs);
        }
        _buttons?.Refresh(store, now);
    }

    /// <summary>All the outputs, in the sound settings' order, or the ones chosen that are on, in the order chosen.</summary>
    internal static IReadOnlyList<SoundOutput> Pick(IReadOnlyList<SoundOutput> outputs, PickList chosen) =>
        chosen.All
            ? outputs
            : chosen.Names.Select(name => outputs.FirstOrDefault(o => o.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<SoundOutput>().ToList();

    /// <summary>The buttons for the outputs, as a Buttons widget's: each one's icon by its kind and its label, switching to it by its id.</summary>
    internal static List<JsonObject> Buttons(IReadOnlyList<SoundOutput> outputs, bool labels) =>
        [.. outputs.Select(o => new JsonObject
        {
            ["label"] = labels ? Label(o, outputs) : "",
            ["icon"] = o.Kind switch
            {
                OutputKind.Headphones or OutputKind.Headset => "headphones",
                OutputKind.Display => "tv",
                _ => "volume-up",
            },
            ["action"] = WidgetAction.SoundOutput,
            ["target"] = o.Id,
        })];

    /// <summary>An output's own name, or its hardware's when another output shown has the same name (two Speakers).</summary>
    internal static string Label(SoundOutput output, IReadOnlyList<SoundOutput> shown) =>
        output.Hardware.Length > 0 && shown.Count(o => o.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase)) > 1 ? output.Hardware : output.Name;

    private void Build(IReadOnlyList<SoundOutput> outputs)
    {
        Children.Clear();
        _buttons = null;
        _empty = null;
        if (outputs.Count == 0)
        {
            _empty = CreateText(Settings.Number("fontSize"), Theme.Secondary);
            _empty.Text = Empty;
            Children.Add(_empty);
            return;
        }
        _buttons = new ButtonsWidget(new WidgetSettings(new JsonObject
        {
            ["type"] = ButtonsWidget.Descriptor.Type,
            ["buttons"] = new JsonArray([.. Buttons(outputs, _labels)]),
            ["columns"] = Settings.Number("columns"),
            ["fontSize"] = Settings.Number("fontSize"),
            ["iconSize"] = Settings.Number("iconSize"),
            ["spacing"] = Settings.Number("spacing"),
            ["style"] = Settings.String("style"),
            ["cornerRadius"] = Settings.Number("cornerRadius"),
            ["color"] = Settings.String("color"),
            ["activeColor"] = Settings.String("activeColor"),
            ["tileColor"] = Settings.String("tileColor"),
            ["labelColor"] = Settings.String("labelColor"),
            [Setting.ShrinkToFitKey] = Settings.Bool(Setting.ShrinkToFitKey),
        }, ButtonsWidget.Descriptor.Settings), Theme);
        // The full name in each button's tooltip, as the label may be short.
        foreach (var (target, output) in _buttons.Taps.Zip(outputs))
            target.ToolTip = $"Play sound through {output.FullName}";
        Children.Add(_buttons);
    }
}

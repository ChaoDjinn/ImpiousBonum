using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// OBS Studio at a glance: a status line (recording or live, for how long, and the scene on air) over buttons to record,
/// stream, save a replay or switch scenes. The buttons are a <see cref="ButtonsWidget"/> inside, so they take its on
/// colour while they're on and can be pressed from a tablet like any buttons.
/// </summary>
public sealed class ObsWidget : Widget
{
    /// <summary>What the status line says before OBS is turned on in settings.</summary>
    public const string OffText = "OBS is off: turn it on on the OBS Studio page";

    public static WidgetDescriptor Descriptor { get; } = new(
        "obs", "OBS Studio",
        "Whether OBS is recording or live and for how long, the scene on air, and buttons to record, stream, save a replay or switch scenes, which turn red while they're on. Connect OBS on the OBS Studio page first.",
        520, 150,
        [
            Setting.Toggle("status", "Status line", true, group: Setting.Content, help: "Recording or live and for how long, and the scene on air, above the buttons."),
            Setting.Toggle("record", "Record button", true, group: Setting.Content),
            Setting.Toggle("stream", "Stream button", true, group: Setting.Content,
                help: "Goes live straight away, with the stream settings in OBS. Turn it off if you only record."),
            Setting.Toggle("replay", "Replay buffer button", false, group: Setting.Content, help: "Turns OBS's replay buffer on or off."),
            Setting.Toggle("saveReplay", "Save replay button", true, group: Setting.Content,
                help: "Saves the last few seconds or minutes, as the replay buffer keeps. The replay buffer has to be on in OBS."),
            Setting.PlainText("scenes", "Scene buttons", null,
                help: "Scenes to switch to, by their names in OBS, with commas between them, e.g. Gaming, BRB. Each is a button, lit while its scene is on air."),
            Setting.Toggle("labels", "Labels", true, group: Setting.Content, help: "A word under each button's icon."),
            Setting.Number("fontSize", "Font size", 22, 6, 200, help: "The status line's; the labels are a little smaller."),
            Setting.ShrinkToFit(),
            Setting.Number("iconSize", "Icon size", 34, 0, 400, group: Setting.Appearance),
            Setting.Number("spacing", "Spacing", 10, 0, 200, group: Setting.Appearance),
            Setting.Choice("style", "Style", "tile", ["tile", "plain"], group: Setting.Appearance, help: "tile gives each button a background."),
            Setting.Number("cornerRadius", "Corner radius", 10, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Icon colour", "accent"),
            Setting.Color("tileColor", "Tile colour", "track", help: "Each button's background, with the tile style."),
            Setting.Color("labelColor", "Label colour", "foreground"),
            Setting.Color("activeColor", "On colour", "critical",
                help: "The status line while recording or live, and a button's icon and label while it's on."),
        ],
        (settings, theme) => new ObsWidget(settings, theme));

    private readonly TextBlock? _status;
    private readonly TextBlock? _scene;
    private readonly ButtonsWidget? _buttons;
    private readonly Brush _active;

    public ObsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _active = theme.Resolve(settings.String("activeColor"), theme.Critical);
        var fontSize = settings.Number("fontSize");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (settings.Bool("status"))
        {
            _status = CreateText(fontSize, theme.Secondary);
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            _scene = CreateText(fontSize * 0.85, theme.Secondary, HorizontalAlignment.Right);
            _scene.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(LabelAndValue(_status, _scene));
        }

        var buttons = Buttons(settings);
        if (buttons.Count > 0)
        {
            _buttons = new ButtonsWidget(new WidgetSettings(new JsonObject
            {
                ["type"] = ButtonsWidget.Descriptor.Type,
                ["buttons"] = new JsonArray([.. buttons]),
                ["fontSize"] = fontSize * 0.75,
                ["iconSize"] = settings.Number("iconSize"),
                ["spacing"] = settings.Number("spacing"),
                ["style"] = settings.String("style"),
                ["cornerRadius"] = settings.Number("cornerRadius"),
                ["color"] = settings.String("color"),
                ["tileColor"] = settings.String("tileColor"),
                ["labelColor"] = settings.String("labelColor"),
                ["activeColor"] = settings.String("activeColor"),
                [Setting.ShrinkToFitKey] = settings.Bool(Setting.ShrinkToFitKey),
            }, ButtonsWidget.Descriptor.Settings), theme);
            SetRow(_buttons, 1);
            if (_status is not null)
                _buttons.Margin = new Thickness(0, fontSize * 0.4, 0, 0);
            Children.Add(_buttons);
        }
    }

    /// <summary>The buttons the settings ask for, as a Buttons widget's: record, stream, the replay buffer, save replay, then the scenes.</summary>
    internal static List<JsonObject> Buttons(WidgetSettings settings)
    {
        var labels = settings.Bool("labels");
        var buttons = new List<JsonObject>();
        void Add(bool shown, string label, string icon, string action, string target = "")
        {
            if (shown)
                buttons.Add(new JsonObject { ["label"] = labels ? label : "", ["icon"] = icon, ["action"] = action, ["target"] = target });
        }
        Add(settings.Bool("record"), "Record", "record", WidgetAction.ObsRecord);
        Add(settings.Bool("stream"), "Stream", "broadcast", WidgetAction.ObsStream);
        Add(settings.Bool("replay"), "Replay", "replay", WidgetAction.ObsReplay);
        Add(settings.Bool("saveReplay"), "Save replay", "save", WidgetAction.ObsSaveReplay);
        foreach (var scene in Scenes(settings.OptionalString("scenes")))
            Add(true, scene, "layout", WidgetAction.ObsScene, scene);
        return buttons;
    }

    /// <summary>The scene names typed, in order, once each: "Gaming, BRB" is Gaming and BRB.</summary>
    internal static IReadOnlyList<string> Scenes(string? typed) =>
        (typed ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public override void Refresh(MetricStore store, DateTime now)
    {
        _buttons?.Refresh(store, now);
        if (_status is null || _scene is null)
            return;
        var (text, live) = Status(store);
        _status.Text = text;
        _status.Foreground = live ? _active : Theme.Secondary;
        _scene.Text = store.TryGet(ObsProvider.Scene, out var scene) && scene.Text is { } name ? name : "";
    }

    /// <summary>
    /// The status line: <c>● LIVE 1:30:00 · REC 12:05</c> while streaming or recording (and live, for the on colour),
    /// <c>Ready</c> while OBS is connected and doing neither, and otherwise why it isn't connected.
    /// </summary>
    internal static (string Text, bool Live) Status(MetricStore store)
    {
        if (!store.TryGet(ObsProvider.Status, out var status))
            return (OffText, false);
        if (!store.TryGet(ObsProvider.Recording, out var recording) || recording.Value is null)
            return (status.Text ?? OffText, false);
        var parts = new List<string>();
        if (store.TryGet(ObsProvider.Streaming, out var streaming) && streaming.Value > 0)
            parts.Add(Going("LIVE", store, ObsProvider.StreamingTime));
        if (recording.Value > 0)
            parts.Add(Going("REC", store, ObsProvider.RecordingTime));
        return parts.Count > 0 ? ("● " + string.Join(" · ", parts), true) : ("Ready", false);
    }

    private static string Going(string what, MetricStore store, string time) =>
        store.TryGet(time, out var sample) && sample.Text is { Length: > 0 } text ? $"{what} {text}" : what;
}

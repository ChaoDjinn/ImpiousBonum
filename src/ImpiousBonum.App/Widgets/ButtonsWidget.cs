using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Buttons to tap on the dashboard: media and volume keys, opening programs, files and web addresses, keyboard
/// shortcuts, switching layout, or sending a web request. The dashboard never takes focus, so a shortcut goes to the
/// app you're using.
/// </summary>
public sealed class ButtonsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "buttons", "Buttons",
        "Buttons to tap: play/pause, skip and volume, open a program, file or web address, send a keyboard shortcut to "
        + "the app you're using, switch layout, or send a web request to Home Assistant, smart lights or ntfy.",
        480, 110,
        [
            Setting.Items("buttons", "Buttons",
            [
                Setting.PlainText("label", "Label", "Play/pause", help: "Empty shows just the icon."),
                Setting.Icon("icon", "Icon", "play-pause", allowNone: true),
                Setting.Choice("action", "Action", WidgetAction.PlayPause, WidgetAction.Kinds, group: Setting.Content,
                    help: "Media and volume actions work like the keyboard's media keys. open, keys, layout and web use the target below. "
                        + "mic-mute mutes or unmutes your microphone; with the mic icon, the button shows mic-off while it's muted. "
                        + "app-mute mutes or unmutes one app, named in the target. "
                        + "sound-output plays sound through the output in the target, lit while it is. "
                        + "The obs actions control OBS Studio, once it's connected on the OBS page; record, stream, replay and scene buttons take the on colour while they're on."),
                Setting.PlainText("target", "Target", null,
                    help: "open: a program, file, folder or web address, e.g. calc.exe, C:\\Games or https://… · "
                        + "keys: a shortcut, e.g. Ctrl+Shift+M or F13 · layout: the layout's name · "
                        + "web: the address to send to, e.g. http://homeassistant.local:8123/api/webhook/… · "
                        + "obs-scene: the scene's name, e.g. BRB · obs-mute: the audio source's name, e.g. Mic/Aux · "
                        + "app-mute: the app, as the volume mixer names it or by its program's name, e.g. Spotify or chrome · "
                        + "sound-output: the output's name, e.g. Headphones; several with commas between take turns, and empty takes turns with them all"),
                Setting.PlainText("arguments", "Arguments", null, help: "open: command-line arguments for a program."),
                Setting.Choice("method", "Method", "POST", WidgetAction.WebMethods, group: Setting.Content,
                    help: "web: how to send it. Webhooks, Home Assistant's services and ntfy want POST."),
                Setting.PlainText("body", "Body", null,
                    help: "web: what a POST or PUT sends, e.g. {\"entity_id\": \"script.movie_night\"}, or the message for ntfy."),
                Setting.PlainText("contentType", "Content type", null,
                    help: "web: the body's type. Empty sends application/json for a body in { } or [ ], and text/plain for anything else."),
                Setting.PlainText("header", "Header", null,
                    help: "web: one header as Name: value, e.g. Authorization: Bearer and a token. Left out when you export the layout."),
            ],
            defaults:
            [
                Button("", "previous", WidgetAction.Previous),
                Button("", "play-pause", WidgetAction.PlayPause),
                Button("", "next", WidgetAction.Next),
                Button("", "mute", WidgetAction.Mute),
            ]),
            Setting.Number("columns", "Columns", 0, 0, 50, group: Setting.Appearance, help: "0 puts every button in one row."),
            Setting.Number("fontSize", "Label size", 20, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("iconSize", "Icon size", 40, 0, 400, group: Setting.Appearance),
            Setting.Number("spacing", "Spacing", 10, 0, 200, group: Setting.Appearance),
            Setting.Choice("style", "Style", "tile", ["tile", "plain"], group: Setting.Appearance, help: "tile gives each button a background."),
            Setting.Number("cornerRadius", "Corner radius", 10, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Icon colour", "accent"),
            Setting.Color("tileColor", "Tile colour", "track", help: "Each button's background, with the tile style. track is the theme's bar track."),
            Setting.Color("labelColor", "Label colour", "foreground"),
            Setting.Color("activeColor", "On colour", "critical",
                help: "An OBS button's icon and label while it's on: recording, streaming, the replay buffer on, or its scene on air."),
        ],
        (settings, theme) => new ButtonsWidget(settings, theme));

    private static JsonObject Button(string label, string icon, string action) =>
        new() { ["label"] = label, ["icon"] = icon, ["action"] = action };

    /// <summary>The icons of mic-mute buttons that follow the microphone's mute.</summary>
    private readonly List<Path> _micIcons = [];

    /// <summary>OBS buttons that take the on colour while what they control is on.</summary>
    private readonly List<OnButton> _onButtons = [];

    private Brush? _iconBrush;
    private Brush? _labelBrush;
    private Brush? _activeBrush;
    private bool _switchesOutput;
    private readonly List<TapTarget> _taps = [];

    /// <summary>The buttons, in the order they're set up.</summary>
    internal IReadOnlyList<TapTarget> Taps => _taps;

    /// <summary>A button that shows whether it's on: when, its icon (and whether that's filled) and its label.</summary>
    private sealed record OnButton(Func<MetricStore, bool> IsOn, Path? Icon, bool Filled, TextBlock? Label)
    {
        public bool? Shown { get; set; }
    }

    public ButtonsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var buttons = settings.Items("buttons");
        var columns = (int)settings.Number("columns");
        var spacing = settings.Number("spacing");
        var fontSize = settings.Number("fontSize");
        var iconSize = settings.Number("iconSize");
        var tile = settings.String("style") != "plain";
        var radius = new CornerRadius(settings.Number("cornerRadius"));
        var iconBrush = theme.Resolve(settings.String("color"), theme.Accent);
        var tileBrush = theme.Resolve(settings.String("tileColor"), theme.Track);
        var labelBrush = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _iconBrush = iconBrush;
        _labelBrush = labelBrush;
        _activeBrush = theme.Resolve(settings.String("activeColor"), theme.Critical);

        var grid = new UniformGrid
        {
            Columns = columns > 0 ? columns : Math.Max(1, buttons.Count),
            Margin = new Thickness(-spacing / 2),
        };
        foreach (var button in buttons)
        {
            var action = new WidgetAction(button.String("action"), button.String("target").Trim(), button.String("arguments").Trim());
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var filled = WidgetAction.MediaKinds.Contains(action.Kind);
            Path? iconPath = null;
            TextBlock? text = null;
            if (iconSize > 0 && Icons.Get(button.String("icon")) is not null)
            {
                var icon = Icons.Create(button.String("icon"), iconBrush, iconSize, filled: filled);
                content.Children.Add(icon);
                iconPath = (icon as Viewbox)?.Child as Path;
                if (action.Kind == WidgetAction.MicMute && IsMicIcon(button.String("icon")) && iconPath is not null)
                    _micIcons.Add(iconPath);
            }
            if (button.String("label") is { Length: > 0 } label)
            {
                text = CreateText(fontSize, labelBrush, HorizontalAlignment.Center);
                text.Text = label;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                if (content.Children.Count > 0)
                    text.Margin = new Thickness(0, fontSize * 0.2, 0, 0);
                content.Children.Add(text);
            }
            if (IsOn(action) is { } isOn)
                _onButtons.Add(new OnButton(isOn, iconPath, filled, text));
            _switchesOutput |= action.Kind == WidgetAction.SoundOutput;

            var target = new TapTarget
            {
                // Labels end with … when they're too long; a button too short for its icon and label shrinks them.
                Child = Fit(content, FitDirection.Height),
                Background = tile ? tileBrush : Brushes.Transparent,
                CornerRadius = radius,
                Margin = new Thickness(spacing / 2),
                Padding = new Thickness(spacing / 2),
                ToolTip = Describe(action),
            };
            if (action.Kind.Equals(WidgetAction.Web, StringComparison.OrdinalIgnoreCase))
                ConnectWeb(target, WebAction(action, button), theme);
            else
                target.Tapped += (_, _) => RequestAction(action);
            grid.Children.Add(target);
            _taps.Add(target);
        }
        Children.Add(grid);
    }

    public override bool PressableFromTablet => true;

    public override void Refresh(MetricStore store, DateTime now)
    {
        // Output buttons need the outputs read, which happens only while something asks.
        if (_switchesOutput)
            SoundProvider.WantOutputs(store, DateTimeOffset.UtcNow);
        foreach (var button in _onButtons)
        {
            var on = button.IsOn(store);
            if (button.Shown == on)
                continue;
            button.Shown = on;
            var iconBrush = on ? _activeBrush : _iconBrush;
            if (button.Icon is { } path)
            {
                path.Stroke = iconBrush;
                path.Fill = button.Filled ? iconBrush : null;
            }
            if (button.Label is { } label)
                label.Foreground = on ? _activeBrush : _labelBrush;
        }

        // Mic-mute buttons with a mic icon show whether the microphone is muted.
        if (_micIcons.Count == 0 || !store.TryGet(PrivacyProvider.MicMuted, out var sample) || sample.Value is not { } muted)
            return;
        var icon = Icons.Get(muted > 0 ? MicOffIcon : MicIcon);
        foreach (var path in _micIcons)
            path.Data = icon;
    }

    /// <summary>
    /// When an OBS button is on, from OBS's readings: recording, streaming, the replay buffer on, or its scene on air.
    /// Null for buttons that don't show it.
    /// </summary>
    internal static Func<MetricStore, bool>? IsOn(WidgetAction action)
    {
        static bool Is(MetricStore store, string id) => store.TryGet(id, out var sample) && sample.Value > 0;
        return action.Kind.ToLowerInvariant() switch
        {
            WidgetAction.ObsRecord => store => Is(store, ObsProvider.Recording),
            WidgetAction.ObsStream => store => Is(store, ObsProvider.Streaming),
            WidgetAction.ObsReplay => store => Is(store, ObsProvider.Replay),
            WidgetAction.ObsScene when action.Target.Length > 0 => store =>
                store.TryGet(ObsProvider.Scene, out var scene) && string.Equals(scene.Text, action.Target, StringComparison.OrdinalIgnoreCase),
            // One output, not one that takes turns, is on while Windows plays through it.
            WidgetAction.SoundOutput when action.Target.Length > 0 && !action.Target.Contains(',') => store =>
                store.GetAttachment(SoundProvider.Outputs) is IReadOnlyList<SoundOutput> outputs && SoundOutputs.Find(outputs, action.Target)?.Default == true,
            _ => null,
        };
    }

    private const string MicIcon = "mic", MicOffIcon = "mic-off";

    private static bool IsMicIcon(string name) =>
        name.Equals(MicIcon, StringComparison.OrdinalIgnoreCase) || name.Equals(MicOffIcon, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a button does, in words, e.g. for its tooltip.</summary>
    internal static string Describe(WidgetAction action) => action.Kind switch
    {
        WidgetAction.Open => action.Target.Length == 0 ? "Opens nothing yet: set a target" : $"Opens {action.Target} {action.Arguments}".TrimEnd(),
        WidgetAction.Keys => action.Target.Length == 0 ? "Sends no keys yet: set a target" : $"Sends {action.Target}",
        WidgetAction.Layout => action.Target.Length == 0 ? "Switches to no layout yet: set a target" : $"Switches to the \"{action.Target}\" layout",
        WidgetAction.Web => Core.WebRequestSender.Address(action.Target) is { } address ? $"Sends a web request to {address.Host}" : "Sends nothing yet: set a web address as its target",
        WidgetAction.PlayPause => "Play or pause",
        WidgetAction.VolumeUp => "Volume up",
        WidgetAction.VolumeDown => "Volume down",
        WidgetAction.MicMute => "Mute or unmute the microphone",
        WidgetAction.ObsRecord => "Start or stop recording in OBS",
        WidgetAction.ObsStream => "Start or stop streaming in OBS",
        WidgetAction.ObsReplay => "Turn OBS's replay buffer on or off",
        WidgetAction.ObsSaveReplay => "Save OBS's replay buffer",
        WidgetAction.ObsScene => action.Target.Length == 0 ? "Shows no OBS scene yet: set a target" : $"Shows the \"{action.Target}\" scene in OBS",
        WidgetAction.ObsMute => action.Target.Length == 0 ? "Mutes no OBS source yet: set a target" : $"Mutes or unmutes \"{action.Target}\" in OBS",
        WidgetAction.AppMute => action.Target.Length == 0 ? "Mutes no app yet: set a target" : $"Mutes or unmutes {action.Target}",
        WidgetAction.SoundOutput => action.Target.Length == 0 ? "Plays sound through the next output"
            : action.Target.Contains(',') ? $"Plays sound through the next of {action.Target}" : $"Plays sound through {action.Target}",
        _ => char.ToUpperInvariant(action.Kind[0]) + action.Kind[1..],
    };

    /// <summary>
    /// Everything the layout's buttons would open (programs, files, web addresses, with their arguments). A layout from
    /// someone else can make a harmless-looking button run anything, so imports show this list and ask first.
    /// </summary>
    public static IReadOnlyList<string> OpenTargets(LayoutDocument layout) =>
        OpenButtons(layout)
            .Select(b => $"{b.GetString("target")} {b.GetString("arguments")}".Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Clears what the layout's buttons open, keeping the buttons themselves (they then do nothing).</summary>
    public static void ClearOpenTargets(LayoutDocument layout)
    {
        foreach (var button in OpenButtons(layout))
        {
            button.Remove("target");
            button.Remove("arguments");
        }
    }

    private static IEnumerable<JsonObject> OpenButtons(LayoutDocument layout) => Buttons(layout, WidgetAction.Open);

    /// <summary>
    /// Where the layout's web buttons send to, and how ("POST http://homeassistant.local:8123/api/webhook/…"), noting
    /// those with a header but never its value. A layout from someone else could switch things in your home or carry
    /// their token, so imports show this list with <see cref="OpenTargets"/> and ask first.
    /// </summary>
    public static IReadOnlyList<string> WebTargets(LayoutDocument layout) =>
        Buttons(layout, WidgetAction.Web)
            .Where(b => !string.IsNullOrWhiteSpace(b.GetString("target")))
            .Select(b => $"{(b.GetString("method") is { Length: > 0 } method ? method.ToUpperInvariant() : "POST")} {b.GetString("target")!.Trim()}"
                + (string.IsNullOrWhiteSpace(b.GetString("header")) ? "" : " (with a header)"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Clears where the layout's web buttons send to, and their headers, keeping the buttons (they then send nothing).</summary>
    public static void ClearWebTargets(LayoutDocument layout)
    {
        foreach (var button in Buttons(layout, WidgetAction.Web))
        {
            button.Remove("target");
            button.Remove("header");
        }
    }

    /// <summary>
    /// Takes the headers off the layout's web buttons, for exporting: a header is often a password or token, such as
    /// Home Assistant's. Returns how many it took off.
    /// </summary>
    public static int RemoveHeaders(LayoutDocument layout)
    {
        var removed = 0;
        foreach (var button in Buttons(layout, WidgetAction.Web).Where(b => !string.IsNullOrWhiteSpace(b.GetString("header"))))
        {
            button.Remove("header");
            removed++;
        }
        return removed;
    }

    private static IEnumerable<JsonObject> Buttons(LayoutDocument layout, string action) =>
        layout.Widgets
            .Where(w => string.Equals(w.GetString("type"), Descriptor.Type, StringComparison.OrdinalIgnoreCase))
            .SelectMany(w => (w["buttons"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Where(b => string.Equals(b.GetString("action") ?? WidgetAction.PlayPause, action, StringComparison.OrdinalIgnoreCase));

    /// <summary>How long a web button stays lit to show how its request went.</summary>
    internal static readonly TimeSpan AnsweredFor = TimeSpan.FromSeconds(1.5);

    /// <summary>A web button's action, with its request's settings.</summary>
    private static WidgetAction WebAction(WidgetAction action, WidgetSettings button) => action with
    {
        Method = button.String("method"),
        Body = button.String("body"),
        ContentType = button.String("contentType").Trim(),
        Header = button.String("header").Trim(),
    };

    /// <summary>
    /// A web button's request goes in the background. The button stays pressed until the answer, ignoring taps so they
    /// don't stack up, then lights up for a moment: in the accent colour if it worked, the critical colour if it didn't.
    /// </summary>
    private void ConnectWeb(TapTarget target, WidgetAction action, Theme theme)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = AnsweredFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            target.Light = null;
        };
        action = action with
        {
            Done = worked =>
            {
                target.Waiting = false;
                target.Light = worked ? theme.Accent : theme.Critical;
                timer.Stop();
                timer.Start();
            },
        };
        target.Tapped += (_, _) =>
        {
            target.Waiting = true;
            // Nothing takes it in the editor's preview or a snapshot, so there's no answer to wait for.
            if (!RequestAction(action))
                target.Waiting = false;
        };
    }
}

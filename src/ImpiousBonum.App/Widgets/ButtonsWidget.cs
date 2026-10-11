using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Buttons to tap on the dashboard: media and volume keys, opening programs, files and web addresses, keyboard
/// shortcuts, or switching layout. The dashboard never takes focus, so a shortcut goes to the app you're using.
/// </summary>
public sealed class ButtonsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "buttons", "Buttons",
        "Buttons to tap: play/pause, skip and volume, open a program, file or web address, send a keyboard shortcut to "
        + "the app you're using, or switch layout.",
        480, 110,
        [
            Setting.Items("buttons", "Buttons",
            [
                Setting.PlainText("label", "Label", "Play/pause", help: "Empty shows just the icon."),
                Setting.Icon("icon", "Icon", "play-pause", allowNone: true),
                Setting.Choice("action", "Action", WidgetAction.PlayPause, WidgetAction.Kinds, group: Setting.Content,
                    help: "Media and volume actions work like the keyboard's media keys. open, keys and layout use the target below."),
                Setting.PlainText("target", "Target", null,
                    help: "open: a program, file, folder or web address, e.g. calc.exe, C:\\Games or https://… · "
                        + "keys: a shortcut, e.g. Ctrl+Shift+M or F13 · layout: the layout's name."),
                Setting.PlainText("arguments", "Arguments", null, help: "open: command-line arguments for a program."),
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
            Setting.Number("iconSize", "Icon size", 40, 0, 400, group: Setting.Appearance),
            Setting.Number("spacing", "Spacing", 10, 0, 200, group: Setting.Appearance),
            Setting.Choice("style", "Style", "tile", ["tile", "plain"], group: Setting.Appearance, help: "tile gives each button a background."),
            Setting.Number("cornerRadius", "Corner radius", 10, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Icon colour", "accent"),
        ],
        (settings, theme) => new ButtonsWidget(settings, theme));

    private static JsonObject Button(string label, string icon, string action) =>
        new() { ["label"] = label, ["icon"] = icon, ["action"] = action };

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

        var grid = new UniformGrid
        {
            Columns = columns > 0 ? columns : Math.Max(1, buttons.Count),
            Margin = new Thickness(-spacing / 2),
        };
        foreach (var button in buttons)
        {
            var action = new WidgetAction(button.String("action"), button.String("target").Trim(), button.String("arguments").Trim());
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (iconSize > 0 && Icons.Get(button.String("icon")) is not null)
                content.Children.Add(Icons.Create(button.String("icon"), iconBrush, iconSize, filled: WidgetAction.MediaKinds.Contains(action.Kind)));
            if (button.String("label") is { Length: > 0 } label)
            {
                var text = CreateText(fontSize, alignment: HorizontalAlignment.Center);
                text.Text = label;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                if (content.Children.Count > 0)
                    text.Margin = new Thickness(0, fontSize * 0.2, 0, 0);
                content.Children.Add(text);
            }

            var target = new TapTarget
            {
                Child = content,
                Background = tile ? theme.Track : Brushes.Transparent,
                CornerRadius = radius,
                Margin = new Thickness(spacing / 2),
                Padding = new Thickness(spacing / 2),
                ToolTip = Describe(action),
            };
            target.Tapped += (_, _) => RequestAction(action);
            grid.Children.Add(target);
        }
        Children.Add(grid);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
    }

    /// <summary>What a button does, in words, e.g. for its tooltip.</summary>
    internal static string Describe(WidgetAction action) => action.Kind switch
    {
        WidgetAction.Open => action.Target.Length == 0 ? "Opens nothing yet: set a target" : $"Opens {action.Target} {action.Arguments}".TrimEnd(),
        WidgetAction.Keys => action.Target.Length == 0 ? "Sends no keys yet: set a target" : $"Sends {action.Target}",
        WidgetAction.Layout => action.Target.Length == 0 ? "Switches to no layout yet: set a target" : $"Switches to the \"{action.Target}\" layout",
        WidgetAction.PlayPause => "Play or pause",
        WidgetAction.VolumeUp => "Volume up",
        WidgetAction.VolumeDown => "Volume down",
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

    private static IEnumerable<JsonObject> OpenButtons(LayoutDocument layout) =>
        layout.Widgets
            .Where(w => string.Equals(w.GetString("type"), Descriptor.Type, StringComparison.OrdinalIgnoreCase))
            .SelectMany(w => (w["buttons"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Where(b => string.Equals(b.GetString("action") ?? WidgetAction.PlayPause, WidgetAction.Open, StringComparison.OrdinalIgnoreCase));
}

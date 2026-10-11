using System.Windows;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Something a tap on a widget asks the app to do, such as open a program or skip a track. Widgets raise it with
/// <see cref="Widget.ActionEvent"/>; the dashboard window passes it to the app, which runs it. Snapshots, the tablet
/// view and the editor's preview don't listen, so nothing happens there.
/// </summary>
/// <param name="Kind">One of the constants below.</param>
/// <param name="Target">For <see cref="Open"/>: a program, file, folder or web address. For <see cref="Keys"/>: a shortcut such as <c>Ctrl+Shift+M</c>. For <see cref="Layout"/>: a layout's name.</param>
/// <param name="Arguments">For <see cref="Open"/>: the program's command-line arguments.</param>
public sealed record WidgetAction(string Kind, string Target = "", string Arguments = "")
{
    public const string Open = "open";
    public const string Keys = "keys";
    public const string Layout = "layout";
    public const string PlayPause = "play-pause";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Stop = "stop";
    public const string VolumeUp = "volume-up";
    public const string VolumeDown = "volume-down";
    public const string Mute = "mute";

    /// <summary>A Windows notification with <see cref="Target"/> as its message, for widgets with news to give (a finished timer). Not a button action.</summary>
    public const string Notify = "notify";

    /// <summary>Every kind, in the order the editor offers them.</summary>
    public static readonly IReadOnlyList<string> Kinds = [PlayPause, Next, Previous, Stop, VolumeUp, VolumeDown, Mute, Open, Keys, Layout];

    /// <summary>The media and volume kinds, which act like the keyboard's media keys.</summary>
    public static readonly IReadOnlyList<string> MediaKinds = [PlayPause, Next, Previous, Stop, VolumeUp, VolumeDown, Mute];
}

/// <summary>Carries a <see cref="WidgetAction"/> up the visual tree.</summary>
public sealed class WidgetActionEventArgs(WidgetAction action) : RoutedEventArgs(Widget.ActionEvent)
{
    public WidgetAction Action { get; } = action;
}

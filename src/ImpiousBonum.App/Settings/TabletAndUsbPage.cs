using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>Tablet and USB screen: the dashboard in a browser on the local network, and on a small USB screen.</summary>
internal sealed class TabletAndUsbPage(ISettingsHost host) : CardPage("Tablet and USB screen",
    "Show the dashboard somewhere besides a monitor: in a browser on a tablet or phone on your network, or on a small USB screen.")
{
    internal static readonly int[] UsbBrightness = [10, 25, 40, 60, 80, 100];

    private SettingsCard? _usbCard;

    // What the link below shows: a saved layout, a tablet screen (by name), or neither for the main dashboard. Not saved:
    // every link works, whichever is picked here.
    private (string? Layout, string? Screen) _linkFor;

    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        Add(Ui.Heading("Tablet view"));
        var on = host.TabletRunning;
        Add(Ui.Card("", "Show on tablets", "Serve the dashboard to browsers on your local network: open the link on a tablet or phone to see it there",
            Ui.Switch(Settings.TabletView, "Show on tablets", value =>
            {
                host.SetTabletView(value);
                Load();
            })));

        var buttons = Add(Ui.Card("", "Let tablets press buttons",
            "Taps on the tablet press the layout's buttons and media controls, and start Recent games' games. Anyone with the link could press them.",
            Ui.Switch(Settings.TabletButtons, "Let tablets press buttons", host.SetTabletButtons)));
        buttons.IsEnabled = on;

        var readings = Add(Ui.Card("", "Share readings with other apps",
            "Apps on your network such as Home Assistant or Rainmeter can read every reading as JSON with the link's secret, "
            + "including the text ones: what's playing, your next event, your IP address. Anyone with the link could read them.",
            Ui.Switch(Settings.TabletReadings, "Share readings with other apps", host.SetTabletReadings)));
        readings.IsEnabled = on;

        // Tablet screens first (each a layout planned at a tablet's size), then the saved layouts; ones that have gone aren't offered.
        var saved = host.Layouts.List();
        var screens = Settings.TabletScreens;
        _linkFor = (saved.FirstOrDefault(l => string.Equals(l, _linkFor.Layout, StringComparison.OrdinalIgnoreCase)),
            _linkFor.Screen is { } named ? TabletScreen.Find(screens, named)?.Name : null);
        var targets = screens.Select(s => (((string?)null, (string?)s.Name), $"{s.Name} (tablet screen)"))
            .Concat(saved.Select(l => (((string?)l, (string?)null), l)))
            .Prepend((((string?)null, (string?)null), "Same as the main dashboard"));
        var layoutCard = Add(Ui.Card("", "Layout for the link",
            "Each tablet can show a layout of its own: pick one, or a tablet screen from the Screens page, then open the link below on that tablet",
            Ui.Choice(targets, _linkFor, "Layout for the link", target =>
            {
                _linkFor = target;
                Load();
            })));
        layoutCard.IsEnabled = on;

        var link = on && Settings.TabletToken is { } token ? TabletLinkWindow.FirstLink(Settings.TabletPort, token, _linkFor.Layout, _linkFor.Screen) : null;
        var linkCard = Add(Ui.Card("", "Link",
            on ? link ?? "This PC doesn't seem to be on a local network." : "Turn on the tablet view for a link to open.",
            Ui.Row(
                Ui.Button("Show QR code…", () => host.ShowTabletLink(_linkFor.Layout, _linkFor.Screen)),
                Ui.Button("Copy", () => host.CopyTabletLink(_linkFor.Layout, _linkFor.Screen)),
                Ui.Button("New link", () =>
                {
                    host.NewTabletLink();
                    Load();
                }))));
        linkCard.IsEnabled = on;
        Add(Ui.Note("A layout of its own stays as it is, in its own theme: game layouts and the schedule only change the main dashboard. "
            + "Renaming or deleting it stops its links working. A tablet screen's link shows whichever layout you give that screen, "
            + "planned at the tablet's size: add one on the Screens page. "
            + "A new link stops every old one working, for when one's been shared with someone it shouldn't have been."));

        Add(Ui.Heading("USB screen"));
        var usb = Settings.UsbScreen;
        _usbCard = Add(Ui.Card("", "Show on a USB screen", host.UsbStatus,
            Ui.Switch(usb.Enabled, "Show on a USB screen", value => Change(usb with { Enabled = value }))));

        var ports = System.IO.Ports.SerialPort.GetPortNames().Order().Select(p => (p, p)).Prepend((UsbScreenSettings.Auto, "Find it automatically"));
        Add(Ui.Card("", "Port", "Where the screen is plugged in. It's usually found by itself.",
            Ui.Choice(ports, usb.Port, "Port", port => Change(usb with { Port = port }))));
        var layouts = host.Layouts.List().Select(l => ((string?)l, l)).Prepend(((string?)null, "Same as the main dashboard"));
        Add(Ui.Card("", "Layout", "A layout of its own, made for the small screen, or the main dashboard's",
            Ui.Choice(layouts, usb.Layout, "Layout", layout => Change(usb with { Layout = layout }))));
        Add(Ui.Card("", "Brightness", null,
            Ui.Choice(UsbBrightness.Select(b => (b, $"{b}%")), usb.Brightness, "Brightness", level => Change(usb with { Brightness = level }))));
        Add(Ui.Card("", "Way up", "Which way up the screen is mounted",
            Ui.Choice(UsbScreenSettings.Orientations, usb.Orientation, "Way up", orientation => Change(usb with { Orientation = orientation }))));
        Add(Ui.Note("For Turing Smart Screen 3.5\" screens and their clones."));
    }

    private void Change(UsbScreenSettings usb)
    {
        host.SetUsbScreen(usb);
        Load();
    }

    public override void Refresh()
    {
        if (_usbCard is not null)
            _usbCard.Description = host.UsbStatus;
    }
}

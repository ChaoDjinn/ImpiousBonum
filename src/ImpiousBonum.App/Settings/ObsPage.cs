using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Obs;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Settings;

/// <summary>OBS Studio: whether to connect to its WebSocket server, where it is and its password, and how connecting is going.</summary>
internal sealed class ObsPage(ISettingsHost host) : CardPage("OBS Studio",
    "Buttons that start and stop recording, streaming and the replay buffer, save a replay or switch scenes in OBS, and readings for whether it's recording or live, for how long, and how the stream is going. They talk to OBS's own WebSocket server, built into OBS since version 28.")
{
    internal const string SetUpNote =
        "In OBS, open Tools → WebSocket Server Settings, tick Enable WebSocket server, and click Show Connect Info for the password.";

    internal const string AddressNote =
        "Leave it empty for OBS on this PC. For OBS on another PC, such as a streaming PC, its address as Show Connect Info gives it, e.g. 192.168.1.20:4455.";

    internal const string PasswordNote =
        "The server password from Show Connect Info. It's kept in settings.json on this PC, and so in backups you make, and only ever sent to OBS, hashed.";

    internal const string NotAnAddress = "That isn't an address. Leave it empty for this PC, or give one like 192.168.1.20:4455.";

    private SettingsCard? _address;
    private SettingsCard? _status;
    private TextBox? _addressBox;
    private PasswordBox? _passwordBox;

    protected override void Build()
    {
        var settings = host.Settings;
        Add(Ui.Card("", "Connect to OBS", SetUpNote, Ui.Switch(settings.Obs, "Connect to OBS", on =>
        {
            Commit();
            host.SetObs(on, settings.ObsAddress, settings.ObsPassword);
            Load();
        })));
        (_address, _status, _addressBox, _passwordBox) = (null, null, null, null);
        if (!settings.Obs)
            return;

        _addressBox = new TextBox { Text = settings.ObsAddress ?? "", Width = 300 };
        Placeholder.SetText(_addressBox, "This PC");
        AutomationProperties.SetName(_addressBox, "Address");
        Taken(_addressBox);
        _address = Add(Ui.Card("", "Address", AddressNote, _addressBox));
        // Hidden as it's typed, like any password: the settings window might be on a stream.
        _passwordBox = new PasswordBox { Password = settings.ObsPassword ?? "", Width = 300 };
        AutomationProperties.SetName(_passwordBox, "Server password");
        Taken(_passwordBox);
        Add(Ui.Card("", "Server password", PasswordNote, _passwordBox));
        _status = Add(Ui.Card("", "Now", Status(host.Store, settings)));
        Add(Ui.Note("Add OBS buttons with a Buttons widget (its actions starting obs-), and the readings, under OBS in the metric picker, to any widget. "
            + "OBS is asked how it's going once a second, and looked for every few seconds while it's closed."));
    }

    /// <summary>What's typed is taken when Enter is pressed or the box loses focus.</summary>
    private void Taken(Control box)
    {
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
        };
    }

    /// <summary>Takes the address and password typed in; an address that can't be one says what's wanted instead.</summary>
    internal void Commit()
    {
        if (_addressBox is null || _passwordBox is null || _address is null)
            return;
        var address = _addressBox.Text.Trim() is { Length: > 0 } typed ? typed : null;
        var password = _passwordBox.Password is { Length: > 0 } secret ? secret : null;
        if (ObsRequest.AddressOf(address) is null)
        {
            _address.Description = NotAnAddress;
            return;
        }
        _address.Description = AddressNote;
        if (address != host.Settings.ObsAddress || password != host.Settings.ObsPassword)
            host.SetObs(host.Settings.Obs, address, password);
    }

    /// <summary>How connecting is going (<c>obs.status</c>), or that it's off.</summary>
    internal static string Status(MetricStore? store, AppSettings settings)
    {
        if (!settings.Obs)
            return "Off";
        return store is not null && store.TryGet(ObsProvider.Status, out var status) && status.Text is { } text ? text : "Connecting to OBS";
    }

    public override void Refresh()
    {
        if (_status is not null)
            _status.Description = Status(host.Store, host.Settings);
    }

    /// <summary>Whatever's been typed and not yet taken is taken now.</summary>
    public override void Left() => Commit();
}

using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.App.Settings;

/// <summary>Steam: your own Web API key and your profile, for your friends online and your achievements, and how fetching them is going.</summary>
internal sealed class SteamPage(ISettingsHost host) : CardPage("Steam",
    "Your Steam friends who are online and what they're playing, and your achievements in the game you're playing, from Steam's Web API with a key of your own. Nothing is fetched from Steam until you add one. Steam downloads and recent games don't need it: they're read from Steam's files on your PC.")
{
    internal const string KeyNote =
        "Your own key, from Steam's Web API key page: sign in, give any domain name (localhost will do) and copy the key. It's kept in settings.json on this PC and only ever sent to Steam.";

    internal const string ProfileNote =
        "Your profile's address, such as steamcommunity.com/id/yourname, or its 17-digit ID. Steam shares only what your privacy settings make public: your friends list for who's online, and your game details for your achievements.";

    internal const string NotAProfile = "That isn't a profile. Paste its address, such as steamcommunity.com/id/yourname, or its 17-digit ID.";

    private SettingsCard? _profile;
    private SettingsCard? _status;
    private TextBox? _keyBox;
    private TextBox? _profileBox;

    protected override void Build()
    {
        var settings = host.Settings;
        _keyBox = Box(settings.SteamApiKey, "Paste your key", "Web API key");
        Add(Ui.Card("", "Web API key", KeyNote, Ui.Row(_keyBox, Ui.Button("Get a key", () => Ui.Open(SteamWeb.KeyPage)))));
        _profileBox = Box(settings.SteamProfile, "steamcommunity.com/id/…", "Your profile");
        _profile = Add(Ui.Card("", "Your profile", ProfileNote, _profileBox));
        _status = Add(Ui.Card("", "Now", Status(host.Store, settings)));
        Add(Ui.Note("Who's online is checked every minute, and your achievements every 2 minutes while a Steam game is in front."));
    }

    /// <summary>A box whose text is taken when Enter is pressed, when it loses focus, or when the page is left.</summary>
    private TextBox Box(string? text, string placeholder, string name)
    {
        var box = new TextBox { Text = text ?? "", Width = 300 };
        Placeholder.SetText(box, placeholder);
        AutomationProperties.SetName(box, name);
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
        };
        return box;
    }

    /// <summary>Takes the key and the profile typed in; a profile that can't be one says what's wanted instead.</summary>
    internal void Commit()
    {
        if (_keyBox is null || _profileBox is null || _profile is null)
            return;
        var key = _keyBox.Text.Trim() is { Length: > 0 } typedKey ? typedKey : null;
        var profile = _profileBox.Text.Trim() is { Length: > 0 } typedProfile ? typedProfile : null;
        if (profile is not null && SteamWeb.Profile(profile) is null)
        {
            _profile.Description = NotAProfile;
            return;
        }
        _profile.Description = ProfileNote;
        if (key != host.Settings.SteamApiKey || profile != host.Settings.SteamProfile)
            host.SetSteamAccount(key, profile);
    }

    /// <summary>How it's going (<c>steam.status</c>), or what's missing before anything can be fetched.</summary>
    internal static string Status(MetricStore? store, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SteamApiKey))
            return "Off: add your key to start.";
        if (settings.SteamRequest() is null)
            return "Off: add your profile to start.";
        return store is not null && store.TryGet(SteamFriendsProvider.Status, out var status) && status.Text is { } text ? text : "Connecting to Steam";
    }

    public override void Refresh()
    {
        if (_status is not null)
            _status.Description = Status(host.Store, host.Settings);
    }

    /// <summary>Whatever's been typed and not yet taken is taken now.</summary>
    public override void Left() => Commit();
}

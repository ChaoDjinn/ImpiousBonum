using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.App.Tests;

/// <summary>The Steam friends widget, and the Steam page with the key and profile it needs.</summary>
public sealed class SteamFriendsWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    private static readonly SteamFriend Sam = new("Sam", SteamPresence.Online, "Hades");
    private static readonly SteamFriend Priya = new("Priya", SteamPresence.Online, null);
    private static readonly SteamFriend Alex = new("Alex", SteamPresence.Away, null);
    private static readonly SteamFriend Jo = new("Jo", SteamPresence.Busy, "Celeste");

    [Fact]
    public void Each_friend_online_has_a_row_with_their_game_or_whether_they_re_around()
    {
        Sta.Run(() =>
        {
            var store = Store(Sam, Jo, Priya, Alex);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "friends" }, Plain);
            widget.Refresh(store, DateTime.Now);

            var rows = Rows(widget);
            Assert.Equal(["Sam", "Jo", "Priya", "Alex"], rows.Select(r => Texts(r)[0]));
            Assert.Equal(["Hades", "Celeste", "Online", "Away"], rows.Select(r => Texts(r)[1]));
            // Games in the accent colour; those away or busy faded.
            Assert.Equal([Plain.Accent, Plain.Accent, Plain.Secondary, Plain.Secondary], rows.Select(r => TextBlocks(r)[1].Foreground));
            Assert.Equal([1, FriendsWidget.AwayOpacity, 1, FriendsWidget.AwayOpacity], rows.Select(r => r.Opacity));
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Fact]
    public void Friends_can_be_just_those_playing_or_those_around_and_none_hides_the_widget()
    {
        Sta.Run(() =>
        {
            var store = Store(Sam, Jo, Priya, Alex);
            var playing = WidgetFactory.Create(new JsonObject { ["type"] = "friends", ["playingOnly"] = true }, Plain);
            playing.Refresh(store, DateTime.Now);
            Assert.Equal(["Sam", "Jo"], Rows(playing).Select(r => Texts(r)[0]));

            var around = WidgetFactory.Create(new JsonObject { ["type"] = "friends", ["showAway"] = false }, Plain);
            around.Refresh(store, DateTime.Now);
            Assert.Equal(["Sam", "Priya"], Rows(around).Select(r => Texts(r)[0]));

            SteamFriendsProvider.Publish(store, [], null, "No friends online.");
            around.Refresh(store, DateTime.Now);
            Assert.Empty(Rows(around));
            Assert.Equal(Visibility.Collapsed, around.Visibility);
            around.Editing = true;
            Assert.Equal(Visibility.Visible, around.Visibility);
        });
    }

    [Fact]
    public void Steam_starts_off_and_the_page_keeps_the_key_and_profile_typed()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", JsonDefaults.Options)!;
        Assert.Null(settings.SteamApiKey);
        Assert.Null(settings.SteamRequest());

        Sta.Run(() =>
        {
            var (page, _) = Page(settings);
            Assert.Equal("Off: add your key to start.", Card(page, "Now").Description);

            KeyBox(page).Password = "  SECRETKEY  ";
            Box(page, "Your profile").Text = "not a profile!";
            page.Commit();
            Assert.Equal(SteamPage.NotAProfile, Card(page, "Your profile").Description);
            Assert.Null(settings.SteamApiKey);

            Box(page, "Your profile").Text = "https://steamcommunity.com/id/me/";
            page.Commit();
            Assert.Equal(SteamPage.ProfileNote, Card(page, "Your profile").Description);
            Assert.Equal("SECRETKEY", settings.SteamApiKey);
            Assert.Equal("https://steamcommunity.com/id/me/", settings.SteamProfile);
            Assert.Equal(new SteamAccount("SECRETKEY", "me"), settings.SteamRequest());

            // Emptied: off again.
            KeyBox(page).Password = "";
            page.Commit();
            Assert.Null(settings.SteamApiKey);
            Assert.Null(settings.SteamRequest());
        });

        var store = new MetricStore();
        new SteamFriendsProvider(() => null).Initialize(store);
        var on = new AppSettings { SteamApiKey = "k", SteamProfile = "me" };
        Assert.Equal("Connecting to Steam", SteamPage.Status(store, on));
        SteamFriendsProvider.Publish(store, [Sam], null, "1 friend online, 1 playing.");
        Assert.Equal("1 friend online, 1 playing.", SteamPage.Status(store, on));
        Assert.Equal("Off: add your profile to start.", SteamPage.Status(store, new AppSettings { SteamApiKey = "k" }));
    }

    private static MetricStore Store(params SteamFriend[] friends)
    {
        var store = new MetricStore();
        using var provider = new SteamFriendsProvider(() => null);
        provider.Initialize(store);
        SteamFriendsProvider.Publish(store, friends, null, null);
        return store;
    }

    private static List<FrameworkElement> Rows(Widget widget) =>
        ((StackPanel)((ShrinkToFit)widget.Children[0]).Child).Children.Cast<FrameworkElement>().ToList();

    private static List<TextBlock> TextBlocks(FrameworkElement row) => ((Grid)((ShrinkToFit)row).Child).Children.OfType<TextBlock>().ToList();

    private static List<string> Texts(FrameworkElement row) => TextBlocks(row).Select(t => t.Text).ToList();

    private static (SteamPage Page, AppSettings Settings) Page(AppSettings settings)
    {
        var host = DispatchProxy.Create<ISettingsHost, SteamHost>();
        ((SteamHost)(object)host).Settings = settings;
        var page = new SteamPage(host);
        page.Load();
        return (page, settings);
    }

    /// <summary>A settings host that keeps the Steam settings in its settings, as the app does.</summary>
    public class SteamHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case nameof(ISettingsHost.SetSteamAccount):
                    Settings.SteamApiKey = (string?)args![0];
                    Settings.SteamProfile = (string?)args![1];
                    return null;
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static PasswordBox KeyBox(DependencyObject page) => Logical<PasswordBox>(page).Single(b => AutomationProperties.GetName(b) == "Web API key");

    private static TextBox Box(DependencyObject page, string name) => Logical<TextBox>(page).Single(b => AutomationProperties.GetName(b) == name);

    private static SettingsCard Card(DependencyObject page, string header) => Logical<SettingsCard>(page).Single(c => c.Header == header);

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));
}

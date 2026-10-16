using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.StatusChecks;

namespace ImpiousBonum.App.Tests;

// The page saves the settings, so it shares AppPaths (a static) with the other tests that do.
[Collection("AppPaths")]
public sealed class StatusChecksTests : IDisposable
{
    private static readonly DateTime Evening = new(2026, 10, 16, 21, 47, 0);

    private static readonly StatusCheck Nas = new("NAS", "nas.local");

    /// <summary>As the provider gives them: two up, one down, and one not checked yet.</summary>
    private static readonly CheckStatus[] Checks =
    [
        new("Router", true, 2, "12 days", null),
        new("NAS", true, 4, "3 h", null),
        new("Plex", false, null, "12 min", "No answer"),
        new("Pi-hole", null, null, null, null),
    ];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public StatusChecksTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // ---- The widget ---------------------------------------------------------------------------------

    [Fact]
    public void The_widget_shows_all_the_checks_or_the_ones_chosen_in_order()
    {
        Assert.Equal(Checks, StatusWidget.Pick(Checks, PickList.Everything));
        // Whatever their case; one that's gone is left out.
        Assert.Equal(["Plex", "Router"], StatusWidget.Pick(Checks, PickList.Parse("plex, Gone, ROUTER", allowAll: true)).Select(s => s.Name));
        Assert.Empty(StatusWidget.Pick(null, PickList.Everything));
    }

    [Fact]
    public void Each_check_says_how_fast_it_answered_or_that_its_down()
    {
        Assert.Equal("2 ms", StatusWidget.Value(Checks[0], time: true, since: false));
        Assert.Equal("2 ms · 12 days", StatusWidget.Value(Checks[0], time: true, since: true));
        Assert.Equal("12 days", StatusWidget.Value(Checks[0], time: false, since: true));
        Assert.Equal(string.Empty, StatusWidget.Value(Checks[0], time: false, since: false));
        // Down says so in words as well as colour, whatever's shown.
        Assert.Equal("Down", StatusWidget.Value(Checks[2], time: false, since: false));
        Assert.Equal("Down · 12 min", StatusWidget.Value(Checks[2], time: true, since: true));
        Assert.Equal("…", StatusWidget.Value(Checks[3], time: true, since: true));
    }

    [Fact]
    public void The_widget_draws_a_dot_for_each_in_the_up_or_down_colour()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var store = new MetricStore();
            store.SetAttachment(StatusProvider.Checks, (IReadOnlyList<CheckStatus>)Checks);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "status", ["columns"] = 2 }, theme);
            widget.Refresh(store, Evening);

            var grid = Logical<UniformGrid>(widget).Single();
            Assert.Equal(2, grid.Columns);
            var rows = grid.Children.OfType<Grid>().ToList();
            Assert.Equal([["Router", "2 ms"], ["NAS", "4 ms"], ["Plex", "Down"], ["Pi-hole", "…"]], rows.Select(Texts));
            Assert.Same(theme.Accent, Dot(rows[0]).Fill);
            Assert.Same(theme.Critical, Dot(rows[2]).Fill);
            Assert.Same(theme.Critical, Logical<TextBlock>(rows[2]).Last().Foreground);
            // Not checked yet: a ring rather than a dot.
            Assert.Null(Dot(rows[3]).Fill);
            Assert.NotNull(Dot(rows[3]).Stroke);

            // Nothing on show changed, so nothing was built afresh.
            widget.Refresh(store, Evening.AddSeconds(1));
            Assert.Same(rows[0], grid.Children[0]);

            // Its own colours.
            var own = WidgetFactory.Create(new JsonObject { ["type"] = "status", ["upColor"] = "#00FF00", ["downColor"] = "warning" }, theme);
            own.Refresh(store, Evening);
            var ownRows = Logical<UniformGrid>(own).Single().Children.OfType<Grid>().ToList();
            Assert.Equal("#FF00FF00", Dot(ownRows[0]).Fill.ToString());
            Assert.Same(theme.Warning, Dot(ownRows[2]).Fill);

            // Without any checks, it says so, across the whole widget.
            widget.Refresh(new MetricStore(), Evening);
            Assert.Equal(StatusWidget.Empty, Assert.IsType<TextBlock>(Assert.Single(grid.Children)).Text);
            Assert.Equal(1, grid.Columns);
        });
    }

    // ---- The check window ---------------------------------------------------------------------------

    [Fact]
    public void The_check_window_builds_a_ping_or_a_web_check_and_round_trips_one()
    {
        Sta.Run(() =>
        {
            var window = new StatusCheckWindow(null, ["Router"]);
            window.NameBox.Text = " NAS ";
            window.AddressBox.Text = "nas.local";
            window.ExpectBox.Text = "Synology";
            window.AnyCertificateCheck.IsChecked = true;
            // A ping has no page to say anything, nor a certificate.
            Assert.Equal(Nas, window.Build(out var problem));
            Assert.Null(problem);
            Assert.False(window.ExpectBox.IsEnabled);
            Assert.False(window.AnyCertificateCheck.IsEnabled);

            window.AddressBox.Text = "https://nas.local:5001";
            var web = window.Build(out _)!;
            Assert.Equal(new StatusCheck("NAS", "https://nas.local:5001", "Synology", 60, AnyCertificate: true), web);
            Assert.True(window.ExpectBox.IsEnabled);
            Assert.True(window.AnyCertificateCheck.IsEnabled);
            window.Close();

            var again = new StatusCheckWindow(web with { Every = 300 }, []);
            Assert.Equal(web with { Every = 300 }, again.Build(out _));
            again.Close();
        });
    }

    [Fact]
    public void The_check_window_says_what_is_missing()
    {
        Sta.Run(() =>
        {
            var window = new StatusCheckWindow(null, ["Pi-hole"]);
            Assert.False(window.OkButton.IsEnabled);
            window.NameBox.Text = "pi-hole";
            window.AddressBox.Text = "pi.hole";
            Assert.Null(window.Build(out var problem));
            Assert.Equal("There's already a check called \"pi-hole\".", problem);

            // A different name with the same readings.
            window.NameBox.Text = "Pi hole";
            Assert.Null(window.Build(out problem));
            Assert.Contains("status.pi-hole.up", problem);

            window.NameBox.Text = "DNS";
            window.EveryBox.Text = "5";
            Assert.Null(window.Build(out problem));
            Assert.Contains("every 10 seconds", problem);
            window.EveryBox.Text = "soon";
            Assert.Null(window.Build(out problem));
            Assert.Contains("every 10 seconds", problem);
            Assert.False(window.OkButton.IsEnabled);

            window.EveryBox.Text = "30";
            Assert.NotNull(window.Build(out problem));
            Assert.True(window.OkButton.IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public void Check_now_says_what_it_found() =>
        Assert.Equal(["Up: it answered in 4 ms", "Down: No answer"],
            [StatusCheckWindow.Describe(CheckResult.Answered(4)), StatusCheckWindow.Describe(CheckResult.Down("No answer"))]);

    // ---- The page -----------------------------------------------------------------------------------

    [Fact]
    public void The_page_adds_changes_and_removes_checks_and_sets_up_alerts()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings();
            var host = DispatchProxy.Create<ISettingsHost, ChecksHost>();
            var fake = (ChecksHost)(object)host;
            fake.Settings = settings;
            var page = new StatusChecksPage(host);
            page.Load();

            // It says what's contacted before anything is.
            Assert.Contains(LogicalTexts(page), text => text.Contains("only the addresses you add here are contacted", StringComparison.Ordinal));
            Assert.Contains("No checks yet. Add one to see whether it's up.", LogicalTexts(page));

            StatusCheck? answer = Nas;
            IReadOnlyCollection<string>? others = null;
            page.AskCheck = (_, _, names) =>
            {
                others = names;
                return answer;
            };
            Click(Button(page, "Add…"));
            Assert.Equal([Nas], settings.StatusChecks);
            Assert.Equal([Nas], AppSettings.Load().StatusChecks);
            Assert.Empty(others!);

            // Its card says what's checked, and how it is now.
            var card = Card(page, "NAS");
            Assert.Equal("Ping nas.local every 60 seconds", card.Description);
            var store = new MetricStore();
            store.SetAttachment(StatusProvider.Checks, (IReadOnlyList<CheckStatus>)Checks);
            fake.Store = store;
            page.Refresh();
            Assert.Equal("Ping nas.local every 60 seconds · Up for 3 h, answering in 4 ms", card.Description);
            Assert.Equal("http://plex.local:32400/web every 60 seconds, which must say \"Plex\" · Down for 12 min: No answer",
                page.Describe(new StatusCheck("Plex", "http://plex.local:32400/web", "Plex")));
            Assert.Equal("Ping pi.hole every 60 seconds · Checking…", page.Describe(new StatusCheck("Pi-hole", "pi.hole")));
            Assert.StartsWith("Not checked: ", page.Describe(new StatusCheck("Pi-hole", "ftp://pi.hole")));

            // Changed in place; the others' names are the ones it can't have.
            var router = new StatusCheck("Router", "192.168.1.1");
            answer = router;
            Click(Button(page, "Add…"));
            answer = Nas with { Address = "192.168.1.20" };
            Click(Button(page, "Edit NAS"));
            Assert.Equal(["Router"], others!);
            Assert.Equal([answer, router], settings.StatusChecks);

            // An alert as soon as it's down: it only is after failing twice.
            AlertRule? offered = null;
            page.AskAlert = (_, rule) => offered = rule;
            Click(Button(page, "Alert me when NAS goes down"));
            Assert.Equal("status.nas.up below 1", offered!.Describe());
            Assert.Equal("NAS is down", offered.Message);
            Assert.True(offered.IsUsable);
            Assert.Equal([offered], settings.Alerts);
            Assert.Equal(["Added alert: status.nas.up below 1"], fake.Alerts);

            Click(Button(page, "Remove NAS"));
            Assert.Equal([router], settings.StatusChecks);
            Assert.Equal([router], AppSettings.Load().StatusChecks);
        });
    }

    [Fact]
    public void Settings_keep_the_checks_and_drop_broken_ones()
    {
        AppPaths.UseDataDirectory(_directory);
        File.WriteAllText(AppPaths.SettingsFile, """
            {
              "statusChecks": [
                { "name": "NAS", "address": "nas.local" },
                { "name": "Plex", "address": "http://plex.local:32400/web", "expect": "Plex", "every": 30 },
                { "name": "No address" },
                null,
              ],
            }
            """);
        var settings = AppSettings.Load();
        Assert.Equal([Nas, new StatusCheck("Plex", "http://plex.local:32400/web", "Plex", 30)], settings.StatusChecks);
        Assert.Empty(new AppSettings().StatusChecks);

        settings.Save();
        Assert.Contains("\"statusChecks\"", File.ReadAllText(AppPaths.SettingsFile));
        Assert.Equal(settings.StatusChecks, AppSettings.Load().StatusChecks);
    }

    /// <summary>A settings host that has the settings and a store to read, and notes the alerts it's told about.</summary>
    public class ChecksHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        public MetricStore? Store { get; set; }

        public List<string> Alerts { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case "get_Store":
                    return Store;
                case nameof(ISettingsHost.AlertsChanged):
                    Alerts.Add((string)args![0]!);
                    return null;
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static System.Windows.Shapes.Ellipse Dot(Grid row) => row.Children.OfType<System.Windows.Shapes.Ellipse>().Single();

    private static List<string> Texts(Grid row) => [.. Logical<TextBlock>(row).Select(t => t.Text)];

    private static SettingsCard Card(DependencyObject page, string header) => Logical<SettingsCard>(page).Single(c => c.Header == header);

    private static Button Button(DependencyObject page, string name) => Logical<Button>(page).First(b => AutomationProperties.GetName(b) == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Logical<T>(DependencyObject? root) where T : DependencyObject =>
        root is null ? [] : LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));

    /// <summary>Text in a page that hasn't been shown, so has no visual tree yet.</summary>
    private static List<string> LogicalTexts(DependencyObject root) => [.. Logical<TextBlock>(root).Select(t => t.Text)];
}

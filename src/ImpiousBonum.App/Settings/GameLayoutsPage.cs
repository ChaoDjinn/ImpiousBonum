using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>Game layouts: games linked to a layout or theme that shows while they're in front, and session recaps.</summary>
internal sealed class GameLayoutsPage(ISettingsHost host) : CardPage("Game layouts",
    "Link a game to a layout or a theme, and the main dashboard shows it a few seconds after the game comes to the front, going back when the game's gone.")
{
    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        Add(Ui.Card("", "Switch automatically", "Off keeps the links for later",
            Ui.Switch(Settings.GameLayoutsEnabled, "Switch automatically", host.SetGameLayoutsEnabled)));
        Add(Ui.Card("", "Recap each session", "After playing a linked game: a notification with its FPS, loads and temperatures, and a line in sessions.csv",
            Ui.Switch(Settings.SessionRecaps, "Recap each session", host.SetSessionRecaps)));

        Add(Ui.Heading("Linked games"));
        if (Settings.GameLayouts.Count == 0)
            Add(Ui.Note("No games linked yet. Link one below."));
        foreach (var rule in Settings.GameLayouts)
        {
            Add(Ui.Card("", rule.Process, rule.Layout is null ? $"Restyles the dashboard with the {rule.Describe()}" : $"Shows {rule.Describe()}",
                Ui.Button("Remove", () =>
                {
                    host.UnlinkGame(rule);
                    Load();
                })));
        }

        Add(Ui.Heading("Link a game"));
        var app = new ComboBox { IsEditable = true, Width = 200, ItemsSource = host.RecentApps, Text = host.RecentApps.FirstOrDefault() ?? string.Empty };
        AutomationProperties.SetName(app, "Game");
        var layouts = host.Layouts.List().Select(l => ((string?)l, (string?)null, l));
        var themes = host.Layouts.Themes.List().Select(t => ((string?)null, (string?)t, $"Theme only: {t}"));
        var targets = layouts.Concat(themes).ToList();
        var target = new ComboBox { Width = 220, ItemsSource = targets.Select(t => t.Item3).ToList(), SelectedIndex = targets.Count > 0 ? 0 : -1 };
        AutomationProperties.SetName(target, "Show");
        var link = Ui.Button("Link", () =>
        {
            var process = app.Text.Trim();
            if (process.Length == 0 || target.SelectedIndex < 0)
                return;
            var (layout, theme, _) = targets[target.SelectedIndex];
            host.LinkGame(new GameLayoutRule(GameLayoutRule.Normalize(process), layout, theme));
            Load();
        }, accent: true);
        Add(Ui.Card("", "Game", "Its name as Windows knows it, without .exe. Games you've had in front lately are in the list.",
            Ui.Row(app, target, link)));
        Add(Ui.Note("A theme only restyles whichever layout is showing. The schedule fills in what a game's link doesn't set."));
    }
}

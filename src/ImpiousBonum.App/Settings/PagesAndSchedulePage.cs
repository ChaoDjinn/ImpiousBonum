using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>Pages and schedule: swiping and turning between the main dashboard's layouts, and what shows when.</summary>
internal sealed class PagesAndSchedulePage(ISettingsHost host) : CardPage("Pages and schedule",
    "Swipe between the main dashboard's layouts or let them take turns, and show a layout or theme at certain times or dates, after sunset, or with Windows in dark or light mode.")
{
    internal static readonly int[] RotateChoices = [0, 15, 30, 60, 300];

    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        Add(Ui.Heading("Pages"));
        Add(Ui.Card("", "Swipe between pages", "Swipe across the dashboard to move to the next or previous page",
            Ui.Switch(Settings.SwipePages, "Swipe between pages", swipe => SetPages(swipe, Settings.RotatePagesSeconds, Settings.Pages))));
        Add(Ui.Card("", "Turn pages by themselves",
            "Not while you're editing, while a game's layout shows, or for a minute after someone touched the dashboard",
            Ui.Choice(RotateChoices.Select(s => (s, Every(s))), Settings.RotatePagesSeconds, "Turn pages",
                seconds => SetPages(Settings.SwipePages, seconds, Settings.Pages))));

        var layouts = host.Layouts.List();
        var pages = Pages.Of(layouts, Settings.Pages);
        var choices = new WrapPanel();
        foreach (var layout in layouts)
        {
            var box = new CheckBox { Content = layout, IsChecked = pages.Contains(layout, StringComparer.OrdinalIgnoreCase), Margin = new Thickness(0, 4, 24, 4) };
            box.Click += (_, _) =>
            {
                var chosen = choices.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
                // Every layout picked is the same as none: all of them, including ones added later.
                SetPages(Settings.SwipePages, Settings.RotatePagesSeconds, chosen.Count == layouts.Count ? [] : chosen);
            };
            choices.Children.Add(box);
        }
        Add(Ui.Card("", "The pages", "The layouts that are pages. All of them, unless you pick some.", detail: choices));

        Add(Ui.Heading("Schedule"));
        Add(Ui.Card("", "Follow the schedule", "Show a layout or theme, or dim the dashboard, at the times below. Off keeps the times for later.",
            Ui.Switch(Settings.ScheduleEnabled, "Follow the schedule", on =>
            {
                host.SetScheduleEnabled(on);
                Load();
            })));

        var active = ScheduledLook.Active(Settings.Schedule, ScheduleNow.Current(Settings.WeatherLocation));
        foreach (var rule in Settings.Schedule)
        {
            var now = Settings.ScheduleEnabled && active.Contains(rule);
            Add(Ui.Card(now ? "" : null, rule.Describe(), now ? "Showing now" : Why(rule),
                Ui.Button("Remove", () =>
                {
                    host.RemoveScheduleRule(rule);
                    Load();
                })));
        }
        Add(Ui.Card("", "Add to the schedule", Settings.Schedule.Count == 0 ? "Nothing's scheduled yet" : null,
            Ui.Button("Add…", () =>
            {
                if (Window.GetWindow(this) is { } owner)
                    host.AddScheduleRule(owner);
                Load();
            })));
    }

    /// <summary>Why a rule that never applies doesn't, or null.</summary>
    private string? Why(ScheduleRule rule) => rule.FollowsSun && Settings.WeatherLocation is null
        ? "Never applies yet: sunset and sunrise come from the weather's location, and there isn't one. Set it on the Weather page."
        : null;

    private void SetPages(bool swipe, int seconds, IReadOnlyList<string> pages)
    {
        host.SetPages(swipe, seconds, pages);
        Load();
    }

    internal static string Every(int seconds) => seconds switch
    {
        0 => "Don't turn them",
        < 60 => $"Every {seconds} seconds",
        60 => "Every minute",
        _ => $"Every {seconds / 60} minutes",
    };
}

using System.Windows;

namespace ImpiousBonum.App.Shell;

/// <summary>Adds one rule to the schedule: when (between two times, or with Windows in dark or light mode) and what to show.</summary>
public partial class ScheduleRuleWindow : Window
{
    private const string Keep = "(keep the one showing)";

    private static readonly (string Label, int? Brightness)[] Levels =
    [
        ("Full (no change)", null), ("75%", 75), ("50%", 50), ("30%", 30), ("15%", 15), ("Black: tap to peek", 0),
    ];

    internal ScheduleRuleWindow(IReadOnlyList<string> layouts, IReadOnlyList<string> themes, string? suggestedTheme)
    {
        InitializeComponent();
        LayoutBox.ItemsSource = layouts.Prepend(Keep).ToList();
        LayoutBox.SelectedIndex = 0;
        ThemeBox.ItemsSource = themes.Prepend(Keep).ToList();
        ThemeBox.SelectedItem = suggestedTheme is not null && themes.Contains(suggestedTheme) ? suggestedTheme : Keep;
        BrightnessBox.ItemsSource = Levels.Select(l => l.Label).ToList();
        BrightnessBox.SelectedIndex = 0;
        Update();
    }

    /// <param name="suggestedTheme">Picked to start with, if it exists (Night Watch suits a night-time rule).</param>
    /// <returns>The new rule, or null if cancelled.</returns>
    public static ScheduleRule? Ask(IReadOnlyList<string> layouts, IReadOnlyList<string> themes, string? suggestedTheme)
    {
        var window = new ScheduleRuleWindow(layouts, themes, suggestedTheme);
        return window.ShowDialog() == true ? window.Rule() : null;
    }

    internal ScheduleRule Rule()
    {
        var layout = LayoutBox.SelectedItem as string is { } l && l != Keep ? l : null;
        var theme = ThemeBox.SelectedItem as string is { } t && t != Keep ? t : null;
        if (Between.IsChecked == true)
        {
            // Written back the way settings.json documents it, e.g. "7:00" becomes "07:00".
            return new ScheduleRule(Format(FromBox.Text), Format(ToBox.Text), null, layout, theme, Brightness);
        }
        return new ScheduleRule(null, null, Dark.IsChecked == true ? ScheduleRule.Dark : ScheduleRule.Light, layout, theme, Brightness);
    }

    private int? Brightness => BrightnessBox.SelectedIndex >= 0 ? Levels[BrightnessBox.SelectedIndex].Brightness : null;

    private static string? Format(string text) => ScheduleRule.ParseTime(text)?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    internal string? Problem()
    {
        if (Between.IsChecked == true)
        {
            if (ScheduleRule.ParseTime(FromBox.Text) is not { } from || ScheduleRule.ParseTime(ToBox.Text) is not { } to)
                return "Write times like 22:00 or 7:30, on a 24-hour clock.";
            if (from == to)
                return "The start and end can't be the same time.";
        }
        if (LayoutBox.SelectedItem as string == Keep && ThemeBox.SelectedItem as string == Keep && Brightness is null)
            return "Pick a layout, a theme or a brightness.";
        return null;
    }

    private void Update()
    {
        // Checked and TextChanged fire while the window is still being built, before every control exists;
        // the constructor calls this once it's ready.
        if (!IsInitialized)
            return;
        FromBox.IsEnabled = ToBox.IsEnabled = Between.IsChecked == true;
        var problem = Problem();
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Problem() is null)
            DialogResult = true;
    }
}

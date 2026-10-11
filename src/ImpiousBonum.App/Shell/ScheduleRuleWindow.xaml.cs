using System.Windows;

namespace ImpiousBonum.App.Shell;

/// <summary>Adds one rule to the schedule: when (between two times, or with Windows in dark or light mode) and what to show.</summary>
public partial class ScheduleRuleWindow : Window
{
    private const string Keep = "(keep the one showing)";

    internal ScheduleRuleWindow(IReadOnlyList<string> layouts, IReadOnlyList<string> themes, string? suggestedTheme)
    {
        InitializeComponent();
        LayoutBox.ItemsSource = layouts.Prepend(Keep).ToList();
        LayoutBox.SelectedIndex = 0;
        ThemeBox.ItemsSource = themes.Prepend(Keep).ToList();
        ThemeBox.SelectedItem = suggestedTheme is not null && themes.Contains(suggestedTheme) ? suggestedTheme : Keep;
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
            return new ScheduleRule(Format(FromBox.Text), Format(ToBox.Text), null, layout, theme);
        }
        return new ScheduleRule(null, null, Dark.IsChecked == true ? ScheduleRule.Dark : ScheduleRule.Light, layout, theme);
    }

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
        if (LayoutBox.SelectedItem as string == Keep && ThemeBox.SelectedItem as string == Keep)
            return "Pick a layout, a theme, or both.";
        return null;
    }

    private void Update()
    {
        if (OkButton is null)
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

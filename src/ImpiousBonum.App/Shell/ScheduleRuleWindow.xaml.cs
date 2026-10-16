using System.Globalization;
using System.Windows;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Adds one rule to the schedule: when (between two times, from sunset to sunrise or the other way round, with Windows
/// in dark or light mode, or all day), on which dates if only some, and what to show.
/// </summary>
public partial class ScheduleRuleWindow : Window
{
    private const string Keep = "(keep the one showing)";

    private static readonly (string Label, int? Brightness)[] Levels =
    [
        ("Full (no change)", null), ("75%", 75), ("50%", 50), ("30%", 30), ("15%", 15), ("Black: tap to peek", 0),
    ];

    private readonly bool _hasLocation;

    /// <param name="hasLocation">The weather has a location, so sunset and sunrise are known.</param>
    internal ScheduleRuleWindow(IReadOnlyList<string> layouts, IReadOnlyList<string> themes, string? suggestedTheme, bool hasLocation)
    {
        _hasLocation = hasLocation;
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
    public static ScheduleRule? Ask(Window? owner, IReadOnlyList<string> layouts, IReadOnlyList<string> themes, string? suggestedTheme, bool hasLocation)
    {
        var window = new ScheduleRuleWindow(layouts, themes, suggestedTheme, hasLocation);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true ? window.Rule() : null;
    }

    internal ScheduleRule Rule()
    {
        var layout = LayoutBox.SelectedItem as string is { } l && l != Keep ? l : null;
        var theme = ThemeBox.SelectedItem as string is { } t && t != Keep ? t : null;
        var (fromDate, toDate) = OnDates.IsChecked == true ? (FormatDate(FromDateBox.Text), FormatDate(ToDateBox.Text)) : (null, null);
        if (Between.IsChecked == true)
        {
            // Written back the way settings.json documents it, e.g. "7:00" becomes "07:00".
            return new ScheduleRule(Format(FromBox.Text), Format(ToBox.Text), null, layout, theme, Brightness, fromDate, toDate);
        }
        var when = Night.IsChecked == true ? ScheduleRule.Night
            : Daytime.IsChecked == true ? ScheduleRule.Day
            : Dark.IsChecked == true ? ScheduleRule.Dark
            : Light.IsChecked == true ? ScheduleRule.Light
            : null;
        return new ScheduleRule(null, null, when, layout, theme, Brightness, fromDate, toDate);
    }

    private int? Brightness => BrightnessBox.SelectedIndex >= 0 ? Levels[BrightnessBox.SelectedIndex].Brightness : null;

    private static string? Format(string text) => ScheduleRule.ParseTime(text)?.ToString("HH:mm", CultureInfo.InvariantCulture);

    // "1-6" becomes "01-06", as settings.json documents it.
    private static string? FormatDate(string text) => ScheduleRule.ParseDate(text)?.ToString("MM-dd", CultureInfo.InvariantCulture);

    internal string? Problem()
    {
        if (Between.IsChecked == true)
        {
            if (ScheduleRule.ParseTime(FromBox.Text) is not { } from || ScheduleRule.ParseTime(ToBox.Text) is not { } to)
                return "Write times like 22:00 or 7:30, on a 24-hour clock.";
            if (from == to)
                return "The start and end can't be the same time.";
        }
        if (OnDates.IsChecked == true && (ScheduleRule.ParseDate(FromDateBox.Text) is null || ScheduleRule.ParseDate(ToDateBox.Text) is null))
            return "Write dates as the month then the day, like 12-25 for Christmas Day.";
        if (AllDay.IsChecked == true && OnDates.IsChecked != true)
            return "All day needs dates: tick Only from and pick them.";
        if (LayoutBox.SelectedItem as string == Keep && ThemeBox.SelectedItem as string == Keep && Brightness is null)
            return "Pick a layout, a theme or a brightness.";
        return null;
    }

    /// <summary>"Every year from 24 Oct to 1 Nov", so a date written day first shows up as the wrong one before it's added.</summary>
    internal string DescribeDates()
    {
        if (ScheduleRule.ParseDate(FromDateBox.Text) is not { } from || ScheduleRule.ParseDate(ToDateBox.Text) is not { } to)
            return "The month then the day, like 10-31 for Halloween.";
        return from == to
            ? $"Every year on {ScheduleRule.DescribeDate(from)}."
            : $"Every year from {ScheduleRule.DescribeDate(from)} to {ScheduleRule.DescribeDate(to)}, both days included.";
    }

    private void Update()
    {
        // Checked and TextChanged fire while the window is still being built, before every control exists;
        // the constructor calls this once it's ready.
        if (!IsInitialized)
            return;
        FromBox.IsEnabled = ToBox.IsEnabled = Between.IsChecked == true;
        FromDateBox.IsEnabled = ToDateBox.IsEnabled = OnDates.IsChecked == true;
        DatesNote.Text = DescribeDates();
        SunNote.Visibility = !_hasLocation && (Night.IsChecked == true || Daytime.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
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

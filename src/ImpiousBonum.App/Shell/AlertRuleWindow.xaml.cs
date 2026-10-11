using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Shell;

/// <summary>Sets up one alert: which metric, the limit, for how long, and what happens.</summary>
public partial class AlertRuleWindow : Window
{
    private readonly MetricStore? _store;

    internal AlertRuleWindow(AlertRule? rule, MetricStore? store)
    {
        InitializeComponent();
        _store = store;
        MetricBox.ItemsSource = store?.Definitions.Where(d => d.Unit != MetricUnit.Text).Select(d => d.Id).ToList() ?? [];
        MetricBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        if (rule is not null)
        {
            MetricBox.Text = rule.Metric;
            AboveCheck.IsChecked = rule.Above is not null;
            AboveBox.Text = rule.Above?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            BelowCheck.IsChecked = rule.Below is not null;
            BelowBox.Text = rule.Below?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            ForBox.Text = rule.For.ToString(CultureInfo.CurrentCulture);
            MessageBox.Text = rule.Message ?? string.Empty;
            NotifyCheck.IsChecked = rule.Notify;
            FlashCheck.IsChecked = rule.Flash;
        }
        else
        {
            AboveCheck.IsChecked = true;
        }
        Update();
    }

    /// <returns>The alert as saved, or null if cancelled.</returns>
    public static AlertRule? Ask(Window? owner, AlertRule? rule, MetricStore? store)
    {
        var window = new AlertRuleWindow(rule, store) { Owner = owner };
        if (owner is null)
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true ? window.Build(out _) : null;
    }

    /// <summary>The alert as filled in, or null with <paramref name="problem"/> saying what's wrong.</summary>
    internal AlertRule? Build(out string? problem)
    {
        problem = null;
        var metric = MetricBox.Text.Trim();
        if (metric.Length == 0)
            problem = "Pick the metric to watch.";
        double? above = null, below = null;
        if (problem is null && AboveCheck.IsChecked == true && (above = Number(AboveBox.Text)) is null)
            problem = "Write the upper limit as a number.";
        if (problem is null && BelowCheck.IsChecked == true && (below = Number(BelowBox.Text)) is null)
            problem = "Write the lower limit as a number.";
        if (problem is null && above is null && below is null)
            problem = "Tick Above, Below or both.";
        if (problem is null && (!int.TryParse(ForBox.Text.Trim(), out var seconds) || seconds is < 0 or > 86400))
            problem = "Write how many seconds, from 0.";
        if (problem is null && NotifyCheck.IsChecked != true && FlashCheck.IsChecked != true)
            problem = "Choose a notification, a banner or both.";
        if (problem is not null)
            return null;

        return new AlertRule(metric, above, below, int.Parse(ForBox.Text.Trim(), CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(MessageBox.Text) ? null : MessageBox.Text.Trim(), NotifyCheck.IsChecked == true, FlashCheck.IsChecked == true);
    }

    private static double? Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var n) || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n)
            ? n
            : null;

    private void Update()
    {
        // Checked and TextChanged fire while the window is still being built, before every control exists;
        // the constructor calls this once it's ready.
        if (!IsInitialized)
            return;
        AboveBox.IsEnabled = AboveCheck.IsChecked == true;
        BelowBox.IsEnabled = BelowCheck.IsChecked == true;
        var metric = MetricBox.Text.Trim();
        NowText.Text = metric.Length == 0 ? string.Empty
            : _store?.TryGet(metric, out var sample) == true ? $"{sample.Definition.Name}: now {MetricFormatter.Format(sample)}"
            : "No such metric yet (it may appear once its source is running).";
        Build(out var problem);
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not null)
            DialogResult = true;
    }
}

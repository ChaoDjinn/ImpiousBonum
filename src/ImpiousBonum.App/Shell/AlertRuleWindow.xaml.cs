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

    // Only plays sounds: the window never switches layouts or sends anything.
    private readonly AlertActions _sounds = new(_ => null, _ => { });

    /// <param name="layouts">The layouts an alert can switch to.</param>
    internal AlertRuleWindow(AlertRule? rule, MetricStore? store, IReadOnlyList<string>? layouts = null)
    {
        InitializeComponent();
        _store = store;
        MetricBox.ItemsSource = store?.Definitions.Where(d => d.Unit != MetricUnit.Text).Select(d => d.Id).ToList() ?? [];
        MetricBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        SoundBox.ItemsSource = AlertRule.SystemSounds;
        SoundBox.Text = AlertRule.SystemSounds[0];
        SoundBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        // A layout that's gone stays in the list, so editing the alert doesn't quietly change it.
        LayoutBox.ItemsSource = (layouts ?? []).Concat(rule?.Layout is { } current && !(layouts ?? []).Contains(current, StringComparer.OrdinalIgnoreCase) ? [current] : []).ToList();
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
            SoundCheck.IsChecked = rule.Sound is not null;
            SoundBox.Text = rule.Sound ?? SoundBox.Text;
            LayoutCheck.IsChecked = rule.Layout is not null;
            LayoutBox.SelectedItem = rule.Layout;
            CommandCheck.IsChecked = rule.Command is not null;
            CommandBox.Text = rule.Command ?? string.Empty;
            ArgumentsBox.Text = rule.Arguments ?? string.Empty;
            WebhookCheck.IsChecked = rule.Webhook is not null;
            WebhookBox.Text = rule.Webhook ?? string.Empty;
            WebhookTextCheck.IsChecked = rule.WebhookText;
        }
        else
        {
            AboveCheck.IsChecked = true;
        }
        Update();
    }

    /// <returns>The alert as saved, or null if cancelled.</returns>
    public static AlertRule? Ask(Window? owner, AlertRule? rule, MetricStore? store, IReadOnlyList<string>? layouts = null)
    {
        var window = new AlertRuleWindow(rule, store, layouts) { Owner = owner };
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
        var sound = Chosen(SoundCheck, SoundBox.Text);
        var layout = Chosen(LayoutCheck, LayoutBox.SelectedItem as string);
        var command = Chosen(CommandCheck, CommandBox.Text);
        var webhook = Chosen(WebhookCheck, WebhookBox.Text);
        if (problem is null && SoundCheck.IsChecked == true && sound is null)
            problem = "Pick a sound, or a .wav file.";
        if (problem is null && LayoutCheck.IsChecked == true && layout is null)
            problem = "Pick the layout to switch to.";
        if (problem is null && CommandCheck.IsChecked == true && command is null)
            problem = "Write the program to run.";
        if (problem is null && webhook is not null && AlertWebhook.Address(webhook) is null)
            problem = "Write the web address in full, starting http:// or https://.";
        if (problem is null && WebhookCheck.IsChecked == true && webhook is null)
            problem = "Write the web address to send it to.";
        if (problem is not null)
            return null;

        var rule = new AlertRule(metric, above, below, int.Parse(ForBox.Text.Trim(), CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(MessageBox.Text) ? null : MessageBox.Text.Trim(), NotifyCheck.IsChecked == true, FlashCheck.IsChecked == true,
            sound, layout, command, command is null ? null : Chosen(CommandCheck, ArgumentsBox.Text), webhook, webhook is not null && WebhookTextCheck.IsChecked == true);
        if (rule.Does().Count == 0)
        {
            problem = "Choose at least one thing for it to do.";
            return null;
        }
        return rule;
    }

    /// <summary>The field's text when its box is ticked and it isn't empty.</summary>
    private static string? Chosen(CheckBox check, string? text) => check.IsChecked == true && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

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
        SoundBox.IsEnabled = SoundBrowseButton.IsEnabled = PlayButton.IsEnabled = SoundCheck.IsChecked == true;
        LayoutBox.IsEnabled = LayoutCheck.IsChecked == true;
        CommandBox.IsEnabled = ArgumentsBox.IsEnabled = CommandBrowseButton.IsEnabled = CommandCheck.IsChecked == true;
        WebhookBox.IsEnabled = WebhookTextCheck.IsEnabled = WebhookCheck.IsChecked == true;
        var metric = MetricBox.Text.Trim();
        NowText.Text = metric.Length == 0 ? string.Empty
            : _store?.TryGet(metric, out var sample) == true ? $"{sample.Definition.Name}: now {MetricFormatter.Format(sample)}"
            : "No such metric yet (it may appear once its source is running).";
        Build(out var problem);
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SoundBox.Text) && _sounds.Play(SoundBox.Text) is { } problem)
            ProblemText.Text = problem;
    }

    private void OnBrowseSound(object sender, RoutedEventArgs e)
    {
        if (Browse("Sound", "Sounds (*.wav)|*.wav") is { } path)
            SoundBox.Text = path;
    }

    private void OnBrowseCommand(object sender, RoutedEventArgs e)
    {
        if (Browse("Program to run", "Programs and scripts (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*") is { } path)
            CommandBox.Text = path;
    }

    private string? Browse(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not null)
            DialogResult = true;
    }
}

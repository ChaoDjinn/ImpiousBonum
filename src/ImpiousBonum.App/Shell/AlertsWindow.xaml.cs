using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Alerts…: the alert rules, with add, edit and remove. Changes are saved at once.</summary>
public partial class AlertsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<MetricStore?> _store;

    public AlertsWindow(AppSettings settings, Func<MetricStore?> store)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        Fill();
    }

    private void Fill()
    {
        var selected = Rules.SelectedIndex;
        Rules.Items.Clear();
        foreach (var rule in _settings.Alerts)
        {
            var does = string.Join(" and ", new[] { rule.Notify ? "notify" : null, rule.Flash ? "flash" : null }.OfType<string>());
            Rules.Items.Add(new ListBoxItem { Content = $"{rule.Describe()}: {does}", ToolTip = rule.Message, Tag = rule });
        }
        Rules.SelectedIndex = Math.Min(selected, Rules.Items.Count - 1);
        OnSelectionChanged(this, null!);
    }

    private AlertRule? Selected => (Rules.SelectedItem as ListBoxItem)?.Tag as AlertRule;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EditButton.IsEnabled = RemoveButton.IsEnabled = Selected is not null;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (AlertRuleWindow.Ask(this, null, _store()) is not { } rule)
            return;
        _settings.Alerts = [.. _settings.Alerts, rule];
        Save($"Added alert: {rule.Describe()}");
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } current || AlertRuleWindow.Ask(this, current, _store()) is not { } edited)
            return;
        _settings.Alerts = _settings.Alerts.Select(r => ReferenceEquals(r, current) ? edited : r).ToList();
        Save($"Changed alert: {edited.Describe()}");
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } current)
            return;
        _settings.Alerts = _settings.Alerts.Where(r => !ReferenceEquals(r, current)).ToList();
        Save($"Removed alert: {current.Describe()}");
    }

    private void Save(string log)
    {
        _settings.Save();
        AppLog.Info(log);
        Fill();
    }
}

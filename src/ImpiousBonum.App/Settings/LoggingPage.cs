using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Settings;

/// <summary>Logging: whether to log readings to CSV files, which, how often, and for how long to keep the files. Changes are saved as they're made.</summary>
internal sealed class LoggingPage : UserControl, IPage
{
    internal static readonly (string Label, int Seconds)[] Periods =
        [("Every second", 1), ("Every 5 seconds", 5), ("Every 10 seconds", 10), ("Every 30 seconds", 30), ("Every minute", 60), ("Every 5 minutes", 300)];

    internal static readonly (string Label, int Days)[] Keeps =
        [("A week", 7), ("30 days", 30), ("90 days", 90), ("A year", 365), ("Always", 0)];

    private readonly ISettingsHost? _host;
    private readonly ObservableCollection<string> _metrics = [];
    private readonly ListBox _metricList;
    private readonly Button _add;
    private readonly Button _remove;
    private readonly ComboBox _every;
    private readonly ComboBox _keep;
    private readonly TextBlock _problem;
    private MetricStore? _store;
    private bool _filling;

    internal readonly CheckBox EnabledCheck;

    /// <param name="host">Null to try the page out without an app, as the tests do.</param>
    public LoggingPage(ISettingsHost? host)
    {
        _host = host;
        EnabledCheck = Ui.Switch(false, "Log readings to CSV files", _ => Changed());
        _metricList = new ListBox { Height = 160, ItemsSource = _metrics };
        Controls.Placeholder.SetText(_metricList, "No metrics yet. Add the ones to log.");
        AutomationProperties.SetName(_metricList, "Metrics");
        _metricList.SelectionChanged += (_, _) => UpdateButtons();
        _add = Ui.Button("Add…", AddFromPicker, "");
        _remove = Ui.Button("Remove", RemoveSelected);
        _every = new ComboBox { Width = 200, ItemsSource = Periods.Select(p => p.Label).ToList() };
        _every.SelectionChanged += (_, _) => Changed();
        _keep = new ComboBox { Width = 200, ItemsSource = Keeps.Select(k => k.Label).ToList() };
        _keep.SelectionChanged += (_, _) => Changed();
        _problem = SettingsWindow.Styled(new TextBlock { Margin = new Thickness(2, 8, 0, 0) }, "ProblemText");

        var column = SettingsWindow.Styled(new Controls.PageColumn(), "PageColumn");
        column.Children.Add(SettingsWindow.Styled(new TextBlock { Text = "Logging" }, "PageTitle"));
        column.Children.Add(SettingsWindow.Styled(new TextBlock
        {
            Text = "Log readings to CSV files, one a day, to chart in a spreadsheet later. Each row is one period: every metric's average, lowest and highest over it, read once a second.",
        }, "PageIntro"));
        column.Children.Add(Ui.Card("", "Log readings to CSV files", null, EnabledCheck));
        var metrics = new StackPanel();
        metrics.Children.Add(_metricList);
        var buttons = Ui.Row(_add, _remove);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 8, 0, 0);
        metrics.Children.Add(buttons);
        column.Children.Add(Ui.Card("", "Metrics", "The readings to log", detail: metrics));
        column.Children.Add(Ui.Card("", "How often", "A row in the file for each period", _every));
        column.Children.Add(Ui.Card("", "Keep logs for", "Older files are removed once a day", _keep));
        column.Children.Add(Ui.Card("", "Logs folder", AppPaths.LogsFolder, Ui.Button("Open", () =>
        {
            Directory.CreateDirectory(AppPaths.LogsFolder);
            Ui.Open(AppPaths.LogsFolder);
        })));
        column.Children.Add(_problem);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = column };
    }

    public void Load()
    {
        if (_host is not null)
            Fill(_host.Settings.Logging, _host.Store);
    }

    internal void Fill(LogSettings settings, MetricStore? store)
    {
        _filling = true;
        _store = store;
        _metrics.Clear();
        foreach (var id in settings.Metrics)
            _metrics.Add(id);
        EnabledCheck.IsChecked = settings.Enabled;
        _every.SelectedIndex = Math.Max(0, Array.FindIndex(Periods, p => p.Seconds == settings.EverySeconds));
        _keep.SelectedIndex = Math.Max(0, Array.FindIndex(Keeps, k => k.Days == settings.KeepDays));
        _filling = false;
        Changed(save: false);
    }

    internal LogSettings Settings() => new(
        EnabledCheck.IsChecked == true,
        [.. _metrics],
        Periods[Math.Max(0, _every.SelectedIndex)].Seconds,
        Keeps[Math.Max(0, _keep.SelectedIndex)].Days);

    internal void AddMetric(string id)
    {
        if (id.Length > 0 && !_metrics.Contains(id, StringComparer.OrdinalIgnoreCase))
            _metrics.Add(id);
        Changed();
    }

    internal string? Problem() =>
        EnabledCheck.IsChecked == true && _metrics.Count == 0 ? "Add at least one metric to log." : null;

    private void Changed(bool save = true)
    {
        if (_filling)
            return;
        UpdateButtons();
        var problem = Problem();
        _problem.Text = problem ?? string.Empty;
        if (save && problem is null)
            _host?.SetLogging(Settings());
    }

    private void UpdateButtons()
    {
        _remove.IsEnabled = _metricList.SelectedItem is not null;
        _add.IsEnabled = _store is not null;
    }

    private void AddFromPicker()
    {
        if (_store is null)
            return;
        var picker = new MetricPickerWindow(_store, placeholder: false, null) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.Result is { } id)
            AddMetric(id);
    }

    private void RemoveSelected()
    {
        if (_metricList.SelectedItem is string id)
            _metrics.Remove(id);
        Changed();
    }
}

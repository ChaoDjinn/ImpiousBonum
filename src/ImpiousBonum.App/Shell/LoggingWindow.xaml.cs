using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ImpiousBonum.App.Editor;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Logging…: whether to log, which metrics, how often, and for how long to keep the files.</summary>
public partial class LoggingWindow : Window
{
    internal static readonly (string Label, int Seconds)[] Periods =
        [("Second", 1), ("5 seconds", 5), ("10 seconds", 10), ("30 seconds", 30), ("Minute", 60), ("5 minutes", 300)];

    internal static readonly (string Label, int Days)[] Keeps =
        [("A week", 7), ("30 days", 30), ("90 days", 90), ("A year", 365), ("Always", 0)];

    private readonly MetricStore? _store;
    private readonly ObservableCollection<string> _metrics;

    internal LoggingWindow(LogSettings settings, MetricStore? store)
    {
        InitializeComponent();
        _store = store;
        _metrics = new ObservableCollection<string>(settings.Metrics);
        MetricList.ItemsSource = _metrics;
        EnabledCheck.IsChecked = settings.Enabled;
        EveryBox.ItemsSource = Periods.Select(p => p.Label).ToList();
        EveryBox.SelectedIndex = Math.Max(0, Array.FindIndex(Periods, p => p.Seconds == settings.EverySeconds));
        KeepBox.ItemsSource = Keeps.Select(k => k.Label).ToList();
        KeepBox.SelectedIndex = Math.Max(0, Array.FindIndex(Keeps, k => k.Days == settings.KeepDays));
        Update();
    }

    /// <returns>The new settings, or null if cancelled.</returns>
    public static LogSettings? Ask(LogSettings settings, MetricStore? store)
    {
        var window = new LoggingWindow(settings, store);
        return window.ShowDialog() == true ? window.Settings() : null;
    }

    internal LogSettings Settings() => new(
        EnabledCheck.IsChecked == true,
        [.. _metrics],
        Periods[Math.Max(0, EveryBox.SelectedIndex)].Seconds,
        Keeps[Math.Max(0, KeepBox.SelectedIndex)].Days);

    internal void AddMetric(string id)
    {
        if (id.Length > 0 && !_metrics.Contains(id, StringComparer.OrdinalIgnoreCase))
            _metrics.Add(id);
        Update();
    }

    internal string? Problem() =>
        EnabledCheck.IsChecked == true && _metrics.Count == 0 ? "Add at least one metric to log." : null;

    private void Update()
    {
        // Checked and TextChanged fire while the window is still being built, before every control exists;
        // the constructor calls this once it's ready.
        if (!IsInitialized)
            return;
        RemoveButton.IsEnabled = MetricList.SelectedItem is not null;
        AddButton.IsEnabled = _store is not null;
        var problem = Problem();
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (_store is null)
            return;
        var picker = new MetricPickerWindow(_store, placeholder: false, null) { Owner = this };
        if (picker.ShowDialog() == true && picker.Result is { } id)
            AddMetric(id);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (MetricList.SelectedItem is string id)
            _metrics.Remove(id);
        Update();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        using var _ = Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Problem() is null)
            DialogResult = true;
    }
}

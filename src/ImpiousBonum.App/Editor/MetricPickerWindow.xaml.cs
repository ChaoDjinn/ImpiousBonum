using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Browse and search every metric with its live value, then pick how to show it.
/// For templates the result is a placeholder like <c>{mem.used:N0 MB}</c>; for metric fields it's the bare id.
/// </summary>
public partial class MetricPickerWindow : Window
{
    private readonly MetricStore _store;
    private readonly bool _placeholder;
    private readonly List<MetricRow> _rows;
    private readonly ListCollectionView _view;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string[] _terms = [];

    /// <param name="placeholder">True for template fields (insert <c>{id:spec}</c>), false for metric fields (bare id).</param>
    /// <param name="currentId">Metric to preselect, if any.</param>
    public MetricPickerWindow(MetricStore store, bool placeholder, string? currentId)
    {
        InitializeComponent();
        _store = store;
        _placeholder = placeholder;
        FormatPanel.Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;

        _rows = store.Definitions
            .Select(d => new MetricRow(d))
            // Friendly metrics first; raw hardware sensors (hw/...) after, grouped by device.
            .OrderBy(r => r.IsRawSensor)
            .ThenBy(r => r.Definition.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        RefreshValues();

        _view = new ListCollectionView(_rows) { Filter = Matches };
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricRow.Category)));
        MetricList.ItemsSource = _view;

        if (currentId is not null && _rows.FirstOrDefault(r => r.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase)) is { } current)
        {
            MetricList.SelectedItem = current;
            MetricList.ScrollIntoView(current);
        }

        _timer.Tick += (_, _) =>
        {
            RefreshValues();
            RefreshFormats();
        };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => SearchBox.Focus();
        UpdateResult();
    }

    /// <summary>What to insert: <c>{id}</c>/<c>{id:spec}</c> for templates, or the id for metric fields. Null if cancelled.</summary>
    public string? Result { get; private set; }

    /// <summary>Ways to show a value of each unit, as (format spec, description). Each is previewed with the live reading.</summary>
    public static IReadOnlyList<(string Spec, string Description)> FormatsFor(MetricUnit unit) => unit switch
    {
        MetricUnit.Bytes =>
        [
            ("", "Automatic unit"), ("N0 MB", "Megabytes, whole number"), ("0.0 GB", "Gigabytes"), ("0.00 TB", "Terabytes"), ("nounit", "Number only"),
        ],
        MetricUnit.BytesPerSecond =>
        [
            ("", "Automatic unit"), ("0 KB", "KB/s, whole number"), ("0.0 MB", "MB/s"), ("nounit", "Number only"),
        ],
        MetricUnit.Text => [("", "As is")],
        MetricUnit.None => [("", "As is"), ("0", "Whole number")],
        _ =>
        [
            ("", "With unit"), ("0", "Whole number with unit"), ("nounit", "Number only"), ("0 nounit", "Whole number only"),
        ],
    };

    public static string Placeholder(string id, string spec) => spec.Length == 0 ? $"{{{id}}}" : $"{{{id}:{spec}}}";

    private MetricRow? Selected => MetricList.SelectedItem as MetricRow;

    private FormatOption? SelectedFormat => FormatList.SelectedItem as FormatOption;

    private void RefreshValues()
    {
        foreach (var row in _rows)
            row.Value = _store.TryGet(row.Id, out var sample) ? MetricFormatter.Format(sample) : MetricFormatter.Missing;
    }

    private void RefreshFormats()
    {
        if (!_placeholder || Selected is not { } row || !_store.TryGet(row.Id, out var sample))
            return;

        var selectedSpec = SelectedFormat?.Spec ?? string.Empty;
        var options = FormatsFor(row.Definition.Unit)
            .Select(f => new FormatOption(f.Spec, MetricFormatter.Format(sample, FormatSpec.Parse(f.Spec)), f.Description))
            .ToList();
        FormatList.ItemsSource = options;
        FormatList.SelectedItem = options.FirstOrDefault(o => o.Spec == selectedSpec) ?? options[0];
    }

    private bool Matches(object item)
    {
        if (_terms.Length == 0)
            return true;
        var row = (MetricRow)item;
        var haystack = $"{row.Id} {row.Definition.Name} {row.Category}";
        return _terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _terms = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _view.Refresh();
        if (Selected is null || !Matches(Selected))
            MetricList.SelectedItem = _view.Cast<object>().FirstOrDefault();
    }

    private void OnMetricChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        FormatList.SelectedItem = null;
        RefreshFormats();
        UpdateResult();
    }

    private void OnFormatChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateResult();

    private void UpdateResult()
    {
        var result = Selected is { } row ? (_placeholder ? Placeholder(row.Id, SelectedFormat?.Spec ?? string.Empty) : row.Id) : null;
        ResultText.Text = result ?? string.Empty;
        InsertButton.IsEnabled = result is not null;
    }

    private void OnMetricDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null)
            OnInsert(sender, e);
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        Result = _placeholder ? Placeholder(row.Id, SelectedFormat?.Spec ?? string.Empty) : row.Id;
        DialogResult = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Down from the search box moves into the list, so it's all keyboard-driven.
        if (e.Key == Key.Down && SearchBox.IsKeyboardFocused && _view.Cast<object>().Any())
        {
            MetricList.SelectedIndex = Math.Max(0, MetricList.SelectedIndex);
            (MetricList.ItemContainerGenerator.ContainerFromIndex(MetricList.SelectedIndex) as UIElement)?.Focus();
            e.Handled = true;
        }
    }

    public sealed class MetricRow(MetricDefinition definition) : INotifyPropertyChanged
    {
        private string _value = string.Empty;

        public MetricDefinition Definition { get; } = definition;

        public string Id => Definition.Id;

        public string Name => Definition.Name;

        public string Category => Definition.Category;

        public bool IsRawSensor => Id.StartsWith("hw/", StringComparison.OrdinalIgnoreCase);

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;
                _value = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed record FormatOption(string Spec, string Example, string Description);
}

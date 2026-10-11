using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImpiousBonum.Core.CustomData;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Custom data…: the list of custom data sources, with add, edit and remove. Changes are saved at once.</summary>
public partial class CustomDataWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<string, string?> _status;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <param name="status">A source's last status (its <c>custom.&lt;name&gt;.status</c> metric), shown in the list.</param>
    public CustomDataWindow(AppSettings settings, Func<string, string?> status)
    {
        InitializeComponent();
        _settings = settings;
        _status = status;
        _refresh.Tick += (_, _) => Fill();
        Loaded += (_, _) => _refresh.Start();
        Closed += (_, _) => _refresh.Stop();
        Fill();
    }

    private void Fill()
    {
        var selected = Sources.SelectedIndex;
        Sources.Items.Clear();
        foreach (var source in _settings.CustomData)
        {
            var from = source.IsWeb ? source.Url : $"{source.Command} {source.Arguments}".Trim();
            var fields = source.Fields is { Count: > 0 } list ? string.Join(", ", list.Select(f => f.Name)) : "value";
            var status = _status(source.StatusId) is { } text ? $" · {text}" : string.Empty;
            Sources.Items.Add(new ListBoxItem { Content = $"{source.Name}: {fields}{status}", ToolTip = from, Tag = source });
        }
        Sources.SelectedIndex = Math.Min(selected, Sources.Items.Count - 1);
        OnSelectionChanged(this, null!);
    }

    private CustomSource? Selected => (Sources.SelectedItem as ListBoxItem)?.Tag as CustomSource;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EditButton.IsEnabled = RemoveButton.IsEnabled = Selected is not null;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (CustomSourceWindow.Ask(this, null, _settings.CustomData.Select(s => s.Name).ToList()) is not { } source)
            return;
        // A new list rather than a change to the old one: the sampler reads it from another thread.
        _settings.CustomData = [.. _settings.CustomData, source];
        Save($"Added custom data source \"{source.Name}\"");
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } current)
            return;
        var others = _settings.CustomData.Where(s => s != current).Select(s => s.Name).ToList();
        if (CustomSourceWindow.Ask(this, current, others) is not { } edited)
            return;
        _settings.CustomData = _settings.CustomData.Select(s => s == current ? edited : s).ToList();
        Save($"Changed custom data source \"{edited.Name}\"");
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } current)
            return;
        _settings.CustomData = _settings.CustomData.Where(s => s != current).ToList();
        Save($"Removed custom data source \"{current.Name}\"");
    }

    private void Save(string log)
    {
        _settings.Save();
        AppLog.Info(log);
        Fill();
    }
}

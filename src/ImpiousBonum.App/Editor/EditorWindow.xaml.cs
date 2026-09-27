using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// The layout editor: widget list, live preview and properties. Edits go to a <see cref="LayoutSession"/>;
/// the app mirrors that onto the real dashboard as you work, and writes layout.json when you save.
/// </summary>
public partial class EditorWindow : Window
{
    private static readonly double[] SnapSizes = [1, 5, 10, 20, 40];

    private readonly LayoutSession _session;
    private readonly PreviewSurface _preview;
    private readonly PropertyPanel _properties;
    private MetricStore? _store;
    private readonly Func<LayoutDocument> _loadSaved;
    private bool _syncingList;

    /// <param name="loadSaved">Reads the layout as last saved, for Revert.</param>
    /// <param name="dashboardSize">The dashboard monitor's size in pixels, for "Match dashboard screen".</param>
    public EditorWindow(LayoutSession session, Func<LayoutDocument> loadSaved, Func<(int Width, int Height)?> dashboardSize)
    {
        InitializeComponent();
        _session = session;
        _loadSaved = loadSaved;

        _preview = new PreviewSurface(session);
        PreviewHost.Content = _preview;
        _properties = new PropertyPanel(session, dashboardSize, () => _store);
        PropertiesHost.Content = _properties;

        SnapBox.ItemsSource = SnapSizes.Select(s => s <= 1 ? "Off" : $"{s} px").ToList();
        SnapBox.SelectedIndex = Array.IndexOf(SnapSizes, 10.0);

        var menu = new ContextMenu();
        foreach (var descriptor in WidgetFactory.Descriptors)
        {
            var item = new MenuItem { Header = descriptor.Name, ToolTip = descriptor.Description };
            item.Click += (_, _) => _session.AddWidget(descriptor);
            menu.Items.Add(item);
        }
        AddButton.ContextMenu = menu;

        session.Changed += (_, change) =>
        {
            if (change.Kind != ChangeKind.Geometry)
                RefreshList();
            UpdateState();
        };
        session.SelectionChanged += (_, _) =>
        {
            SyncListSelection();
            UpdateState();
        };

        RefreshList();
        UpdateState();
    }

    /// <summary>Raised when the user saves; the app writes the document to layout.json.</summary>
    public event EventHandler<LayoutDocument>? SaveRequested;

    public void Refresh(MetricStore store, DateTime now)
    {
        var first = _store is null;
        _store = store;
        _preview.Refresh(store, now);
        if (first)
            _properties.Rebuild();
        else
            _properties.RefreshLive();
    }

    // ---- List ---------------------------------------------------------------------------------------

    private void RefreshList()
    {
        _syncingList = true;
        WidgetList.ItemsSource = _session.Document.Widgets.Select((w, i) => $"{i + 1}. {Describe(w)}").ToList();
        _syncingList = false;
        SyncListSelection();
    }

    private void SyncListSelection()
    {
        _syncingList = true;
        WidgetList.SelectedIndex = _session.SelectedIndex;
        if (_session.SelectedIndex >= 0)
            WidgetList.ScrollIntoView(WidgetList.SelectedItem);
        _syncingList = false;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingList)
            _session.Select(WidgetList.SelectedIndex);
    }

    /// <summary>"Graph: CPU", "Text: {gpu.load}" — enough to tell widgets apart.</summary>
    private static string Describe(System.Text.Json.Nodes.JsonObject widget)
    {
        var type = widget.GetString("type");
        var name = WidgetFactory.Find(type)?.Name ?? $"Unknown ({type})";
        var detail = widget.GetString("label") ?? widget.GetString("text") ?? widget.GetString("metric") ?? widget.GetString("icon")
            ?? widget.GetString("drives") ?? widget.GetString("timeFormat")
            ?? (widget["rows"] is System.Text.Json.Nodes.JsonArray rows
                ? string.Join(", ", rows.OfType<System.Text.Json.Nodes.JsonObject>().Select(r => r.GetString("label")).Where(l => l is not null))
                : null);
        return string.IsNullOrWhiteSpace(detail) ? name : $"{name}: {detail}";
    }

    // ---- Commands -------------------------------------------------------------------------------------

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        AddButton.ContextMenu.PlacementTarget = AddButton;
        AddButton.ContextMenu.Placement = PlacementMode.Bottom;
        AddButton.ContextMenu.IsOpen = true;
    }

    private void OnDuplicate(object sender, RoutedEventArgs e) => _session.DuplicateSelected();

    private void OnDelete(object sender, RoutedEventArgs e) => _session.DeleteSelected();

    private void OnForward(object sender, RoutedEventArgs e) => _session.MoveSelected(+1);

    private void OnBackward(object sender, RoutedEventArgs e) => _session.MoveSelected(-1);

    private void OnUndo(object sender, RoutedEventArgs e) => _session.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => _session.Redo();

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        if (!_session.IsDirty)
            return;
        _session.Replace(_loadSaved());
    }

    private void OnSnapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_preview is not null && SnapBox.SelectedIndex >= 0)
            _preview.Snap = SnapSizes[SnapBox.SelectedIndex];
    }

    private void Save()
    {
        SaveRequested?.Invoke(this, _session.Document);
        _session.MarkSaved();
        UpdateState();
    }

    private void UpdateState()
    {
        var dirty = _session.IsDirty;
        Title = $"{(dirty ? "● " : string.Empty)}Impious Bonum layout editor";
        SaveButton.IsEnabled = dirty;
        RevertButton.IsEnabled = dirty;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;

        var selected = _session.SelectedIndex >= 0;
        DuplicateButton.IsEnabled = selected;
        DeleteButton.IsEnabled = selected;
        ForwardButton.IsEnabled = selected && _session.SelectedIndex < _session.Document.Widgets.Count - 1;
        BackwardButton.IsEnabled = selected && _session.SelectedIndex > 0;

        if (_session.SelectedWidget is { } widget)
        {
            var r = LayoutSession.GeometryOf(widget);
            StatusText.Text = $"{Describe(widget)} · position {r.X:0}, {r.Y:0} · size {r.Width:0}×{r.Height:0} · arrows nudge (Shift for 10)";
        }
        else
        {
            StatusText.Text = $"Canvas {_session.Document.Width:0}×{_session.Document.Height:0} · {_session.Document.Widgets.Count} widgets";
        }

        var issues = LayoutValidator.Validate(_session.Document);
        IssuesText.Text = issues.Count == 0 ? string.Empty : $"{issues.Count} problem{(issues.Count == 1 ? "" : "s")}";
        IssuesText.ToolTip = issues.Count == 0 ? null : string.Join(Environment.NewLine, issues);
    }

    // ---- Keyboard -------------------------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // Plain keys belong to whatever text field has focus.
        var typing = Keyboard.FocusedElement is TextBoxBase or ComboBox;

        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                _session.Redo();
                break;
            case Key.Z when ctrl:
                _session.Undo();
                break;
            case Key.S when ctrl:
                Save();
                break;
            case Key.D when ctrl:
                _session.DuplicateSelected();
                break;
            case Key.Delete when !typing:
                _session.DeleteSelected();
                break;
            case Key.Escape when !typing:
                _session.Select(-1);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when !typing && _session.SelectedWidget is { } widget:
                var step = shift ? 10 : 1;
                var r = LayoutSession.GeometryOf(widget);
                r.Offset(e.Key switch { Key.Left => -step, Key.Right => step, _ => 0 }, e.Key switch { Key.Up => -step, Key.Down => step, _ => 0 });
                _session.SetGeometry(_session.SelectedIndex, r, $"nudge:{_session.SelectedIndex}", null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---- Closing ------------------------------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_session.IsDirty)
            return;

        var answer = MessageBox.Show(this, "Save your changes to the layout?", "Impious Bonum", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            Save();
        else if (answer == MessageBoxResult.Cancel)
            e.Cancel = true;
    }
}

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
/// the app mirrors that onto the real dashboard as you work. It edits the active saved layout, and can switch to,
/// create, rename, duplicate, delete, export and import saved layouts.
/// </summary>
public partial class EditorWindow : Window
{
    private static readonly double[] SnapSizes = [1, 5, 10, 20, 40];

    private readonly LayoutSession _session;
    private readonly PreviewSurface _preview;
    private readonly PropertyPanel _properties;
    private MetricStore? _store;
    private readonly LayoutStore _layouts;
    private readonly MenuItem _deleteLayoutItem;
    private bool _syncingList;
    private bool _syncingLayouts;

    /// <param name="layouts">The saved layouts; the session holds a working copy of the active one.</param>
    /// <param name="dashboardSize">The dashboard monitor's size in pixels, for "Match dashboard screen".</param>
    public EditorWindow(LayoutSession session, LayoutStore layouts, Func<(int Width, int Height)?> dashboardSize)
    {
        InitializeComponent();
        _session = session;
        _layouts = layouts;

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

        var layoutMenu = new ContextMenu();
        layoutMenu.Items.Add(MenuItemFor("Save as…", "Save your changes as a new layout", SaveAs));
        layoutMenu.Items.Add(MenuItemFor("Rename…", null, RenameLayout));
        layoutMenu.Items.Add(MenuItemFor("Duplicate…", "Copy the saved layout and edit the copy", DuplicateLayout));
        layoutMenu.Items.Add(new Separator());
        layoutMenu.Items.Add(MenuItemFor("Export…", "Save this layout, with its font and background image, as one file to share or move to another PC", ExportLayout));
        layoutMenu.Items.Add(MenuItemFor("Import…", "Add a layout from an exported .ibl file", ImportLayout));
        layoutMenu.Items.Add(new Separator());
        _deleteLayoutItem = MenuItemFor("Delete", null, DeleteLayout);
        layoutMenu.Items.Add(_deleteLayoutItem);
        LayoutMenuButton.ContextMenu = layoutMenu;

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
        RefreshLayouts();
        UpdateState();
    }

    private static MenuItem MenuItemFor(string header, string? toolTip, Action action)
    {
        var item = new MenuItem { Header = header, ToolTip = toolTip };
        item.Click += (_, _) => action();
        return item;
    }

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

    // ---- Saved layouts ------------------------------------------------------------------------------

    private void RefreshLayouts()
    {
        _syncingLayouts = true;
        var names = _layouts.List();
        LayoutBox.ItemsSource = names;
        LayoutBox.SelectedItem = _layouts.Active;
        _deleteLayoutItem.IsEnabled = names.Count > 1;
        _syncingLayouts = false;
    }

    private void OnLayoutBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingLayouts && LayoutBox.SelectedItem is string name)
            SwitchTo(name);
    }

    private void OnLayoutMenu(object sender, RoutedEventArgs e)
    {
        LayoutMenuButton.ContextMenu.PlacementTarget = LayoutMenuButton;
        LayoutMenuButton.ContextMenu.Placement = PlacementMode.Bottom;
        LayoutMenuButton.ContextMenu.IsOpen = true;
    }

    /// <summary>Edits another saved layout (the dashboard follows), asking about unsaved changes first.</summary>
    /// <returns>False if the user cancelled.</returns>
    public bool SwitchTo(string name)
    {
        if (string.Equals(name, _layouts.Active, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!ConfirmLeave())
        {
            RefreshLayouts();
            return false;
        }

        _layouts.SetActive(name);
        LoadActive();
        return true;
    }

    private void SaveAs()
    {
        var name = NamePromptWindow.Ask(this, "Save layout as", "Name for the new layout", $"{_layouts.Active} copy",
            n => _layouts.Library.CheckNewName(n));
        if (name is null)
            return;

        // Unsaved changes go to the new layout; the old one stays as it was last saved.
        _layouts.Save(name, _session.Document);
        _layouts.SetActive(name);
        _session.MarkSaved();
        RefreshLayouts();
        UpdateState();
    }

    private void RenameLayout()
    {
        var current = _layouts.Active;
        var name = NamePromptWindow.Ask(this, "Rename layout", $"New name for \"{current}\"", current,
            n => _layouts.Library.CheckNewName(n, except: current));
        if (name is null || name == current)
            return;

        _layouts.Rename(current, name);
        RefreshLayouts();
        UpdateState();
    }

    private void DuplicateLayout()
    {
        if (!ConfirmLeave())
            return;
        var name = NamePromptWindow.Ask(this, "Duplicate layout", $"Name for the copy of \"{_layouts.Active}\"", $"{_layouts.Active} copy",
            n => _layouts.Library.CheckNewName(n));
        if (name is null)
            return;

        _layouts.Duplicate(_layouts.Active, name);
        _layouts.SetActive(name);
        LoadActive();
    }

    /// <summary>Exports what's in the editor, unsaved changes included.</summary>
    private void ExportLayout() => LayoutTransfer.Export(this, _session.Document, _layouts.Active);

    /// <summary>Imports a layout file and starts editing it (the dashboard follows).</summary>
    public void ImportLayout()
    {
        if (!ConfirmLeave())
            return;
        if (LayoutTransfer.Import(this, _layouts) is not { } name)
            return;
        _layouts.SetActive(name);
        LoadActive();
    }

    private void DeleteLayout()
    {
        if (_layouts.List().Count <= 1)
            return;
        var answer = MessageBox.Show(this, $"Delete the layout \"{_layouts.Active}\"? This can't be undone.", "Impious Bonum",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        _layouts.Delete(_layouts.Active);
        LoadActive();
    }

    /// <summary>Starts editing the active layout from its saved file.</summary>
    private void LoadActive()
    {
        _session.Reset(_layouts.LoadOrDefault());
        RefreshLayouts();
        UpdateState();
    }

    /// <summary>Before leaving the layout being edited: save, discard, or (false) stay.</summary>
    private bool ConfirmLeave()
    {
        if (!_session.IsDirty)
            return true;

        var answer = MessageBox.Show(this, $"Save your changes to \"{_layouts.Active}\"?", "Impious Bonum", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            Save();
        return answer != MessageBoxResult.Cancel;
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
        _session.Replace(_layouts.LoadOrDefault());
    }

    private void OnSnapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session is not null && SnapBox.SelectedIndex >= 0)
            _session.Snap = SnapSizes[SnapBox.SelectedIndex];
    }

    private void Save()
    {
        _layouts.Save(_session.Document);
        _session.MarkSaved();
        UpdateState();
    }

    private void UpdateState()
    {
        var dirty = _session.IsDirty;
        Title = $"{(dirty ? "● " : string.Empty)}{_layouts.Active} · Impious Bonum layout editor";
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
        if (!ConfirmLeave())
            e.Cancel = true;
    }
}

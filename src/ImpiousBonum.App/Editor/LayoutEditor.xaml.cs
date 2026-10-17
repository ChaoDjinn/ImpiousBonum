using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// The layout editor, the main window's Layouts page: widget list, live preview and properties. Edits go to a
/// <see cref="LayoutSession"/>; the app mirrors that onto the real dashboards as you work. It edits the saved layout one
/// dashboard shows (the main one, or another screen's), can move to another dashboard, and can switch to, create,
/// rename, duplicate, delete, export and import saved layouts for the one it's on, or get one from the gallery.
/// </summary>
public partial class LayoutEditor : UserControl
{
    private static readonly double[] SnapSizes = [1, 5, 10, 20, 40];

    /// <summary>The Align menu, in groups between separators: what each item does, its name and its tooltip.</summary>
    private static readonly (Arrangement Arrangement, string Header, string ToolTip)[][] AlignChoices =
    [
        [
            (Arrangement.AlignLeft, "Align left", "Line up their left edges with the leftmost"),
            (Arrangement.AlignCentres, "Align centres", "Line up their centres on one line down the middle of them all"),
            (Arrangement.AlignRight, "Align right", "Line up their right edges with the rightmost"),
        ],
        [
            (Arrangement.AlignTop, "Align top", "Line up their top edges with the highest"),
            (Arrangement.AlignMiddles, "Align middles", "Line up their middles on one line across the middle of them all"),
            (Arrangement.AlignBottom, "Align bottom", "Line up their bottom edges with the lowest"),
        ],
        [
            (Arrangement.DistributeHorizontally, "Distribute horizontally", "Equal gaps between them from left to right, in the space they take up now. Needs three or more."),
            (Arrangement.DistributeVertically, "Distribute vertically", "Equal gaps between them from top to bottom, in the space they take up now. Needs three or more."),
        ],
        [
            (Arrangement.SameWidth, "Same width", "Make them as wide as the first one you selected"),
            (Arrangement.SameHeight, "Same height", "Make them as tall as the first one you selected"),
        ],
    ];

    private readonly LayoutSession _session;
    private readonly PreviewSurface _preview;
    private readonly PropertyPanel _properties;
    private MetricStore? _store;
    private readonly LayoutStore _layouts;
    private readonly Func<string> _shownLayout;
    private readonly MenuItem _deleteLayoutItem;
    private readonly MenuItem _bringForwardItem;
    private readonly MenuItem _sendBackItem;
    private readonly MenuItem _groupItem;
    private readonly MenuItem _ungroupItem;
    private readonly MenuItem _renameGroupItem;
    private readonly MenuItem _alignListItem;
    /// <summary>What each of the list's rows is, top first.</summary>
    private List<WidgetRows.Row> _rows = [];
    /// <summary>Every Align menu item, in both menus, with how many widgets it needs selected.</summary>
    private readonly List<(MenuItem Item, int Needs)> _alignItems = [];
    private bool _syncingList;
    private bool _syncingLayouts;
    private bool _syncingScreens;
    private int _screen;

    /// <param name="layouts">The saved layouts; the session holds a working copy of the one being edited.</param>
    /// <param name="shownLayout">The saved layout the dashboard being edited shows, which is the one to edit.</param>
    /// <param name="dashboardSize">
    /// The size in pixels of the dashboard being edited (its monitor, or its window when windowed), for "Match dashboard screen".
    /// </param>
    public LayoutEditor(LayoutSession session, LayoutStore layouts, Func<string> shownLayout, Func<(int Width, int Height)?> dashboardSize)
    {
        InitializeComponent();
        _session = session;
        _layouts = layouts;
        _shownLayout = shownLayout;
        Editing = shownLayout();

        _preview = new PreviewSurface(session);
        PreviewHost.Content = _preview;
        _properties = new PropertyPanel(session, dashboardSize, () => _store);
        PropertiesHost.Content = _properties;

        SnapBox.ItemsSource = SnapSizes.Select(s => s <= 1 ? "Off" : $"{s} px").ToList();
        SnapBox.SelectedIndex = Array.IndexOf(SnapSizes, 10.0);

        // Grouped by what the widgets are for, each group under a heading.
        var menu = new ContextMenu();
        foreach (var category in WidgetFactory.Descriptors.GroupBy(d => d.Category))
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = category.Key, FontWeight = FontWeights.SemiBold, FontSize = 12 },
                IsHitTestVisible = false,
                Focusable = false,
            });
            foreach (var descriptor in category)
            {
                var item = new MenuItem { Header = descriptor.Name, ToolTip = descriptor.Description };
                item.Click += (_, _) =>
                {
                    _session.AddWidget(descriptor);
                    WidgetAdded?.Invoke(this, descriptor);
                };
                menu.Items.Add(item);
            }
        }
        AddButton.ContextMenu = menu;

        var layoutMenu = new ContextMenu();
        layoutMenu.Items.Add(MenuItemFor("Save as…", "Save your changes as a new layout", SaveAs));
        layoutMenu.Items.Add(MenuItemFor("Rename…", null, RenameLayout));
        layoutMenu.Items.Add(MenuItemFor("Duplicate…", "Copy the saved layout and edit the copy", DuplicateLayout));
        layoutMenu.Items.Add(new Separator());
        layoutMenu.Items.Add(MenuItemFor("Get one from the gallery…", "Browse ready-made layouts and show one on this screen", FromGallery));
        layoutMenu.Items.Add(MenuItemFor("Export…", "Save this layout, with its font and background image, as one file to share or move to another PC", ExportLayout));
        layoutMenu.Items.Add(MenuItemFor("Import…", "Add a layout from an exported .ibl file", ImportLayout));
        layoutMenu.Items.Add(MenuItemFor("Open layout file", "The saved layout's JSON file, to edit by hand. Revert then loads your changes.", OpenLayoutFile));
        layoutMenu.Items.Add(new Separator());
        _deleteLayoutItem = MenuItemFor("Delete", null, DeleteLayout);
        layoutMenu.Items.Add(_deleteLayoutItem);
        LayoutMenuButton.ContextMenu = layoutMenu;

        var alignMenu = new ContextMenu();
        AddAlignItems(alignMenu.Items);
        AlignButton.ContextMenu = alignMenu;

        // A widget's menu in the list (right-clicking one selects it, or keeps the selection it's in), with the keyboard's
        // ways of doing the same.
        var widgetMenu = new ContextMenu();
        _bringForwardItem = MenuItemFor("Bring forward", "Draw on top of the next widget", () => MoveSelectedRow(-1), "Ctrl+]");
        _sendBackItem = MenuItemFor("Send back", "Draw behind the previous widget", () => MoveSelectedRow(+1), "Ctrl+[");
        widgetMenu.Items.Add(_bringForwardItem);
        widgetMenu.Items.Add(_sendBackItem);
        widgetMenu.Items.Add(new Separator());
        _groupItem = MenuItemFor("Group", "Select two or more widgets to group them", () => _session.GroupSelected(), "Ctrl+G");
        _ungroupItem = MenuItemFor("Ungroup", "Break up the group, leaving its widgets where they are", _session.UngroupSelected, "Ctrl+Shift+G");
        _renameGroupItem = MenuItemFor("Rename group…", "Select a group to rename it", RenameGroup);
        foreach (var item in new[] { _groupItem, _ungroupItem, _renameGroupItem })
        {
            ToolTipService.SetShowOnDisabled(item, true);
            widgetMenu.Items.Add(item);
        }
        widgetMenu.Items.Add(new Separator());
        widgetMenu.Items.Add(MenuItemFor("Duplicate", null, _session.DuplicateSelected, "Ctrl+D"));
        widgetMenu.Items.Add(MenuItemFor("Delete", null, _session.DeleteSelected, "Del"));
        widgetMenu.Items.Add(new Separator());
        _alignListItem = new MenuItem { Header = "Align", ToolTip = "Select two or more widgets to line them up" };
        ToolTipService.SetShowOnDisabled(_alignListItem, true);
        AddAlignItems(_alignListItem.Items);
        widgetMenu.Items.Add(_alignListItem);
        WidgetList.ContextMenu = widgetMenu;
        DragReorder.Attach(WidgetList, MoveRow);

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

    /// <summary>The window it's in, for the dialogs it opens.</summary>
    private Window? Owner => Window.GetWindow(this);

    /// <param name="gesture">The shortcut that does the same, shown beside it.</param>
    private static MenuItem MenuItemFor(string header, string? toolTip, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, ToolTip = toolTip, InputGestureText = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The Align menu's items, for the toolbar's drop-down and the widget list's menu.</summary>
    private void AddAlignItems(ItemCollection items)
    {
        foreach (var group in AlignChoices)
        {
            if (items.Count > 0)
                items.Add(new Separator());
            foreach (var (arrangement, header, toolTip) in group)
            {
                var item = MenuItemFor(header, toolTip, () => _session.ArrangeSelected(arrangement));
                // Distributing needs three, so with two selected it says why it's greyed out.
                ToolTipService.SetShowOnDisabled(item, true);
                _alignItems.Add((item, ArrangeGeometry.Needs(arrangement)));
                items.Add(item);
            }
        }
    }

    /// <summary>What a widget needs set up outside the layout, offered as buttons when it's selected.</summary>
    public Func<WidgetDescriptor, IReadOnlyList<WidgetSetup>>? Setups
    {
        get => _properties.Setups;
        set
        {
            _properties.Setups = value;
            _properties.Rebuild();
        }
    }

    /// <summary>The names in a list the app keeps, such as the news feeds, for settings that pick from it.</summary>
    public Func<string, IReadOnlyList<string>>? Options
    {
        get => _properties.Options;
        set
        {
            _properties.Options = value;
            _properties.Rebuild();
        }
    }

    /// <summary>For a name picked from one of those lists that's spelt another way now, the one it stands for (see <see cref="PropertyPanel.PickedOption"/>).</summary>
    public Func<string, string, string?>? PickedOption
    {
        get => _properties.PickedOption;
        set
        {
            _properties.PickedOption = value;
            _properties.Rebuild();
        }
    }

    /// <summary>A widget was added from the Add menu.</summary>
    public event EventHandler<WidgetDescriptor>? WidgetAdded;

    /// <summary>"Get one from the gallery…": the gallery is wanted, for this screen.</summary>
    public event EventHandler? GalleryRequested;

    /// <summary>What's being edited and whether it has unsaved changes, for the window's title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The title changed.</summary>
    public event EventHandler? StateChanged;

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

    /// <summary>A saved theme changed on disk; redraw with it.</summary>
    public void RefreshTheme() => _session.RefreshTheme();

    // ---- List ---------------------------------------------------------------------------------------

    private void RefreshList()
    {
        // A new list loses the row that had focus; someone using the keyboard carries on from the selected widget.
        var focused = WidgetList.IsKeyboardFocusWithin;
        _syncingList = true;
        _rows = WidgetRows.For(Groups());
        WidgetList.ItemsSource = _rows.Select(row => row.IsGroup
            ? new ListEntry(row.Group!, $" · {_session.MembersOf(row.Group!).Count} widgets") { Row = row }
            : ListEntry.Of(_session.Document.Widgets[row.Widget]) with { Row = row }).ToList();
        _syncingList = false;
        SyncListSelection();
        if (focused && WidgetList.SelectedIndex >= 0)
        {
            WidgetList.UpdateLayout();
            // Focused after a key without Ctrl (Enter in the rename prompt, say), a row selects itself alone, as an arrow key
            // would make it; so the list isn't listened to while it moves, then shows the selection again.
            _syncingList = true;
            (WidgetList.ItemContainerGenerator.ContainerFromIndex(WidgetList.SelectedIndex) as UIElement)?.Focus();
            _syncingList = false;
            SyncListSelection();
        }
    }

    /// <summary>The widget menu is for a widget, so not over the empty space below them.</summary>
    private void OnListMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(WidgetList, source) is null)
            e.Handled = true;
    }

    private void SyncListSelection()
    {
        _syncingList = true;
        // The selected widgets' rows, the first picked first so the list agrees about which that was. A group's own row
        // stands for its widgets only while it's being clicked: their rows show them selected, so each can be taken out.
        var rows = _session.Selection.Select(RowOf).Where(row => row >= 0 && row < WidgetList.Items.Count).ToList();
        // Left alone when it already matches, such as when the change came from clicking in the list itself.
        if (!WidgetList.SelectedItems.Cast<object>().Select(WidgetList.Items.IndexOf).SequenceEqual(rows))
        {
            WidgetList.UnselectAll();
            WidgetList.SelectedIndex = rows.Count > 0 ? rows[0] : -1;
            foreach (var row in rows.Skip(1))
                WidgetList.SelectedItems.Add(WidgetList.Items[row]);
        }
        if (_session.SelectedIndex >= 0)
            WidgetList.ScrollIntoView(WidgetList.SelectedItem);
        _syncingList = false;
    }

    /// <summary>The widgets selected in the list, in the order they were picked; a group's row is its widgets.</summary>
    private IEnumerable<int> ListSelection() => WidgetList.SelectedItems.Cast<object>()
        .Select(WidgetList.Items.IndexOf)
        .Where(row => row >= 0 && row < _rows.Count)
        .SelectMany(row => _rows[row] is { IsGroup: true, Group: { } group } ? _session.MembersOf(group) : [_rows[row].Widget])
        .Distinct();

    /// <summary>The list's row for a widget.</summary>
    private int RowOf(int index) => _rows.FindIndex(row => row.Widget == index);

    /// <summary>The group each widget is in, back to front.</summary>
    private List<string?> Groups() => [.. Enumerable.Range(0, _session.Document.Widgets.Count).Select(_session.GroupOf)];

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingList)
            return;
        _session.Select(ListSelection());
        // A group's row clicked selects its widgets, which the session may already have had selected.
        if (WidgetList.SelectedItems.Cast<ListEntry>().Any(entry => entry.IsGroup))
            SyncListSelection();
    }

    /// <summary>
    /// Moves a row of the list to another place (take it out at <paramref name="from"/>, put it in at <paramref name="to"/>):
    /// a widget, into or out of a group by where it goes, or a group with all its widgets.
    /// </summary>
    private void MoveRow(int from, int to)
    {
        if (from < 0 || from >= _rows.Count || WidgetRows.Move(Groups(), from, to) is not var (order, groups))
            return;
        var row = _rows[from];
        _session.Restack(order, groups, row.IsGroup ? _session.MembersOf(row.Group!) : [row.Widget]);
    }

    /// <summary>Moves the selected widget's row up (-1, so drawn on top of the one above) or down (+1): bringing it forward or sending it back.</summary>
    private void MoveSelectedRow(int step)
    {
        if (_session.Selection.Count != 1)
            return;
        var row = RowOf(_session.SelectedIndex);
        if (row >= 0 && row + step >= 0 && row + step < _rows.Count)
            MoveRow(row, row + step);
    }

    private void RenameGroup()
    {
        if (_session.SelectedGroup is not { } group)
            return;
        var name = NamePromptWindow.Ask(Owner, "Rename group", $"New name for \"{group}\"", group, n => _session.CheckGroupName(n, group));
        if (name is not null)
            _session.RenameGroup(group, name);
    }

    /// <summary>"Graph: CPU", "Text: {gpu.load}" — enough to tell widgets apart.</summary>
    private static string Describe(System.Text.Json.Nodes.JsonObject widget) =>
        ListEntry.Of(widget) is var entry && entry.Detail.Length > 0 ? $"{entry.Name}:{entry.Detail}" : entry.Name;

    /// <summary>
    /// A row of the widget list: the widget type's name, then what tells it apart, e.g. "Graph" and " CPU"; or a group's
    /// name and how many widgets it has.
    /// </summary>
    internal sealed record ListEntry(string Name, string Detail)
    {
        /// <summary>The row it is in the list; null for a widget described on its own.</summary>
        public WidgetRows.Row? Row { get; init; }

        public bool IsGroup => Row?.IsGroup == true;

        public string ToolTip => IsGroup ? $"Group: {Name}{Detail}. Click to select them all." : Detail.Length > 0 ? $"{Name}:{Detail}" : Name;

        /// <summary>A group's widgets are set in under its row.</summary>
        public Thickness Indent => new(Row is { IsGroup: false, Group: not null } ? 18 : 0, 0, 0, 0);

        /// <summary>A group's row has a folder before its name.</summary>
        public Visibility GlyphVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;

        // Each row is a widget of its own even when two read the same (a copy, say), so selecting one doesn't select both.
        public bool Equals(ListEntry? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public static ListEntry Of(System.Text.Json.Nodes.JsonObject widget)
        {
            var type = widget.GetString("type");
            var name = WidgetFactory.Find(type)?.Name ?? $"Unknown ({type})";
            var detail = widget.GetString("label") ?? widget.GetString("text") ?? widget.GetString("metric") ?? widget.GetString("icon")
                ?? widget.GetString("drives") ?? widget.GetString("timeFormat")
                ?? (widget["rows"] is System.Text.Json.Nodes.JsonArray rows
                    ? string.Join(", ", rows.OfType<System.Text.Json.Nodes.JsonObject>().Select(r => r.GetString("label")).Where(l => l is not null))
                    : null);
            return new(name, string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}");
        }
    }

    // ---- Screens ------------------------------------------------------------------------------------

    /// <summary>The dashboards it can edit the layouts of, by their screens' names, and which one it's on.</summary>
    public void SetScreens(IReadOnlyList<string> screens, int current)
    {
        _syncingScreens = true;
        ScreenBox.ItemsSource = screens;
        _syncingScreens = false;
        SelectScreen(current);
    }

    /// <summary>
    /// Another dashboard is to be edited, by its place in the <see cref="SetScreens"/> list. Unsaved changes have been
    /// dealt with; once the app has moved to it, the editor loads the layout it shows.
    /// </summary>
    public event EventHandler<int>? ScreenChosen;

    /// <summary>Edits another dashboard's layout, asking about unsaved changes first.</summary>
    /// <returns>False if the user cancelled.</returns>
    public bool SwitchScreen(int index)
    {
        if (index == _screen)
            return true;
        if (!ConfirmLeave())
        {
            SelectScreen(_screen);
            return false;
        }

        SelectScreen(index);
        ScreenChosen?.Invoke(this, index);
        Load(_shownLayout());
        return true;
    }

    private void SelectScreen(int index)
    {
        _screen = index;
        _syncingScreens = true;
        ScreenBox.SelectedIndex = index;
        _syncingScreens = false;
    }

    private void OnScreenBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingScreens && ScreenBox.SelectedIndex >= 0)
            SwitchScreen(ScreenBox.SelectedIndex);
    }

    // ---- Saved layouts ------------------------------------------------------------------------------

    /// <summary>The saved layout being edited, which the dashboard being edited shows.</summary>
    public string Editing { get; private set; }

    /// <summary>
    /// The editor moved to another saved layout (picked, saved as, duplicated, imported, or in place of a deleted one):
    /// the dashboard being edited shows it from now on.
    /// </summary>
    public event EventHandler<string>? LayoutChosen;

    private void RefreshLayouts()
    {
        _syncingLayouts = true;
        var names = _layouts.List();
        LayoutBox.ItemsSource = names;
        LayoutBox.SelectedItem = names.FirstOrDefault(n => string.Equals(n, Editing, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Edits another saved layout (the dashboard being edited follows), asking about unsaved changes first.</summary>
    /// <returns>False if the user cancelled.</returns>
    public bool SwitchTo(string name)
    {
        if (string.Equals(name, Editing, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!ConfirmLeave())
        {
            RefreshLayouts();
            return false;
        }

        Choose(name);
        return true;
    }

    private void SaveAs()
    {
        var name = NamePromptWindow.Ask(Owner, "Save layout as", "Name for the new layout", $"{Editing} copy",
            n => _layouts.Library.CheckNewName(n));
        if (name is null)
            return;

        // Unsaved changes go to the new layout; the old one stays as it was last saved.
        _layouts.Save(name, _session.Document);
        Editing = name;
        LayoutChosen?.Invoke(this, name);
        _session.MarkSaved();
        RefreshLayouts();
        UpdateState();
    }

    private void RenameLayout()
    {
        var current = Editing;
        var name = NamePromptWindow.Ask(Owner, "Rename layout", $"New name for \"{current}\"", current,
            n => _layouts.Library.CheckNewName(n, except: current));
        if (name is null || name == current)
            return;

        // The store moves every dashboard showing it to the new name.
        _layouts.Rename(current, name);
        Editing = _layouts.Library.Find(name) ?? name;
        RefreshLayouts();
        UpdateState();
    }

    private void DuplicateLayout()
    {
        if (!ConfirmLeave())
            return;
        var name = NamePromptWindow.Ask(Owner, "Duplicate layout", $"Name for the copy of \"{Editing}\"", $"{Editing} copy",
            n => _layouts.Library.CheckNewName(n));
        if (name is null)
            return;

        _layouts.Duplicate(Editing, name);
        Choose(name);
    }

    /// <summary>Exports what's in the editor, unsaved changes included.</summary>
    private void ExportLayout()
    {
        if (Owner is { } owner)
            LayoutTransfer.Export(owner, _session.Document, Editing, _session.Themes);
    }

    /// <summary>Imports a layout file and starts editing it (the dashboard being edited follows).</summary>
    public void ImportLayout()
    {
        if (!ConfirmLeave())
            return;
        if (LayoutTransfer.Import(Owner, _layouts) is not { } name)
            return;
        Choose(name);
    }

    /// <summary>Off to the gallery, for a layout for the screen being edited. Leaving asks about unsaved changes.</summary>
    private void FromGallery() => GalleryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The saved layout's file, in whatever edits JSON.</summary>
    private void OpenLayoutFile()
    {
        var path = _layouts.Library.PathOf(Editing);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No handler registered for .json; fall back to Notepad.
            Process.Start("notepad.exe", $"\"{path}\"");
        }
    }

    private void DeleteLayout()
    {
        if (_layouts.List().Count <= 1)
            return;
        var answer = MessageBox.Show(Owner!, $"Delete the layout \"{Editing}\"? This can't be undone.", "Impious Bonum",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        // The store moves the dashboards that showed it to another layout; carry on with the one this dashboard has now.
        _layouts.Delete(Editing);
        Choose(_shownLayout());
    }

    /// <summary>Edits another saved layout, which the dashboard being edited shows from now on.</summary>
    private void Choose(string name)
    {
        LayoutChosen?.Invoke(this, name);
        Load(name);
    }

    /// <summary>Starts editing a saved layout from its file.</summary>
    private void Load(string name)
    {
        Editing = name;
        _session.Reset(_layouts.LoadOrDefault(name));
        RefreshLayouts();
        UpdateState();
    }

    /// <summary>Before leaving the layout being edited: save, discard, or (false) stay.</summary>
    public bool ConfirmLeave()
    {
        if (!_session.IsDirty)
            return true;

        var answer = MessageBox.Show(Owner!, $"Save your changes to \"{Editing}\"?", "Impious Bonum", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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

    private void OnAlign(object sender, RoutedEventArgs e)
    {
        AlignButton.ContextMenu.PlacementTarget = AlignButton;
        AlignButton.ContextMenu.Placement = PlacementMode.Bottom;
        AlignButton.ContextMenu.IsOpen = true;
    }

    private void OnDuplicate(object sender, RoutedEventArgs e) => _session.DuplicateSelected();

    private void OnDelete(object sender, RoutedEventArgs e) => _session.DeleteSelected();

    private void OnUndo(object sender, RoutedEventArgs e) => _session.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => _session.Redo();

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        if (!_session.IsDirty)
            return;
        _session.Replace(_layouts.LoadOrDefault(Editing));
    }

    private void OnSnapChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session is not null && SnapBox.SelectedIndex >= 0)
            _session.Snap = SnapSizes[SnapBox.SelectedIndex];
    }

    private void Save()
    {
        _layouts.Save(Editing, _session.Document);
        _session.MarkSaved();
        UpdateState();
    }

    private void UpdateState()
    {
        var dirty = _session.IsDirty;
        var title = $"{(dirty ? "● " : string.Empty)}{Editing} · Impious Bonum";
        if (title != Title)
        {
            Title = title;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        SaveButton.IsEnabled = dirty;
        RevertButton.IsEnabled = dirty;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;

        var count = _session.Selection.Count;
        DuplicateButton.IsEnabled = count > 0;
        DeleteButton.IsEnabled = count > 0;
        AlignButton.IsEnabled = count >= 2;
        _alignListItem.IsEnabled = count >= 2;
        foreach (var (item, needs) in _alignItems)
            item.IsEnabled = count >= needs;
        // Several widgets have no one place in the drawing order to move from.
        var row = count == 1 ? RowOf(_session.SelectedIndex) : -1;
        _bringForwardItem.IsEnabled = row > 0;
        _sendBackItem.IsEnabled = row >= 0 && row < _rows.Count - 1;
        _groupItem.IsEnabled = count >= 2 && _session.SelectedGroup is null;
        _ungroupItem.IsEnabled = _session.SelectionHasGroup;
        _renameGroupItem.IsEnabled = _session.SelectedGroup is not null;

        if (_session.SelectedGroup is { } group)
        {
            var r = EditGeometry.Bounds(_session.Selection.Select(i => LayoutSession.GeometryOf(_session.Document.Widgets[i])));
            StatusText.Text = $"Group: {group} · {count} widgets · position {r.X:0}, {r.Y:0} · size {r.Width:0}×{r.Height:0} · click one again for it alone · Ctrl+Shift+G ungroups";
        }
        else if (count > 1)
        {
            var r = EditGeometry.Bounds(_session.Selection.Select(i => LayoutSession.GeometryOf(_session.Document.Widgets[i])));
            StatusText.Text = $"{count} widgets selected · position {r.X:0}, {r.Y:0} · size {r.Width:0}×{r.Height:0} together · drag one to move them all · arrows nudge (Shift for 10)";
        }
        else if (_session.SelectedWidget is { } widget)
        {
            var r = LayoutSession.GeometryOf(widget);
            StatusText.Text = $"{Describe(widget)} · position {r.X:0}, {r.Y:0} · size {r.Width:0}×{r.Height:0} · arrows nudge (Shift for 10) · Ctrl+] brings forward, Ctrl+[ sends back";
        }
        else
        {
            StatusText.Text = $"Canvas {_session.Document.Width:0}×{_session.Document.Height:0} · {_session.Document.Widgets.Count} widgets";
        }

        var issues = LayoutValidator.Validate(_session.Document, _session.Themes);
        IssuesPanel.Visibility = issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        IssuesText.Text = $"{issues.Count} problem{(issues.Count == 1 ? "" : "s")}";
        IssuesPanel.ToolTip = issues.Count == 0 ? null : string.Join(Environment.NewLine, issues);
    }

    // ---- Keyboard -------------------------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (HandleKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>Does what a key does in the editor (its shortcuts, and arrows nudging the selected widgets).</summary>
    /// <returns>Whether the key was one of them, and so is used up.</returns>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        // Plain keys belong to whatever text field has focus.
        var typing = Keyboard.FocusedElement is TextBoxBase or ComboBox;

        switch (key)
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
            case Key.OemCloseBrackets when ctrl:
                MoveSelectedRow(-1);
                break;
            case Key.OemOpenBrackets when ctrl:
                MoveSelectedRow(+1);
                break;
            case Key.G when ctrl && shift:
                _session.UngroupSelected();
                break;
            case Key.G when ctrl:
                _session.GroupSelected();
                break;
            case Key.Delete when !typing:
                _session.DeleteSelected();
                break;
            case Key.Escape when !typing:
                _session.Select(-1);
                break;
            case Key.A when ctrl && !typing:
                _session.Select(Enumerable.Range(0, _session.Document.Widgets.Count));
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when !typing && _session.Selection.Count > 0:
                var step = shift ? 10 : 1;
                _session.NudgeSelected(key switch { Key.Left => -step, Key.Right => step, _ => 0 }, key switch { Key.Up => -step, Key.Down => step, _ => 0 });
                break;
            default:
                return false;
        }
        return true;
    }
}

using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using WinForms = System.Windows.Forms;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Shows the settings of the current selection (a widget, or the canvas and theme when nothing is selected),
/// generated from descriptors. Each row has an editor that suits the setting's kind and a reset-to-default button.
/// </summary>
public sealed partial class PropertyPanel : StackPanel
{
    private const double LabelWidth = 128;

    private readonly LayoutSession _session;
    private readonly Func<(int Width, int Height)?> _dashboardSize;
    private readonly Func<MetricStore?> _store;
    private readonly List<Action> _refreshers = [];
    private readonly List<Action> _liveRefreshers = [];
    /// <summary>Each list entry's card, by its list and place, for giving focus back to one that moved.</summary>
    private readonly Dictionary<(string Key, int Index), Border> _itemCards = [];
    private bool _updating;

    public PropertyPanel(LayoutSession session, Func<(int Width, int Height)?> dashboardSize, Func<MetricStore?> store)
    {
        _session = session;
        _dashboardSize = dashboardSize;
        _store = store;
        Margin = new Thickness(16, 12, 16, 24);

        session.SelectionChanged += (_, _) => Rebuild();
        session.Changed += (_, change) =>
        {
            if (change.Kind is ChangeKind.Structure or ChangeKind.Reset)
                Rebuild();
            else
                RefreshValues();
        };
        Rebuild();
    }

    /// <summary>What a widget needs set up outside the layout, shown as buttons when it's selected; none if not set.</summary>
    public Func<WidgetDescriptor, IReadOnlyList<WidgetSetup>>? Setups { get; set; }

    /// <summary>
    /// The names in a list the app keeps, by the name a setting gives it (<see cref="SettingDescriptor.OptionsSource"/>),
    /// such as the news feeds for the news widget's; none if not set.
    /// </summary>
    public Func<string, IReadOnlyList<string>>? Options { get; set; }

    /// <summary>
    /// For a name picked from one of those lists (by its name, then the name picked) that isn't spelt as one of its names
    /// now, the one it stands for, or null: a weather place picked as Paris before another Paris made it Paris
    /// (Île-de-France), say. None if not set.
    /// </summary>
    public Func<string, string, string?>? PickedOption { get; set; }

    public void Rebuild()
    {
        // A font still being tried in a drop-down that's going away goes back.
        _session.EndPreview(keep: false, this);
        Children.Clear();
        _refreshers.Clear();
        _liveRefreshers.Clear();
        _itemCards.Clear();

        if (_session.SelectedWidget is not { } widget)
        {
            AddHeading("Canvas and theme", "Select a widget in the preview or the list to edit it.");
            var canvas = AddGroup("Canvas");
            foreach (var setting in EditorDescriptors.Canvas)
                AddRow(canvas, SettingTarget.Canvas, setting);
            if (_dashboardSize() is { } size)
            {
                var match = new Button { Content = $"Match dashboard screen ({size.Width}×{size.Height})", Margin = new Thickness(LabelWidth, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                match.Click += (_, _) =>
                {
                    _session.SetValue(SettingTarget.Canvas, "width", LayoutSession.Number(size.Width), this);
                    _session.SetValue(SettingTarget.Canvas, "height", LayoutSession.Number(size.Height), this);
                };
                canvas.Children.Add(match);
            }
            AddThemePicker();
            AddSettings(SettingTarget.Theme, EditorDescriptors.Theme);
        }
        else
        {
            var target = SettingTarget.Widget(_session.SelectedIndex);
            var descriptor = WidgetFactory.Find(widget.GetString("type"));
            if (descriptor is null)
            {
                AddHeading($"Unknown widget '{widget.GetString("type")}'", "This widget type doesn't exist. Delete it, or fix the type in the layout file.");
                AddSettings(target, EditorDescriptors.Geometry);
            }
            else
            {
                AddHeading(descriptor.Name, descriptor.Description);
                AddSetups(descriptor);
                AddSettings(target, EditorDescriptors.Geometry);
                AddSettings(target, descriptor.Settings);
            }
        }

        RefreshValues();
    }

    /// <summary>Updates the live "what this shows now" previews under template and metric fields. Called once a second.</summary>
    public void RefreshLive()
    {
        foreach (var refresh in _liveRefreshers)
            refresh();
    }

    /// <summary>Updates every editor from the session, except the one being typed in.</summary>
    private void RefreshValues()
    {
        _updating = true;
        try
        {
            foreach (var refresh in _refreshers)
                refresh();
        }
        finally
        {
            _updating = false;
        }
    }

    private void AddHeading(string title, string description)
    {
        Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        Children.Add(Secondary(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }));
    }

    /// <summary>The widget's setup buttons (weather location, calendars…), each with what's set up now.</summary>
    private void AddSetups(WidgetDescriptor descriptor)
    {
        if (Setups?.Invoke(descriptor) is not { Count: > 0 } setups)
            return;
        var group = AddGroup("Set up");
        foreach (var setup in setups)
        {
            var status = Secondary(new TextBlock { Text = setup.Status(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            var button = new Button { Content = setup.Button, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 3, 10, 3) };
            button.Click += (_, _) =>
            {
                if (Window.GetWindow(this) is { } owner)
                    setup.Open(owner);
                // The button's text can change too ("Set location…" becomes "Change location…").
                Rebuild();
            };
            group.Children.Add(status);
            group.Children.Add(button);
        }
    }

    private StackPanel AddGroup(string title)
    {
        Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 16, 0, 4) });
        var group = new StackPanel();
        Children.Add(group);
        return group;
    }

    /// <summary>
    /// Which saved theme the layout uses, with Save theme as, Update theme and Detach. The theme settings below it are
    /// the layout's own values, which override the theme's; resetting one goes back to the theme's value.
    /// </summary>
    private void AddThemePicker()
    {
        const string own = "None (this layout's own)";
        var group = AddGroup("Theme");
        var themes = _session.Themes;
        var combo = new ComboBox { ItemsSource = themes.List().Prepend(own).ToList(), ItemTemplate = ThemeItemTemplate(themes) };
        combo.SelectionChanged += (_, _) =>
        {
            if (_updating || combo.SelectedItem is not string choice)
                return;
            var name = choice == own ? null : choice;
            if (!string.Equals(name, _session.ThemeName, StringComparison.OrdinalIgnoreCase))
                _session.UseTheme(name);
        };
        group.Children.Add(LabelledRow("Theme", combo,
            "A saved theme sets the font, colours and background. Changing a setting below changes it for this layout only; ↺ goes back to the theme's value."));

        var buttons = new WrapPanel { Margin = new Thickness(LabelWidth, 6, 0, 0) };
        var saveAs = new Button { Content = "Save theme as…", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Save this look as a theme other layouts can use" };
        var update = new Button { Content = "Update theme", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Save this layout's changes into the theme, restyling every layout that uses it" };
        var detach = new Button { Content = "Detach", ToolTip = "Copy the theme's values into this layout, so later changes to the theme don't affect it" };
        ToolTipService.SetShowOnDisabled(update, true);
        buttons.Children.Add(saveAs);
        buttons.Children.Add(update);
        buttons.Children.Add(detach);
        group.Children.Add(buttons);

        saveAs.Click += (_, _) =>
        {
            var initial = _session.ThemeName is { } current ? $"{current} copy" : "My theme";
            var name = NamePromptWindow.Ask(Window.GetWindow(this)!, "Save theme as", "Name for the new theme", initial, themes.CheckNewName);
            if (name is not null)
                SaveTheme(name);
        };
        update.Click += (_, _) =>
        {
            if (_session.ThemeName is not { } name)
                return;
            var answer = MessageBox.Show(Window.GetWindow(this)!, $"Save this layout's theme changes into \"{name}\"? Every layout using it will change.",
                "Impious Bonum", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.OK)
                SaveTheme(name);
        };
        detach.Click += (_, _) => _session.DetachTheme();

        _refreshers.Add(() =>
        {
            var name = _session.ThemeName;
            // A theme that doesn't exist (deleted, or a typo in the file) shows as nothing picked; the status bar explains.
            combo.SelectedItem = name is null ? own : themes.Find(name);
            update.IsEnabled = name is not null && !ThemeLibrary.IsBuiltIn(name) && themes.Find(name) is not null && _session.HasThemeOverrides;
            update.ToolTip = ThemeLibrary.IsBuiltIn(name)
                ? "Built-in themes can't be changed. Use Save theme as to make your own copy."
                : "Save this layout's changes into the theme, restyling every layout that uses it";
            detach.IsEnabled = name is not null;
        });
    }

    /// <summary>An icon's name after the icon itself.</summary>
    private static DataTemplate IconItemTemplate()
    {
        var sample = new FrameworkElementFactory(typeof(IconSample));
        sample.SetBinding(IconSample.IconNameProperty, new Binding());
        sample.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding());
        name.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(OrientationProperty, Orientation.Horizontal);
        row.AppendChild(sample);
        row.AppendChild(name);
        return new DataTemplate { VisualTree = row };
    }

    /// <summary>A theme's name after a sample of its colours, so a long list of themes is easy to scan.</summary>
    private static DataTemplate ThemeItemTemplate(ThemeLibrary themes)
    {
        var sample = new FrameworkElementFactory(typeof(ThemeSample));
        sample.SetValue(ThemeSample.LibraryProperty, themes);
        sample.SetBinding(ThemeSample.ThemeNameProperty, new Binding());
        sample.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding());
        name.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(OrientationProperty, Orientation.Horizontal);
        row.AppendChild(sample);
        row.AppendChild(name);
        return new DataTemplate { VisualTree = row };
    }

    private void SaveTheme(string name)
    {
        try
        {
            _session.SaveThemeAs(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Couldn't save the theme", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // The list of themes may have grown.
        Rebuild();
    }

    private static Grid LabelledRow(string label, FrameworkElement editor, string? help)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, ToolTip = help });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (help is not null)
        {
            var text = Secondary(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetRow(text, 1);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }

    private void AddSettings(SettingTarget target, IReadOnlyList<SettingDescriptor> settings)
    {
        foreach (var group in settings.GroupBy(s => s.Group))
        {
            var panel = AddGroup(group.Key);
            foreach (var setting in group)
            {
                if (setting.Kind == SettingKind.Items)
                    AddItems(panel, target, setting);
                else
                    AddRow(panel, target, setting);
            }
        }
    }

    private void AddRow(Panel parent, SettingTarget target, SettingDescriptor setting)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Beside the first line of an editor taller than one (a list of names to pick), not halfway down it.
        var tall = setting.Kind == SettingKind.Picks;
        var label = new TextBlock
        {
            Text = setting.Label,
            VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(0, tall ? 6 : 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = setting.Help,
        };
        grid.Children.Add(label);

        var (editor, refresh) = CreateEditor(target, setting);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        var reset = new Button
        {
            Content = Glyph("\uE7A7"),
            ToolTip = "Reset to default",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        reset.Click += (_, _) => _session.SetValue(target, setting.Key, null, this);
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);

        if (setting.Help is not null)
        {
            var help = Secondary(new TextBlock { Text = setting.Help, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetRow(help, 1);
            Grid.SetColumn(help, 1);
            grid.Children.Add(help);
        }

        parent.Children.Add(grid);
        _refreshers.Add(() =>
        {
            reset.Visibility = _session.GetValue(target, setting.Key) is null ? Visibility.Hidden : Visibility.Visible;
            refresh();
        });
    }

    private (FrameworkElement Editor, Action Refresh) CreateEditor(SettingTarget target, SettingDescriptor setting) => setting.Kind switch
    {
        SettingKind.Number => NumberEditor(target, setting),
        SettingKind.Toggle => ToggleEditor(target, setting),
        SettingKind.Choice or SettingKind.Icon => ChoiceEditor(target, setting),
        SettingKind.Color => ColorEditor(target, setting),
        SettingKind.Font => FontEditor(target, setting),
        SettingKind.FontFile => FileEditor(target, setting, "Fonts (*.ttf;*.otf)|*.ttf;*.otf|All files|*.*", "Choose a font file"),
        SettingKind.ImageFile when target.Kind == SettingTargetKind.Theme => FileEditor(target, setting,
            "Pictures, animated GIFs and videos (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.m4v;*.wmv)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.m4v;*.wmv|All files|*.*",
            "Choose a background picture, GIF or video"),
        SettingKind.ImageFile => FileEditor(target, setting, "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*", "Choose a background image"),
        SettingKind.Template or SettingKind.DriveTemplate or SettingKind.Metric => MetricTextEditor(target, setting),
        SettingKind.Picks => PicksEditor(target, setting),
        _ => TextEditor(target, setting),
    };

    // ---- Editors -------------------------------------------------------------------------------

    private (FrameworkElement, Action) TextEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new TextBox { IsUndoEnabled = false };
        box.TextChanged += (_, _) => Write(target, setting.Key, JsonValue.Create(box.Text));
        return (box, Refresh);

        void Refresh()
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
        }
    }

    /// <summary>
    /// A template or metric id, with a button that opens the metric picker and a live preview underneath
    /// ("→ 11.8 % / 16 threads") that also flags ids that don't exist. A per-drive template previews the first drive's row.
    /// </summary>
    private (FrameworkElement, Action) MetricTextEditor(SettingTarget target, SettingDescriptor setting)
    {
        var perDrive = setting.Kind == SettingKind.DriveTemplate;
        var isTemplate = setting.Kind == SettingKind.Template || perDrive;
        var panel = new StackPanel();
        var line = new DockPanel();
        var pick = new Button { Content = "{…}", ToolTip = isTemplate ? "Insert metric…" : "Choose metric…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        DockPanel.SetDock(pick, Dock.Right);
        var box = new TextBox { IsUndoEnabled = false, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        line.Children.Add(pick);
        line.Children.Add(box);
        var preview = Secondary(new TextBlock { FontSize = 11.5, Margin = new Thickness(2, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(line);
        panel.Children.Add(preview);

        void UpdatePreview()
        {
            if (_store() is not { } store)
            {
                preview.Visibility = Visibility.Collapsed;
                return;
            }

            var text = box.Text;
            if (text.Length == 0)
            {
                preview.Visibility = Visibility.Collapsed;
                return;
            }
            if (perDrive)
                text = DriveTemplate.Expand(text, PreviewDrive(store));

            var ids = isTemplate ? ValueTemplate.Parse(text).MetricIds.ToList() : [text.Trim()];
            var unknown = ids.Where(id => !store.TryGet(id, out _)).Distinct().ToList();
            preview.Visibility = Visibility.Visible;
            preview.SetResourceReference(TextBlock.ForegroundProperty, unknown.Count > 0 ? CriticalBrush : SecondaryBrush);
            preview.Text = unknown.Count > 0
                ? $"Unknown metric{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown)}"
                : "→ " + (isTemplate ? ValueTemplate.Parse(text).Render(store) : ValueTemplate.Parse($"{{{text.Trim()}}}").Render(store));
        }

        box.TextChanged += (_, _) =>
        {
            Write(target, setting.Key, JsonValue.Create(box.Text));
            UpdatePreview();
        };

        pick.Click += (_, _) =>
        {
            if (_store() is not { } store)
                return;
            var currentId = isTemplate ? null : box.Text.Trim();
            var picker = new MetricPickerWindow(store, placeholder: isTemplate, currentId) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.Result is not { } result)
                return;

            if (isTemplate)
            {
                // A drive's metric becomes a placeholder for each row's own drive.
                if (perDrive)
                    result = DriveTemplate.ForRow(result);
                // Insert at the caret, replacing any selected text.
                var caret = box.SelectionStart;
                box.Text = box.Text.Remove(caret, box.SelectionLength).Insert(caret, result);
                box.CaretIndex = caret + result.Length;
            }
            else
            {
                box.Text = result;
            }
            box.Focus();
        };

        _liveRefreshers.Add(UpdatePreview);
        return (panel, Refresh);

        void Refresh()
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
            UpdatePreview();
        }
    }

    /// <summary>The drive a per-drive template is previewed with: the first one the dashboard reports, else C.</summary>
    private static string PreviewDrive(MetricStore store) =>
        store.TryGet(Core.Providers.DriveProvider.Letters, out var letters) && letters.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries) is [var first, ..]
            ? first
            : "C";

    private (FrameworkElement, Action) NumberEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new TextBox { IsUndoEnabled = false };

        box.TextChanged += (_, _) =>
        {
            if (_updating)
                return;
            var text = box.Text.Trim();
            if (text.Length == 0)
            {
                box.ClearValue(Control.BorderBrushProperty);
                Write(target, setting.Key, null);
            }
            else if (TryParse(text, out var value))
            {
                box.ClearValue(Control.BorderBrushProperty);
                Write(target, setting.Key, LayoutSession.Number(Math.Clamp(value, setting.Min ?? double.MinValue, setting.Max ?? double.MaxValue)));
            }
            else
            {
                box.SetResourceReference(Control.BorderBrushProperty, CriticalBrush);
            }
        };

        // Up/Down step the value (Shift for 10), which is handy for sizes and positions.
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down))
                return;
            var step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) * (e.Key == Key.Up ? 1 : -1);
            var current = TryParse(box.Text, out var v) ? v : setting.Default as double? ?? 0;
            box.Text = Format(Math.Clamp(current + step, setting.Min ?? double.MinValue, setting.Max ?? double.MaxValue));
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };

        return (box, Refresh);

        void Refresh()
        {
            if (box.IsKeyboardFocusWithin)
                return;
            var current = _session.GetValue(target, setting.Key);
            box.Text = JsonDefaults.TryGetNumber(current, out var n) ? Format(n)
                : DefaultOf(target, setting) is double d ? Format(d)
                : string.Empty;
            box.ClearValue(Control.BorderBrushProperty);
        }
    }

    private (FrameworkElement, Action) ToggleEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => Write(target, setting.Key, JsonValue.Create(box.IsChecked == true));
        return (box, Refresh);

        void Refresh()
        {
            var current = _session.GetValue(target, setting.Key);
            box.IsChecked = current is JsonValue v && v.TryGetValue<bool>(out var b) ? b : setting.Default is true;
        }
    }

    private (FrameworkElement, Action) ChoiceEditor(SettingTarget target, SettingDescriptor setting)
    {
        var combo = new ComboBox { ItemsSource = setting.Choices };
        if (setting.Kind == SettingKind.Icon)
            combo.ItemTemplate = IconItemTemplate();
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string choice)
                Write(target, setting.Key, JsonValue.Create(choice));
        };
        return (combo, () => combo.SelectedItem = CurrentString(target, setting));
    }

    private (FrameworkElement, Action) FileEditor(SettingTarget target, SettingDescriptor setting, string filter, string title)
    {
        var panel = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        var box = new TextBox { IsUndoEnabled = false };
        panel.Children.Add(browse);
        panel.Children.Add(box);

        box.TextChanged += (_, _) => Write(target, setting.Key, box.Text.Length == 0 ? null : JsonValue.Create(box.Text));
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = title };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                _session.SetValue(target, setting.Key, JsonValue.Create(dialog.FileName), this);
        };
        return (panel, Refresh);

        void Refresh()
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
        }
    }

    private (FrameworkElement, Action) ColorEditor(SettingTarget target, SettingDescriptor setting)
    {
        var panel = new DockPanel();
        var swatch = new Button { Width = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pick a colour" };
        swatch.SetResourceReference(Control.BorderBrushProperty, "TextFillColorTertiaryBrush");
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);

        // Named choices (foreground/secondary/accent) as a drop-down when allowed; any #hex can be typed either way.
        Control input;
        Func<string> read;
        Action<string> show;
        if (setting.Choices is { Count: > 0 } choices)
        {
            var combo = new ComboBox { ItemsSource = choices, IsEditable = true };
            combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => WriteIfChanged(target, setting, combo.Text)));
            input = combo;
            read = () => combo.Text;
            show = text => combo.Text = text;
        }
        else
        {
            var box = new TextBox { IsUndoEnabled = false };
            box.TextChanged += (_, _) => Write(target, setting.Key, JsonValue.Create(box.Text));
            input = box;
            read = () => box.Text;
            show = text => box.Text = text;
        }
        panel.Children.Add(input);

        swatch.Click += (_, _) =>
        {
            var current = ResolveColor(read()) ?? Colors.White;
            using var dialog = new WinForms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) };
            // Owned by the editor, so it stays in front of it and the editor waits for it.
            if (dialog.ShowDialog(Owner.Of(Window.GetWindow(this))) != WinForms.DialogResult.OK)
                return;
            var picked = dialog.Color;
            // The dialog has no alpha; keep whatever transparency the colour already had.
            var hex = current.A == 0xFF ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}" : $"#{current.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            _session.SetValue(target, setting.Key, JsonValue.Create(hex), this);
        };

        return (panel, Refresh);

        void Refresh()
        {
            var value = CurrentString(target, setting);
            if (!input.IsKeyboardFocusWithin)
                show(value);
            swatch.Background = ResolveColor(input.IsKeyboardFocusWithin ? read() : value) is { } color ? new SolidColorBrush(color) : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Some names from a list, in order: a box for each, the ticked ones first in their order, dragged into place by their
    /// handles (or Alt+Up/Alt+Down). Where "all" is allowed, a box for it comes first and greys out the rest, and the last
    /// name ticked stays until another (or all) is, so a swap never goes through all. A ticked name that isn't an option
    /// any more (a feed since removed or renamed) still shows, marked, so it can be unticked. An option whose name can't
    /// be stored on its own (<see cref="PickList.CanPick"/>) is greyed out, saying why. Each change is an undo step of
    /// its own.
    /// </summary>
    private (FrameworkElement, Action) PicksEditor(SettingTarget target, SettingDescriptor setting)
    {
        var allowAll = setting.AllLabel is not null;
        var panel = new StackPanel();
        var all = new CheckBox { Content = setting.AllLabel, Visibility = allowAll ? Visibility.Visible : Visibility.Collapsed };
        var entries = new StackPanel();
        var none = Secondary(new TextBlock { Text = setting.NoOptions ?? "Nothing to choose from yet.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        panel.Children.Add(all);
        panel.Children.Add(entries);
        panel.Children.Add(none);

        // What the boxes show, so they're only made afresh when it changes; and how many are ticked, which can be reordered.
        string? shown = null;
        var ticked = 0;

        // Checked and Unchecked rather than Click, so Narrator ticking a box counts too. Unticking all starts from every
        // option ticked that can be, so little changes on screen until some are unticked.
        all.Checked += (_, _) => Save(PickList.Everything);
        all.Unchecked += (_, _) => Save(PickList.Of(OptionsOf(setting).Where(o => PickList.CanPick(o, allowAll))));
        DragReorder.Attach(entries, (from, to) =>
        {
            // Only the ticked ones have an order, and they stay above the rest, so moving the last of them down leaves it be.
            to = Math.Min(to, ticked - 1);
            if (from < ticked && to != from)
                Save(Current().Move(from, to));
        }, byHandle: true);
        return (panel, Refresh);

        // The names as the options spell them, so a change also tidies "bbc" to "BBC".
        PickList Current()
        {
            var value = PickList.Parse(CurrentString(target, setting), allowAll);
            return value.All ? value : PickList.Of(value.Entries(OptionsOf(setting), FindOf(setting)).Where(e => e.Ticked).Select(e => e.Name));
        }

        void Save(PickList value)
        {
            if (!_updating)
                _session.SetValue(target, setting.Key, JsonValue.Create(value.ToString()), this, merge: false);
        }

        void Refresh()
        {
            var options = OptionsOf(setting);
            var value = Current();
            var key = string.Join("\n", options.Prepend(value.ToString()));
            if (key == shown)
                return;
            shown = key;

            // The box or handle with focus is made afresh too; focus goes to the new one for the same name.
            var focused = Keyboard.FocusedElement as FrameworkElement;
            var refocus = focused is { Tag: string name } && entries.IsAncestorOf(focused) ? (Name: name, Handle: DragReorder.GetIsHandle(focused), Index: IndexIn(entries, focused)) : default;

            var list = value.Entries(options, FindOf(setting));
            ticked = value.All ? 0 : list.Count(e => e.Ticked);
            all.IsChecked = value.All;
            // With nothing that can be picked, unticking all would leave nothing ticked, which is all again.
            all.IsEnabled = !(value.All && !options.Any(o => PickList.CanPick(o, allowAll)));
            entries.IsEnabled = !value.All;
            none.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            entries.Children.Clear();
            foreach (var entry in list)
                entries.Children.Add(PickRow(entry, reorderable: entry.Ticked && !value.All, last: allowAll && ticked == 1 && entry.Ticked));

            if (refocus.Name is null)
                return;
            // Laid out first, so the new boxes can take focus. A name that's gone (unticked, and not an option) leaves it
            // with the one that took its place.
            UpdateLayout();
            var rows = entries.Children.OfType<DockPanel>().ToList();
            var row = rows.FirstOrDefault(r => r.Tag as string == refocus.Name) ?? (rows.Count > 0 ? rows[Math.Clamp(refocus.Index, 0, rows.Count - 1)] : null);
            if (!(refocus.Handle && row?.Children[0].Focus() == true) && row?.Children[1].Focus() != true)
                all.Focus();
        }

        // last: the only one ticked, which stays until another is (where unticking it would go back to all).
        DockPanel PickRow(PickEntry entry, bool reorderable, bool last)
        {
            var row = new DockPanel { Tag = entry.Name };
            var handle = DragHandle(entry.Name);
            handle.Tag = entry.Name;
            // Hidden rather than left out, so every name lines up.
            handle.Visibility = reorderable ? Visibility.Visible : Visibility.Hidden;
            DockPanel.SetDock(handle, Dock.Left);
            // A text block rather than the name itself, so an underscore in it shows rather than marking a shortcut key.
            var label = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            var canPick = PickList.CanPick(entry.Name, allowAll);
            var box = new CheckBox { IsChecked = entry.Ticked, Tag = entry.Name, Content = label, IsEnabled = !last && (entry.Ticked || canPick) };
            // A greyed-out box still says why.
            ToolTipService.SetShowOnDisabled(box, true);
            AutomationProperties.SetName(box, entry.Name);
            var untick = last ? $"To untick it, first tick another or {setting.AllLabel}." : "Untick it to take it off.";
            if (!entry.Found)
            {
                var missing = new Run(" (not found)");
                missing.SetResourceReference(TextElement.ForegroundProperty, CriticalBrush);
                label.Inlines.Add(missing);
                box.ToolTip = $"This isn't one of the choices any more: it may have been removed or renamed. {untick}";
                AutomationProperties.SetName(box, $"{entry.Name} (not found)");
            }
            else if (!canPick)
            {
                var why = entry.Name.Contains(',') ? "Its name has a comma in it" : "Its name means all of them";
                box.ToolTip = $"{why}, so it can't be chosen on its own. To choose it, remove it and add it again with another name.";
            }
            else if (last)
            {
                box.ToolTip = untick;
            }
            box.Checked += (_, _) => Save(Current().Tick(entry.Name));
            box.Unchecked += (_, _) => Save(Current().Untick(entry.Name, allowAll));
            row.Children.Add(handle);
            row.Children.Add(box);
            return row;
        }
    }

    /// <summary>What a <see cref="SettingKind.Picks"/> setting can pick from: its own choices, or the app's list it names.</summary>
    private IReadOnlyList<string> OptionsOf(SettingDescriptor setting) =>
        setting.Choices ?? (setting.OptionsSource is { } source ? Options?.Invoke(source) : null) ?? [];

    /// <summary>For a name picked from the app's list that's spelt another way now, the one it stands for (see <see cref="PickedOption"/>).</summary>
    private Func<string, string?>? FindOf(SettingDescriptor setting) =>
        setting.OptionsSource is { } source && PickedOption is { } find ? name => find(source, name) : null;

    /// <summary>Which of a panel's children an element is in, or -1.</summary>
    private static int IndexIn(Panel panel, DependencyObject element)
    {
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (VisualTreeHelper.GetParent(e) == panel)
                return panel.Children.IndexOf((UIElement)e);
        }
        return -1;
    }

    private void AddItems(Panel parent, SettingTarget target, SettingDescriptor setting)
    {
        var items = _session.GetValue(target, setting.Key) as JsonArray ?? [];
        // The entries in a stack of their own, which they're dragged around in by their handles.
        var entries = new StackPanel();
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var title = $"{setting.Label} {index + 1}";
            var card = new StackPanel();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var handle = DragHandle(title);
            DockPanel.SetDock(handle, Dock.Left);
            var remove = ItemButton("\uE74D", "Remove", () => _session.EditItems(target.WidgetIndex, setting.Key, a => a.RemoveAt(index)));
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(handle);
            header.Children.Add(remove);
            header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            card.Children.Add(header);

            foreach (var itemSetting in setting.ItemSettings ?? [])
                AddRow(card, SettingTarget.Item(target.WidgetIndex, setting.Key, index), itemSetting);

            var border = new Border { Child = card, Margin = new Thickness(0, 0, 0, 8) };
            border.SetResourceReference(StyleProperty, "Card");
            entries.Children.Add(border);
            _itemCards[(setting.Key, index)] = border;
        }
        DragReorder.Attach(entries, (from, to) => MoveItem(target, setting.Key, from, to), byHandle: true);
        parent.Children.Add(entries);

        var add = new Button { Content = WithGlyph("\uE710", $"Add {setting.Label.ToLowerInvariant().TrimEnd('s')}"), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            _session.EditItems(target.WidgetIndex, setting.Key, a => a.Add(new JsonObject()));
            Rebuild();
        };
        parent.Children.Add(add);
    }

    /// <summary>The grip a list entry is dragged by, at the left of its card.</summary>
    private static Border DragHandle(string title) => DragReorder.Handle(title, new Thickness(2, 4, 8, 4));

    /// <summary>
    /// Moves a list entry (dragged, or Alt+Up/Alt+Down), then gives focus back to the same control in it once the panel
    /// is rebuilt: its handle, or the field that was being typed in.
    /// </summary>
    private void MoveItem(SettingTarget target, string key, int from, int to)
    {
        var focused = _itemCards.TryGetValue((key, from), out var card) ? Focusables(card).IndexOf(Keyboard.FocusedElement) : -1;
        _session.MoveItem(target.WidgetIndex, key, from, to);
        // The cards are numbered by place, so they're drawn afresh.
        Rebuild();
        if (!_itemCards.TryGetValue((key, to), out card))
            return;
        // Laid out first, so its controls are all there and the scroll viewer brings the right place into view.
        UpdateLayout();
        var focusables = Focusables(card);
        if (focusables.Count > 0)
            focusables[Math.Clamp(focused, 0, focusables.Count - 1)].Focus();
    }

    /// <summary>The elements inside a card that can take focus, in order. Every card of a list has the same ones.</summary>
    private static List<IInputElement> Focusables(DependencyObject root)
    {
        var found = new List<IInputElement>();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Focusable: true } element)
                found.Add(element);
            found.AddRange(Focusables(child));
        }
        return found;
    }

    private Button ItemButton(string symbol, string tip, Action action)
    {
        var button = new Button { Content = Glyph(symbol), ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            action();
            // Changing how many entries there are needs a fresh panel.
            Rebuild();
        };
        return button;
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private void Write(SettingTarget target, string key, JsonNode? value)
    {
        if (_updating)
            return;
        _session.SetValue(target, key, value, this);
    }

    /// <summary>
    /// For editable drop-downs. Their text box catches up with the value a moment after it's shown, outside the
    /// <see cref="_updating"/> guard, so only text that differs from what the setting shows already is written: looking at
    /// a widget mustn't copy its defaults into the layout (and ask to save changes nobody made).
    /// </summary>
    private void WriteIfChanged(SettingTarget target, SettingDescriptor setting, string text)
    {
        if (text != CurrentString(target, setting))
            Write(target, setting.Key, JsonValue.Create(text));
    }

    private string CurrentString(SettingTarget target, SettingDescriptor setting) =>
        _session.GetValue(target, setting.Key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : DefaultOf(target, setting) as string ?? string.Empty;

    /// <summary>What an unset setting shows: its default, or for the theme, the value from the layout's named theme.</summary>
    private object? DefaultOf(SettingTarget target, SettingDescriptor setting)
    {
        if (target.Kind != SettingTargetKind.Theme || _session.GetResolvedThemeValue(setting.Key) is not JsonValue value)
            return setting.Default;
        if (value.TryGetValue<string>(out var text))
            return text;
        return JsonDefaults.TryGetNumber(value, out var number) ? number : setting.Default;
    }

    private Color? ResolveColor(string value)
    {
        var theme = _session.ResolvedTheme;
        value = value.Trim().ToLowerInvariant() switch
        {
            "foreground" => theme.Foreground,
            "secondary" => theme.Secondary,
            "accent" => theme.Accent,
            "warning" => theme.Warning,
            "critical" => theme.Critical,
            "track" => theme.Track,
            "background" => theme.Background,
            _ => value,
        };
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private const string SecondaryBrush = "TextFillColorSecondaryBrush";
    private const string CriticalBrush = "SystemFillColorCriticalBrush";

    /// <summary>Less important text (help, live previews) in the theme's secondary colour.</summary>
    private static TextBlock Secondary(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, SecondaryBrush);
        return text;
    }

    /// <summary>An icon from the icon font (see Styles.xaml), for small buttons.</summary>
    private static TextBlock Glyph(string glyph)
    {
        var text = new TextBlock { Text = glyph };
        text.SetResourceReference(StyleProperty, "Glyph");
        return text;
    }

    private static StackPanel WithGlyph(string glyph, string label)
    {
        var icon = Glyph(glyph);
        icon.Margin = new Thickness(0, 0, 8, 0);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = label });
        return panel;
    }

    /// <summary>A WPF window as the owner of a Windows Forms dialog.</summary>
    private sealed class Owner(nint handle) : WinForms.IWin32Window
    {
        public nint Handle => handle;

        public static WinForms.IWin32Window? Of(Window? window) =>
            window is null ? null : new Owner(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    }
}

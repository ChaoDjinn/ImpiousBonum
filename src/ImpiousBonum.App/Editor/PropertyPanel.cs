using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using WinForms = System.Windows.Forms;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Shows the settings of the current selection (a widget, or the canvas and theme when nothing is selected),
/// generated from descriptors. Each row has an editor that suits the setting's kind and a reset-to-default button.
/// </summary>
public sealed class PropertyPanel : StackPanel
{
    private const double LabelWidth = 128;

    private readonly LayoutSession _session;
    private readonly Func<(int Width, int Height)?> _dashboardSize;
    private readonly Func<MetricStore?> _store;
    private readonly List<Action> _refreshers = [];
    private readonly List<Action> _liveRefreshers = [];
    private bool _updating;
    private static IReadOnlyList<string>? _fontNames;

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

    public void Rebuild()
    {
        Children.Clear();
        _refreshers.Clear();
        _liveRefreshers.Clear();

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
        var combo = new ComboBox { ItemsSource = themes.List().Prepend(own).ToList() };
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

        var label = new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = setting.Help };
        grid.Children.Add(label);

        var (editor, refresh) = CreateEditor(target, setting);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        var reset = new Button { Content = "↺", ToolTip = "Reset to default", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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
        SettingKind.ImageFile => FileEditor(target, setting, "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*", "Choose a background image"),
        SettingKind.Template or SettingKind.Metric => MetricTextEditor(target, setting),
        _ => TextEditor(target, setting),
    };

    // ---- Editors -------------------------------------------------------------------------------

    private (FrameworkElement, Action) TextEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new TextBox { IsUndoEnabled = false };
        box.TextChanged += (_, _) => Write(target, setting.Key, JsonValue.Create(box.Text));
        return (box, () =>
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
        });
    }

    /// <summary>
    /// A template or metric id, with a button that opens the metric picker and a live preview underneath
    /// ("→ 11.8 % / 16 threads") that also flags ids that don't exist.
    /// </summary>
    private (FrameworkElement, Action) MetricTextEditor(SettingTarget target, SettingDescriptor setting)
    {
        var isTemplate = setting.Kind == SettingKind.Template;
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

            var ids = isTemplate ? ValueTemplate.Parse(text).MetricIds.ToList() : [text.Trim()];
            var unknown = ids.Where(id => !store.TryGet(id, out _)).Distinct().ToList();
            preview.Visibility = Visibility.Visible;
            preview.Foreground = unknown.Count > 0 ? Brushes.IndianRed : null;
            if (preview.Foreground is null)
                preview.ClearValue(TextBlock.ForegroundProperty);
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
        return (panel, () =>
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
            UpdatePreview();
        });
    }

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
                box.BorderBrush = Brushes.IndianRed;
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

        return (box, () =>
        {
            if (box.IsKeyboardFocusWithin)
                return;
            var current = _session.GetValue(target, setting.Key);
            box.Text = JsonDefaults.TryGetNumber(current, out var n) ? Format(n)
                : DefaultOf(target, setting) is double d ? Format(d)
                : string.Empty;
            box.ClearValue(Control.BorderBrushProperty);
        });
    }

    private (FrameworkElement, Action) ToggleEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => Write(target, setting.Key, JsonValue.Create(box.IsChecked == true));
        return (box, () =>
        {
            var current = _session.GetValue(target, setting.Key);
            box.IsChecked = current is JsonValue v && v.TryGetValue<bool>(out var b) ? b : setting.Default is true;
        });
    }

    private (FrameworkElement, Action) ChoiceEditor(SettingTarget target, SettingDescriptor setting)
    {
        var combo = new ComboBox { ItemsSource = setting.Choices };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string choice)
                Write(target, setting.Key, JsonValue.Create(choice));
        };
        return (combo, () => combo.SelectedItem = CurrentString(target, setting));
    }

    private (FrameworkElement, Action) FontEditor(SettingTarget target, SettingDescriptor setting)
    {
        _fontNames ??= Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().Order().ToList();
        var combo = new ComboBox { ItemsSource = _fontNames, IsEditable = true, IsTextSearchEnabled = true };
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Write(target, setting.Key, JsonValue.Create(combo.Text))));
        return (combo, () =>
        {
            if (!combo.IsKeyboardFocusWithin)
                combo.Text = CurrentString(target, setting);
        });
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
        return (panel, () =>
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
        });
    }

    private (FrameworkElement, Action) ColorEditor(SettingTarget target, SettingDescriptor setting)
    {
        var panel = new DockPanel();
        var swatch = new Button { Width = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pick a colour", BorderBrush = Brushes.Gray };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);

        // Named choices (foreground/secondary/accent) as a drop-down when allowed; any #hex can be typed either way.
        Control input;
        Func<string> read;
        Action<string> show;
        if (setting.Choices is { Count: > 0 } choices)
        {
            var combo = new ComboBox { ItemsSource = choices, IsEditable = true };
            combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Write(target, setting.Key, JsonValue.Create(combo.Text))));
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
            if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                return;
            var picked = dialog.Color;
            // The dialog has no alpha; keep whatever transparency the colour already had.
            var hex = current.A == 0xFF ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}" : $"#{current.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            _session.SetValue(target, setting.Key, JsonValue.Create(hex), this);
        };

        return (panel, () =>
        {
            var value = CurrentString(target, setting);
            if (!input.IsKeyboardFocusWithin)
                show(value);
            swatch.Background = ResolveColor(input.IsKeyboardFocusWithin ? read() : value) is { } color ? new SolidColorBrush(color) : Brushes.Transparent;
        });
    }

    private void AddItems(Panel parent, SettingTarget target, SettingDescriptor setting)
    {
        var items = _session.GetValue(target, setting.Key) as JsonArray ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var card = new StackPanel();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            buttons.Children.Add(ItemButton("↑", "Move up", index > 0, () => _session.EditItems(target.WidgetIndex, setting.Key, a => Swap(a, index, index - 1))));
            buttons.Children.Add(ItemButton("↓", "Move down", index < items.Count - 1, () => _session.EditItems(target.WidgetIndex, setting.Key, a => Swap(a, index, index + 1))));
            buttons.Children.Add(ItemButton("✕", "Remove", true, () => _session.EditItems(target.WidgetIndex, setting.Key, a => a.RemoveAt(index))));
            header.Children.Add(buttons);
            header.Children.Add(new TextBlock { Text = $"{setting.Label} {index + 1}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            card.Children.Add(header);

            foreach (var itemSetting in setting.ItemSettings ?? [])
                AddRow(card, SettingTarget.Item(target.WidgetIndex, setting.Key, index), itemSetting);

            parent.Children.Add(new Border
            {
                Child = card,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                CornerRadius = new CornerRadius(6),
            });
        }

        var add = new Button { Content = $"Add {setting.Label.ToLowerInvariant().TrimEnd('s')}", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            _session.EditItems(target.WidgetIndex, setting.Key, a => a.Add(new JsonObject()));
            Rebuild();
        };
        parent.Children.Add(add);
    }

    private Button ItemButton(string symbol, string tip, bool enabled, Action action)
    {
        var button = new Button { Content = symbol, ToolTip = tip, IsEnabled = enabled, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            action();
            // Changing how many entries there are needs a fresh panel.
            Rebuild();
        };
        return button;
    }

    private static void Swap(JsonArray array, int a, int b)
    {
        var first = array[a]!.DeepClone();
        var second = array[b]!.DeepClone();
        array[a] = second;
        array[b] = first;
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private void Write(SettingTarget target, string key, JsonNode? value)
    {
        if (_updating)
            return;
        _session.SetValue(target, key, value, this);
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

    private static TextBlock Secondary(TextBlock text)
    {
        text.Opacity = 0.7;
        return text;
    }
}

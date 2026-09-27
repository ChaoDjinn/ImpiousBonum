using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImpiousBonum.App.Widgets;
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
    private readonly List<Action> _refreshers = [];
    private bool _updating;
    private static IReadOnlyList<string>? _fontNames;

    public PropertyPanel(LayoutSession session, Func<(int Width, int Height)?> dashboardSize)
    {
        _session = session;
        _dashboardSize = dashboardSize;
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
            AddSettings(SettingTarget.Theme, EditorDescriptors.Theme);
        }
        else
        {
            var target = SettingTarget.Widget(_session.SelectedIndex);
            var descriptor = WidgetFactory.Find(widget.GetString("type"));
            if (descriptor is null)
            {
                AddHeading($"Unknown widget '{widget.GetString("type")}'", "This widget type doesn't exist. Delete it, or fix the type in layout.json.");
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
        SettingKind.FontFile => FontFileEditor(target, setting),
        _ => TextEditor(target, setting),
    };

    // ---- Editors -------------------------------------------------------------------------------

    private (FrameworkElement, Action) TextEditor(SettingTarget target, SettingDescriptor setting)
    {
        var box = new TextBox { IsUndoEnabled = false };
        if (setting.Kind is SettingKind.Template or SettingKind.Metric)
            box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        box.TextChanged += (_, _) => Write(target, setting.Key, JsonValue.Create(box.Text));
        return (box, () =>
        {
            if (!box.IsKeyboardFocusWithin)
                box.Text = CurrentString(target, setting);
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
                : setting.Default is double d ? Format(d)
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

    private (FrameworkElement, Action) FontFileEditor(SettingTarget target, SettingDescriptor setting)
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
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf|All files|*.*", Title = "Choose a font file" };
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
        _session.GetValue(target, setting.Key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : setting.Default as string ?? string.Empty;

    private Color? ResolveColor(string value)
    {
        var theme = _session.Document.Theme;
        value = value.Trim().ToLowerInvariant() switch
        {
            "foreground" => theme.Foreground,
            "secondary" => theme.Secondary,
            "accent" => theme.Accent,
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

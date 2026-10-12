using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Editor;

// The font picker.
public sealed partial class PropertyPanel
{
    private static IReadOnlyList<FontChoice>? _fontsWithTheme;
    private int _fontRuns;

    /// <summary>
    /// An editable drop-down of fonts, each drawn in itself. While it's open, the font under the mouse or picked with the
    /// arrow keys shows on the widget straight away as a preview: Enter or a click keeps it (one undo step), and Esc or a
    /// click anywhere else puts the font back. Closed, Up and Down step through the fonts as one undo step, and typing a
    /// name works as before.
    /// </summary>
    private (FrameworkElement, Action) FontEditor(SettingTarget target, SettingDescriptor setting)
    {
        // A widget's own font can be empty, meaning the theme's, so that's offered first.
        var themeFont = setting.Default is null;
        var fonts = themeFont ? _fontsWithTheme ??= [FontChoice.ThemeFont, .. FontChoice.Installed] : FontChoice.Installed;
        var combo = new ComboBox
        {
            ItemsSource = fonts,
            IsEditable = true,
            IsTextSearchEnabled = true,
            // Hundreds of fonts: only the rows on screen are made, and reused as the list scrolls.
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))),
        };
        combo.ItemTemplate = FontItemTemplate(combo);
        TextSearch.SetTextPath(combo, nameof(FontChoice.Name));
        VirtualizingPanel.SetIsVirtualizing(combo, true);
        VirtualizingPanel.SetVirtualizationMode(combo, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(combo, true);
        if (themeFont)
            combo.Loaded += (_, _) =>
            {
                if (EditableBox(combo) is { } box)
                    Placeholder.SetText(box, "Theme font");
            };

        var trying = false;
        var keep = false;
        var run = string.Empty;

        // Empty is the theme's font, stored as nothing (like ↺) rather than as an empty name.
        JsonNode? ValueOf(string name) => themeFont && name.Length == 0 ? null : JsonValue.Create(name);
        void Try(string name)
        {
            // Not from a drop-down left open when the panel was rebuilt (undo while it's open), whose setting may be gone.
            if (combo.IsDescendantOf(this))
                _session.Preview(target, setting.Key, ValueOf(name), this);
        }

        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (_updating)
                return;
            if (trying)
                Try(combo.Text);
            // As WriteIfChanged: only text that differs from what the setting shows already is a change.
            else if (combo.Text != CurrentString(target, setting))
                _session.SetValue(target, setting.Key, ValueOf(combo.Text), this);
        }));

        // While it's open, what it points at is only a preview, until it closes.
        combo.DropDownOpened += (_, _) => (trying, keep) = (true, false);
        combo.AddHandler(MouseMoveEvent, new MouseEventHandler((_, e) =>
        {
            // The font under the mouse; off the list, the one in the box.
            if (trying)
                Try(FontAt(combo, e.OriginalSource)?.Name ?? combo.Text);
        }), handledEventsToo: true);

        // An editable drop-down handles keys as they come in, so keys and clicks are seen first on the way to it.
        var host = new Border { Child = combo };

        // How it closes says whether to keep the font: a click on one or Enter does; Esc or a click anywhere else doesn't.
        host.PreviewMouseDown += (_, _) => keep = false;
        host.PreviewMouseUp += (_, e) => keep |= trying && FontAt(combo, e.OriginalSource) is not null;
        host.PreviewKeyDown += (_, e) =>
        {
            if (combo.IsDropDownOpen)
            {
                if (e.Key == Key.Escape)
                    keep = false;
                else if (e.Key is Key.Enter or Key.Tab or Key.F4 || (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down))
                    keep = true;
            }
            else if (e.Key is Key.Up or Key.Down)
            {
                Step(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
            }
        };
        combo.DropDownClosed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            // Once Enter's font is in the box, which comes just after the drop-down closes.
            trying = false;
            if (!combo.IsDescendantOf(this))
                return;
            if (keep)
            {
                Try(combo.Text);
                _session.EndPreview(keep: true, this);
            }
            else
            {
                _session.EndPreview(keep: false, this);
                ShowCurrent();
            }
        });

        // Each visit to the box is a new run of steps, so coming back to it later is a new undo step.
        combo.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (combo.IsKeyboardFocusWithin)
                run = $"font{++_fontRuns}";
        };

        return (host, Refresh);

        // Up and Down with it closed: the next font goes straight on, and the steps are one undo step.
        void Step(int by)
        {
            var choice = fonts[Math.Clamp(IndexOf(fonts, combo.Text) + by, 0, fonts.Count - 1)];
            _session.SetValue(target, setting.Key, ValueOf(choice.Name), this, run);
            ShowCurrent();
            EditableBox(combo)?.SelectAll();
        }

        void ShowCurrent()
        {
            _updating = true;
            try
            {
                combo.Text = CurrentString(target, setting);
            }
            finally
            {
                _updating = false;
            }
        }

        void Refresh()
        {
            if (!trying && !combo.IsKeyboardFocusWithin)
                combo.Text = CurrentString(target, setting);
        }
    }

    /// <summary>
    /// Each font drawn in itself, all as wide as the box: with only the rows on screen made, rows sized to their names
    /// would widen and narrow the list as it scrolls.
    /// </summary>
    private static DataTemplate FontItemTemplate(ComboBox combo)
    {
        var sample = new FrameworkElementFactory(typeof(FontSample));
        sample.SetBinding(FontSample.ChoiceProperty, new Binding());
        sample.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = combo });
        return new DataTemplate { VisualTree = sample };
    }

    /// <summary>The font in the drop-down that <paramref name="source"/> (what the mouse is over) is part of, if any.</summary>
    private static FontChoice? FontAt(ComboBox combo, object source) =>
        source is DependencyObject element && ItemsControl.ContainerFromElement(combo, element) is ComboBoxItem { Content: FontChoice choice } ? choice : null;

    private static int IndexOf(IReadOnlyList<FontChoice> fonts, string name)
    {
        name = name.Trim();
        for (var i = 0; i < fonts.Count; i++)
        {
            if (string.Equals(fonts[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>The text box an editable drop-down is typed in.</summary>
    private static TextBox? EditableBox(ComboBox combo) => combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox;
}

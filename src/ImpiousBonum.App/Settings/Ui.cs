using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Controls;

namespace ImpiousBonum.App.Settings;

/// <summary>The pieces the pages build their cards from in code: switches, lists of choices and buttons that act at once.</summary>
internal static class Ui
{
    public static SettingsCard Card(string? glyph, string header, string? description, object? content = null, object? detail = null) =>
        new() { Glyph = glyph, Header = header, Description = description, Content = content, Detail = detail };

    /// <summary>A toggle switch that calls <paramref name="changed"/> with its new state when flipped.</summary>
    public static CheckBox Switch(bool on, string name, Action<bool> changed)
    {
        var box = SettingsWindow.Styled(new CheckBox { IsChecked = on }, "ToggleSwitch");
        AutomationProperties.SetName(box, name);
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>A list of choices showing <paramref name="current"/>, which calls <paramref name="chosen"/> when another is picked.</summary>
    public static ComboBox Choice<T>(IEnumerable<(T Value, string Label)> options, T current, string name, Action<T> chosen, double width = 240)
    {
        var list = options.ToList();
        var box = new ComboBox
        {
            Width = width,
            ItemsSource = list.Select(o => o.Label).ToList(),
            SelectedIndex = list.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)),
        };
        AutomationProperties.SetName(box, name);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
                chosen(list[box.SelectedIndex].Value);
        };
        return box;
    }

    public static Button Button(string text, Action click, string? glyph = null, bool accent = false)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(SettingsWindow.Styled(new TextBlock { Text = glyph }, "ButtonGlyph"));
            row.Children.Add(new TextBlock { Text = text });
            content = row;
        }
        var button = new Button { Content = content, MinWidth = 96 };
        AutomationProperties.SetName(button, text);
        if (accent)
            SettingsWindow.Styled(button, "AccentButtonStyle");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Controls side by side, for a card with more than one.</summary>
    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement element)
                element.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(children[i]);
        }
        return row;
    }

    public static TextBlock Heading(string text) => SettingsWindow.Styled(new TextBlock { Text = text }, "GroupHeading");

    /// <summary>A line of small print between cards.</summary>
    public static TextBlock Note(string text) => SettingsWindow.Styled(new TextBlock { Text = text, Margin = new Thickness(2, 6, 0, 10) }, "NoteText");

    /// <summary>Opens a web page or a folder in whatever Windows uses for it.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nothing registered to open it; nothing else to offer.
        }
    }
}

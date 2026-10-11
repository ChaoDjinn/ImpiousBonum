using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Hotkeys…: a box per action (and per layout) that takes the key combination pressed in it.</summary>
public partial class HotkeysWindow : Window
{
    private readonly List<(Hotkey Action, TextBox Box)> _boxes = [];

    internal HotkeysWindow(IReadOnlyList<Hotkey> current, IReadOnlyList<string> layouts)
    {
        InitializeComponent();
        var actions = new List<Hotkey>
        {
            new(Hotkey.ShowHide, ""),
            new(Hotkey.NextPage, ""),
            new(Hotkey.PreviousPage, ""),
            new(Hotkey.Edit, ""),
        };
        actions.AddRange(layouts.Select(name => new Hotkey(Hotkey.SwitchLayout, "", name)));
        // Keep hotkeys for layouts that are gone, so a renamed layout's key can be seen and cleared.
        actions.AddRange(current.Where(h => h.Action == Hotkey.SwitchLayout && !layouts.Contains(h.Layout ?? "", StringComparer.OrdinalIgnoreCase)));

        foreach (var action in actions)
        {
            var keys = current.FirstOrDefault(h => Same(h, action))?.Keys ?? string.Empty;
            AddRow(action, keys);
        }
        Update();
    }

    /// <returns>The new hotkeys, or null if cancelled.</returns>
    public static IReadOnlyList<Hotkey>? Ask(IReadOnlyList<Hotkey> current, IReadOnlyList<string> layouts)
    {
        var window = new HotkeysWindow(current, layouts);
        return window.ShowDialog() == true ? window.Hotkeys() : null;
    }

    internal IReadOnlyList<Hotkey> Hotkeys() =>
        _boxes.Where(b => b.Box.Text.Length > 0).Select(b => b.Action with { Keys = b.Box.Text }).ToList();

    /// <summary>Why the hotkeys can't be saved: one that isn't allowed, or the same keys twice.</summary>
    internal string? Problem()
    {
        foreach (var hotkey in Hotkeys())
        {
            if (KeyChord.Parse(hotkey.Keys, out var problem) is not { } chord)
                return $"{hotkey.Describe()}: {problem}";
            if (Hotkey.Problem(chord) is { } notAllowed)
                return $"{hotkey.Describe()}: {notAllowed}";
        }
        var twice = Hotkeys().GroupBy(h => h.Keys, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return twice is null ? null : $"{twice.Key} is set for both \"{twice.First().Describe()}\" and \"{twice.Last().Describe()}\".";
    }

    /// <summary>Puts <paramref name="keys"/> in the box for <paramref name="action"/>, as a key press would.</summary>
    internal void Set(Hotkey action, string keys)
    {
        _boxes.First(b => Same(b.Action, action)).Box.Text = keys;
        Update();
    }

    private static bool Same(Hotkey a, Hotkey b) =>
        a.Action == b.Action && string.Equals(a.Layout ?? "", b.Layout ?? "", StringComparison.OrdinalIgnoreCase);

    private void AddRow(Hotkey action, string keys)
    {
        var row = Rows.RowDefinitions.Count;
        Rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = action.Describe(), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 8, 4) };
        var box = new TextBox { Text = keys, IsReadOnly = true, IsReadOnlyCaretVisible = false, Margin = new Thickness(0, 4, 0, 4) };
        box.PreviewKeyDown += OnKey;
        Grid.SetRow(label, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Rows.Children.Add(label);
        Rows.Children.Add(box);
        _boxes.Add((action, box));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        // Plain Tab moves between boxes, plain Esc and Enter close the window.
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape or Key.Enter)
            return;
        e.Handled = true;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
            box.Text = string.Empty;
        else if (Hotkey.Text(modifiers, key) is { } text)
            box.Text = text;
        Update();
    }

    private void Update()
    {
        var problem = Problem();
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Problem() is null)
            DialogResult = true;
    }
}

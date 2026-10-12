using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// Hotkeys: a box per action (and per layout) that takes the key combination pressed in it. Each change is saved once
/// the keys are allowed and not taken twice. While a box has the keyboard, the hotkeys are let go, so pressing one
/// sets it again rather than running it.
/// </summary>
internal sealed class HotkeysPage : UserControl, IPage
{
    private readonly ISettingsHost? _host;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _problem;
    private readonly List<(Hotkey Action, TextBox Box)> _boxes = [];

    /// <param name="host">Null to try the page out without an app, as the tests do.</param>
    public HotkeysPage(ISettingsHost? host)
    {
        _host = host;
        var column = SettingsWindow.Styled(new Controls.PageColumn(), "PageColumn");
        column.Children.Add(SettingsWindow.Styled(new TextBlock { Text = "Hotkeys" }, "PageTitle"));
        column.Children.Add(SettingsWindow.Styled(new TextBlock
        {
            Text = "Keys that work from anywhere, even in a game. Click a box and press the keys, e.g. Ctrl+Alt+D; Backspace clears one.",
        }, "PageIntro"));
        _problem = SettingsWindow.Styled(new TextBlock { Margin = new Thickness(2, 0, 0, 10) }, "ProblemText");
        column.Children.Add(_problem);
        column.Children.Add(_rows);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = column };
    }

    public void Load()
    {
        if (_host is not null)
            Fill(_host.Settings.Hotkeys, _host.Layouts.List());
    }

    public void Left() => _host?.ResumeHotkeys();

    /// <summary>A box for each action and each layout, holding the keys set for it in <paramref name="current"/>.</summary>
    internal void Fill(IReadOnlyList<Hotkey> current, IReadOnlyList<string> layouts)
    {
        _rows.Children.Clear();
        _boxes.Clear();
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

        var first = true;
        foreach (var action in actions)
        {
            if (action.Action == Hotkey.SwitchLayout && first)
            {
                _rows.Children.Add(Ui.Heading("Switch the main dashboard to"));
                first = false;
            }
            AddRow(action, current.FirstOrDefault(h => Same(h, action))?.Keys ?? string.Empty);
        }
        Update(save: false);
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
        Update(save: true);
    }

    private static bool Same(Hotkey a, Hotkey b) =>
        a.Action == b.Action && string.Equals(a.Layout ?? "", b.Layout ?? "", StringComparison.OrdinalIgnoreCase);

    private void AddRow(Hotkey action, string keys)
    {
        var box = new TextBox { Text = keys, IsReadOnly = true, IsReadOnlyCaretVisible = false, Width = 200 };
        Controls.Placeholder.SetText(box, "Not set");
        AutomationProperties.SetName(box, action.Describe());
        box.PreviewKeyDown += OnKey;
        box.GotKeyboardFocus += (_, _) => _host?.SuspendHotkeys();
        box.LostKeyboardFocus += (_, _) => _host?.ResumeHotkeys();
        var glyph = action.Action switch
        {
            Hotkey.ShowHide => "",
            Hotkey.NextPage => "",
            Hotkey.PreviousPage => "",
            Hotkey.Edit => "",
            _ => "",
        };
        _rows.Children.Add(Ui.Card(glyph, action.Action == Hotkey.SwitchLayout ? action.Layout ?? "" : action.Describe(), null, box));
        _boxes.Add((action, box));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        // Plain Tab moves between boxes.
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape)
            return;
        e.Handled = true;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
            box.Text = string.Empty;
        else if (Hotkey.Text(modifiers, key) is { } text)
            box.Text = text;
        Update(save: true);
    }

    private void Update(bool save)
    {
        var problem = Problem();
        _problem.Text = problem ?? string.Empty;
        if (save && problem is null)
            _host?.SetHotkeys(Hotkeys());
    }
}

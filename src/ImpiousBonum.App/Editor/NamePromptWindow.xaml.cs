using System.Windows;
using System.Windows.Controls;

namespace ImpiousBonum.App.Editor;

/// <summary>Asks for a layout name, explaining what's wrong with it as you type.</summary>
public partial class NamePromptWindow : Window
{
    private readonly Func<string, string?> _check;

    private NamePromptWindow(string title, string prompt, string initial, Func<string, string?> check)
    {
        InitializeComponent();
        _check = check;
        Title = title;
        PromptText.Text = prompt;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <param name="check">Returns why a name can't be used, or null if it can.</param>
    /// <returns>The name, or null if cancelled.</returns>
    public static string? Ask(Window? owner, string title, string prompt, string initial, Func<string, string?> check)
    {
        var window = new NamePromptWindow(title, prompt, initial, check) { Owner = owner };
        return window.ShowDialog() == true ? window.NameBox.Text : null;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        var problem = _check(NameBox.Text);
        ErrorText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = problem is null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_check(NameBox.Text) is null)
            DialogResult = true;
    }
}

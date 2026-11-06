using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Screens → Add a tablet screen, or a tablet screen's Change: its name, and its size picked from common tablets or
/// typed in, landscape or portrait.
/// </summary>
public partial class TabletScreenWindow : Window
{
    private const string OtherSize = "Another tablet (type its size)";

    private readonly IReadOnlyList<TabletScreen> _others;
    private bool _syncing;

    /// <param name="others">The other tablet screens, which it can't share a name with.</param>
    /// <param name="editing">The tablet screen being changed, or null for a new one.</param>
    internal TabletScreenWindow(IReadOnlyList<TabletScreen> others, TabletScreen? editing)
    {
        _others = others;
        InitializeComponent();
        PresetBox.ItemsSource = TabletScreen.Presets.Select(p => $"{p.Label} · {p.Width}×{p.Height}").Prepend(OtherSize).ToList();

        _syncing = true;
        if (editing is null)
        {
            NameBox.Text = TabletScreen.NewName(others);
            var (_, width, height) = TabletScreen.Presets[0];
            WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            Title = "Change the tablet screen";
            IntroText.Text = "Its size is what the editor's Match dashboard screen makes a layout. Renaming it changes its link.";
            OkButton.Content = "Save";
            NameBox.Text = editing.Name;
            WidthBox.Text = editing.Width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = editing.Height.ToString(CultureInfo.InvariantCulture);
        }
        _syncing = false;
        FollowSize();
        NameBox.SelectAll();
        NameBox.Focus();
    }

    /// <param name="others">The other tablet screens, which it can't share a name with.</param>
    /// <param name="editing">The tablet screen being changed, or null for a new one.</param>
    /// <returns>Its name and size, or null if cancelled.</returns>
    public static (string Name, int Width, int Height)? Ask(Window? owner, IReadOnlyList<TabletScreen> others, TabletScreen? editing = null)
    {
        var window = new TabletScreenWindow(others, editing);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true ? window.Result() : null;
    }

    internal (string Name, int Width, int Height)? Result() =>
        Problem() is null && TypedSize() is { } size ? (NameBox.Text.Trim(), size.Width, size.Height) : null;

    internal string? Problem()
    {
        var size = TypedSize();
        return TabletScreen.Problem(NameBox.Text, size?.Width, size?.Height, _others);
    }

    /// <summary>The width and height typed, if both are whole numbers.</summary>
    private (int Width, int Height)? TypedSize() =>
        int.TryParse(WidthBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
        && int.TryParse(HeightBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : null;

    /// <summary>A tablet picked: its size, the way up that's picked.</summary>
    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetBox.SelectedIndex <= 0)
            return;
        var (_, width, height) = TabletScreen.Presets[PresetBox.SelectedIndex - 1];
        if (Portrait.IsChecked == true)
            (width, height) = (height, width);
        _syncing = true;
        WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        _syncing = false;
        Update();
    }

    /// <summary>Turned the other way up: the width and height swap, if they're the other way round.</summary>
    private void OnWayUp(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsInitialized || TypedSize() is not { } size)
            return;
        if (size.Width == size.Height || (size.Height > size.Width) == (Portrait.IsChecked == true))
            return;
        _syncing = true;
        (WidthBox.Text, HeightBox.Text) = (HeightBox.Text, WidthBox.Text);
        _syncing = false;
        Update();
    }

    private void OnSizeTyped(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !IsInitialized)
            return;
        FollowSize();
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    /// <summary>The tablet and the way up follow a size typed in: the tablet that size, if one's listed, or another.</summary>
    private void FollowSize()
    {
        _syncing = true;
        if (TypedSize() is (var width, var height))
        {
            var (longer, shorter) = (Math.Max(width, height), Math.Min(width, height));
            PresetBox.SelectedIndex = TabletScreen.Presets.ToList().FindIndex(p => p.Width == longer && p.Height == shorter) + 1;
            if (width != height)
                (height > width ? Portrait : Landscape).IsChecked = true;
        }
        else
        {
            PresetBox.SelectedIndex = 0;
        }
        _syncing = false;
        Update();
    }

    private void Update()
    {
        // TextChanged fires while the window is still being built, before every control exists.
        if (!IsInitialized || OkButton is null)
            return;
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

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// A small sample of a theme for the theme picker: its text, accent and critical colours as dots on its background.
/// Draws nothing for a name that isn't a theme (such as "None"), so the names beside it stay lined up.
/// </summary>
public sealed class ThemeSample : FrameworkElement
{
    public static readonly DependencyProperty ThemeNameProperty = DependencyProperty.Register(
        nameof(ThemeName), typeof(string), typeof(ThemeSample), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LibraryProperty = DependencyProperty.Register(
        nameof(Library), typeof(ThemeLibrary), typeof(ThemeSample), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen Outline = CreateOutline();

    public ThemeSample()
    {
        Width = 36;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public string? ThemeName
    {
        get => (string?)GetValue(ThemeNameProperty);
        set => SetValue(ThemeNameProperty, value);
    }

    public ThemeLibrary? Library
    {
        get => (ThemeLibrary?)GetValue(LibraryProperty);
        set => SetValue(LibraryProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Library is not { } library || library.Find(ThemeName) is not { } name)
            return;

        var theme = Theme.From(library.Resolve(new JsonObject { [ThemeLibrary.BaseKey] = name }));
        dc.DrawRoundedRectangle(theme.Background, Outline, new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 3, 3);
        Brush[] dots = [theme.Foreground, theme.Accent, theme.Critical];
        for (var i = 0; i < dots.Length; i++)
            dc.DrawEllipse(dots[i], null, new Point(ActualWidth / 2 + (i - 1) * 9, ActualHeight / 2), 3, 3);
    }

    // Grey at half opacity: visible around both a black and a white theme, in the editor's light and dark modes.
    private static Pen CreateOutline()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)), 1);
        pen.Freeze();
        return pen;
    }
}

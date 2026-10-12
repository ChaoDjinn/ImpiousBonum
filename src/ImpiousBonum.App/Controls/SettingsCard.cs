using System.Windows;
using System.Windows.Controls;

namespace ImpiousBonum.App.Controls;

/// <summary>
/// One setting, as Windows 11's Settings app shows them: an icon, what it is and a line about it on the left, and the
/// control that changes it (a switch, a list, a button) on the right. Its look is in Styles.xaml.
/// </summary>
public class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    /// <summary>What the setting is, e.g. "Start with Windows".</summary>
    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>A line about it, in the secondary colour; hidden when empty.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>An icon from Segoe Fluent Icons; none when empty.</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>More below the header, across the whole card: a list, a set of choices.</summary>
    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}

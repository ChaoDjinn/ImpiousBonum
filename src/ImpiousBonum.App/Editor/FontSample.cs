using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImpiousBonum.App.Editor;

/// <summary>A font in the font picker: an installed font, or (for a widget's own font) the theme's.</summary>
public sealed class FontChoice
{
    private static IReadOnlyList<FontChoice>? _installed;
    private string? _sample;
    private bool _inspected;

    private FontChoice(string name, FontFamily? family)
    {
        Name = name;
        Family = family;
    }

    /// <summary>The theme's font, which a widget uses when its own font is empty.</summary>
    public static readonly FontChoice ThemeFont = new(string.Empty, null);

    /// <summary>
    /// Every installed font, by name. Listing them is quick; each font is only loaded when the drop-down shows it.
    /// </summary>
    public static IReadOnlyList<FontChoice> Installed => _installed ??=
        [.. Fonts.SystemFontFamilies.DistinctBy(f => f.Source).OrderBy(f => f.Source).Select(f => new FontChoice(f.Source, f))];

    /// <summary>What's stored for it: the font's name, or empty for the theme's font.</summary>
    public string Name { get; }

    /// <summary>The font, or null for the theme's.</summary>
    public FontFamily? Family { get; }

    /// <summary>
    /// A few of the font's symbols to show beside its name when it's a symbol or icon font, whose letters wouldn't spell
    /// its name; null for any other font, whose name is drawn in itself. Worked out the first time the font is shown.
    /// </summary>
    public string? Sample
    {
        get
        {
            if (!_inspected)
            {
                _inspected = true;
                _sample = Inspect(Family, Name);
            }
            return _sample;
        }
    }

    /// <summary>
    /// What screen readers call its row. The drop-down's text and search use <see cref="Name"/>, which is empty for the
    /// theme's font.
    /// </summary>
    public override string ToString() => Family is null ? "Theme font" : Name;

    private static string? Inspect(FontFamily? family, string name)
    {
        // Composite fonts such as Global User Interface have no single typeface, but they're made of fonts with letters.
        if (family is null || !new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var glyphs))
            return null;
        var map = glyphs.CharacterToGlyphMap;
        return ShowsItsName(glyphs.Symbol, map, name) ? null : SampleOf(map);
    }

    /// <summary>
    /// Whether a font draws <paramref name="name"/> as letters: not a symbol font (Wingdings, Marlett…), whose letters are
    /// pictures, and with a character for each of the name's own (icon fonts such as Segoe MDL2 Assets have none, so the
    /// name would come out in some other font and look like any other).
    /// </summary>
    internal static bool ShowsItsName(bool symbol, IDictionary<int, ushort> characters, string name) =>
        !symbol && name.All(c => char.IsWhiteSpace(c) || characters.ContainsKey(c));

    /// <summary>The first few characters a font has, skipping spaces and control characters, to show what it draws.</summary>
    internal static string SampleOf(IDictionary<int, ushort> characters, int count = 5) =>
        string.Join(" ", characters.Keys
            .Where(c => Rune.TryCreate(c, out var rune) && !Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune))
            .Order()
            .Take(count)
            .Select(char.ConvertFromUtf32));
}

/// <summary>
/// A font's name in the font picker, drawn in that font, Word-style. A symbol or icon font has its name in the editor's
/// own font instead, with a few of its symbols beside it. Every row is the same height, however tall the font.
/// </summary>
public sealed class FontSample : DockPanel
{
    public static readonly DependencyProperty ChoiceProperty = DependencyProperty.Register(
        nameof(Choice), typeof(FontChoice), typeof(FontSample), new FrameworkPropertyMetadata(null, (d, _) => ((FontSample)d).Show()));

    private const double Size = 15;
    private const double RowHeight = 22;

    private readonly TextBlock _name = Line();
    private readonly TextBlock _sample = Line();

    public FontSample()
    {
        // Glyphs taller than the row (swashes, maths fonts) are cut off rather than drawn over the next one.
        ClipToBounds = true;
        _sample.Margin = new Thickness(10, 0, 0, 0);
        SetDock(_sample, Dock.Right);
        Children.Add(_sample);
        Children.Add(_name);
    }

    public FontChoice? Choice
    {
        get => (FontChoice?)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    /// <summary>Fills in the name and sample; also when a row is reused for another font as the list scrolls.</summary>
    private void Show()
    {
        var choice = Choice;
        var sample = choice?.Sample;
        _name.Text = choice is null ? null : choice.Family is null ? "Theme font" : choice.Name;
        if (choice?.Family is { } family && sample is null)
            _name.FontFamily = family;
        else
            _name.ClearValue(TextBlock.FontFamilyProperty);

        _sample.Text = sample;
        _sample.Visibility = sample is null ? Visibility.Collapsed : Visibility.Visible;
        if (choice?.Family is { } symbols && sample is not null)
            _sample.FontFamily = symbols;
        else
            _sample.ClearValue(TextBlock.FontFamilyProperty);
    }

    private static TextBlock Line() => new()
    {
        FontSize = Size,
        LineHeight = RowHeight,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Picks a widget to add: the widgets in groups down the side (<see cref="WidgetDescriptor.Category"/>), each a card with
/// a picture of it (<see cref="WidgetPreviews"/>), its name and what it's for, and a box to find one by name or what it
/// shows. Double-click a card, or choose one and press Add or Enter. It opens on the group last used.
/// </summary>
public partial class AddWidgetWindow : Window
{
    /// <summary>The group last shown, for opening on it next time.</summary>
    private static string? _lastGroup;

    private readonly ThemeSettings _theme;
    private readonly List<WidgetGroup> _groups;
    private readonly List<WidgetCard> _cards;
    private bool _changing;

    /// <param name="theme">The layout's theme, resolved, which the pictures are drawn in.</param>
    public AddWidgetWindow(ThemeSettings theme)
    {
        InitializeComponent();
        _theme = theme;
        var backdrop = Theme.From(theme).Background;
        _cards = [.. WidgetFactory.Descriptors.Select(descriptor => new WidgetCard(descriptor, backdrop))];
        _groups = [.. _cards.GroupBy(card => card.Descriptor.Category).Select(group => new WidgetGroup(group.Key, [.. group]))];
        GroupList.ItemsSource = _groups;
        GroupList.SelectedItem = _groups.FirstOrDefault(group => group.Name == _lastGroup) ?? _groups.FirstOrDefault();
        Loaded += (_, _) =>
        {
            WidgetList.Focus();
            DrawPreviews();
        };
    }

    /// <summary>The widget chosen, once the window closes with Add; null if it was cancelled.</summary>
    public WidgetDescriptor? Chosen { get; private set; }

    /// <summary>The groups, in the order the editor offers them.</summary>
    internal IReadOnlyList<WidgetGroup> Groups => _groups;

    /// <summary>The cards showing now: the chosen group's, or those found.</summary>
    internal IReadOnlyList<WidgetCard> Shown => WidgetList.ItemsSource as IReadOnlyList<WidgetCard> ?? [];

    /// <summary>The widgets matching all of <paramref name="search"/>'s words, in their name, what they're for, their type or their group.</summary>
    internal static bool Matches(WidgetDescriptor descriptor, string search)
    {
        var haystack = $"{descriptor.Name} {descriptor.Description} {descriptor.Type} {descriptor.Category}";
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Draws the pictures after the window has opened, one at a time when it's otherwise idle, the group showing first, so
    /// it opens at once and the pictures follow. A widget that can't be drawn keeps an empty picture.
    /// </summary>
    private void DrawPreviews()
    {
        var first = GroupList.SelectedItem as WidgetGroup;
        foreach (var card in _cards.OrderBy(card => card.Descriptor.Category != first?.Name))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsLoaded || card.Preview is not null)
                    return;
                try
                {
                    card.Preview = WidgetPreviews.Draw(card.Descriptor, _theme);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    AppLog.Warning($"Couldn't draw a picture of the {card.Descriptor.Type} widget: {ex.Message}");
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changing || GroupList.SelectedItem is not WidgetGroup group)
            return;
        _lastGroup = group.Name;
        // Choosing a group leaves what was being looked for.
        _changing = true;
        SearchBox.Text = "";
        _changing = false;
        Show(group.Cards);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_changing)
            return;
        var search = SearchBox.Text.Trim();
        _changing = true;
        if (search.Length == 0)
        {
            GroupList.SelectedItem = _groups.FirstOrDefault(group => group.Name == _lastGroup) ?? _groups.FirstOrDefault();
            Show((GroupList.SelectedItem as WidgetGroup)?.Cards ?? []);
        }
        else
        {
            // Found in every group, so none is chosen.
            GroupList.SelectedItem = null;
            Show([.. _cards.Where(card => Matches(card.Descriptor, search))]);
        }
        _changing = false;
    }

    /// <summary>Down from the box goes to the cards, to choose one by the arrow keys.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || WidgetList.Items.Count == 0)
            return;
        if (WidgetList.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, WidgetList.SelectedIndex)) is ListBoxItem item)
            item.Focus();
        e.Handled = true;
    }

    private void Show(IReadOnlyList<WidgetCard> cards)
    {
        WidgetList.ItemsSource = cards;
        WidgetList.SelectedIndex = cards.Count > 0 ? 0 : -1;
        if (cards.Count > 0)
            WidgetList.ScrollIntoView(cards[0]);
    }

    private void OnWidgetChanged(object sender, SelectionChangedEventArgs e)
    {
        var card = WidgetList.SelectedItem as WidgetCard;
        AddButton.IsEnabled = card is not null;
        ChosenText.Text = card is null ? "" : string.Create(CultureInfo.CurrentCulture,
            $"{card.Name} · {card.Descriptor.DefaultWidth:0} × {card.Descriptor.DefaultHeight:0} · {card.Descriptor.Description}");
        ChosenText.ToolTip = card?.Descriptor.Description;
    }

    /// <summary>Takes the card chosen as the widget to add, and returns it; null while none is.</summary>
    internal WidgetDescriptor? Choose() => Chosen = (WidgetList.SelectedItem as WidgetCard)?.Descriptor;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Choose() is not null)
            DialogResult = true;
    }

    private void OnWidgetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // A double-click on a card, not on the space between them or the scroll bar.
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(WidgetList, source) is ListBoxItem)
            OnAdd(sender, e);
    }
}

/// <summary>A group of widgets, as a tab down the side: its name and how many are in it.</summary>
internal sealed record WidgetGroup(string Name, IReadOnlyList<WidgetCard> Cards)
{
    public int Count => Cards.Count;
}

/// <summary>A widget as a card: its name, the first sentence of what it's for, and its picture once drawn.</summary>
internal sealed class WidgetCard(WidgetDescriptor descriptor, Brush backdrop) : INotifyPropertyChanged
{
    private ImageSource? _preview;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WidgetDescriptor Descriptor { get; } = descriptor;

    public string Name => Descriptor.Name;

    /// <summary>What it's for, in its first sentence.</summary>
    public string Summary { get; } = FirstSentence(descriptor.Description);

    /// <summary>The layout's background, behind the picture, so a picture narrower than its card sits on the same colour.</summary>
    public Brush Backdrop { get; } = backdrop;

    public ImageSource? Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            Changed();
        }
    }

    /// <summary>The text up to its first full stop and space, past any "e.g." or "i.e.", which don't end a sentence.</summary>
    internal static string FirstSentence(string text)
    {
        for (var end = text.IndexOf(". ", StringComparison.Ordinal); end >= 0; end = text.IndexOf(". ", end + 1, StringComparison.Ordinal))
        {
            var before = text[..end];
            if (!before.EndsWith("e.g", StringComparison.OrdinalIgnoreCase) && !before.EndsWith("i.e", StringComparison.OrdinalIgnoreCase))
                return text[..(end + 1)];
        }
        return text;
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

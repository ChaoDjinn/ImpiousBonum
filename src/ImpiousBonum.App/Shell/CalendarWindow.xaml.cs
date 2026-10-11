using System.Collections.ObjectModel;
using System.Windows;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Calendar…: the iCal links the agenda widget shows events from.</summary>
public partial class CalendarWindow : Window
{
    private readonly ObservableCollection<CalendarLink> _links;

    internal CalendarWindow(IReadOnlyList<CalendarLink> links)
    {
        InitializeComponent();
        _links = new ObservableCollection<CalendarLink>(links);
        LinkList.ItemsSource = _links;
        Update();
    }

    /// <returns>The new list, or null if cancelled.</returns>
    public static IReadOnlyList<CalendarLink>? Ask(IReadOnlyList<CalendarLink> links)
    {
        var window = new CalendarWindow(links);
        return window.ShowDialog() == true ? window.Links : null;
    }

    internal IReadOnlyList<CalendarLink> Links => [.. _links];

    /// <summary>Why the link being typed can't be added, or null if it can (or nothing's typed yet).</summary>
    internal string? Problem()
    {
        var link = LinkBox.Text.Trim();
        if (link.Length == 0)
            return null;
        if (!Uri.TryCreate(CalendarProvider.WebAddress(link), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return "Paste the whole link, starting https:// or webcal://.";
        if (_links.Any(l => l.Url.Trim().Equals(link, StringComparison.OrdinalIgnoreCase)))
            return "That calendar is already in the list.";
        if (_links.Any(l => l.Name.Equals(ChosenName(), StringComparison.OrdinalIgnoreCase)))
            return "Another calendar has that name.";
        return null;
    }

    /// <summary>The name typed, or one made up from the link's site.</summary>
    private string ChosenName()
    {
        if (NameBox.Text.Trim() is { Length: > 0 } typed)
            return typed;
        return Uri.TryCreate(CalendarProvider.WebAddress(LinkBox.Text), UriKind.Absolute, out var uri) ? uri.Host : "Calendar";
    }

    internal void Add()
    {
        if (LinkBox.Text.Trim().Length == 0 || Problem() is not null)
            return;
        _links.Add(new CalendarLink(ChosenName(), LinkBox.Text.Trim()));
        NameBox.Text = LinkBox.Text = string.Empty;
        Update();
    }

    private void Update()
    {
        if (AddButton is null)
            return;
        var problem = Problem();
        ProblemText.Text = problem ?? string.Empty;
        AddButton.IsEnabled = LinkBox.Text.Trim().Length > 0 && problem is null;
        RemoveButton.IsEnabled = LinkList.SelectedItem is not null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private void OnAdd(object sender, RoutedEventArgs e) => Add();

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (LinkList.SelectedItem is CalendarLink link)
            _links.Remove(link);
        Update();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // A link typed but not added yet is what was meant.
        Add();
        DialogResult = true;
    }
}

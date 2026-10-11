using System.Collections.ObjectModel;
using System.Windows;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>A named link, as the list shows and edits it.</summary>
public sealed record NamedLink(string Name, string Url);

/// <summary>
/// A list of named web links to add to and remove from: tray → Calendar… (iCal links) and tray → News feeds…
/// (RSS and Atom feeds).
/// </summary>
public partial class LinkListWindow : Window
{
    private readonly ObservableCollection<NamedLink> _links;
    private readonly bool _allowWebcal;
    private readonly string _what;

    /// <param name="what">What a link is, in the singular ("calendar", "feed"), for messages.</param>
    /// <param name="allowWebcal">Accept webcal:// links (calendars) as well as http(s).</param>
    internal LinkListWindow(IEnumerable<NamedLink> links, string title, string what, string intro, string note, bool allowWebcal)
    {
        InitializeComponent();
        Title = title;
        _what = what;
        _allowWebcal = allowWebcal;
        IntroText.Text = intro;
        NoteText.Text = note;
        AddHeading.Text = $"Add a {what}";
        _links = new ObservableCollection<NamedLink>(links);
        LinkList.ItemsSource = _links;
        Update();
    }

    /// <summary>Tray → Calendar…, or the agenda widget's button in the editor: returns the new list, or null if cancelled.</summary>
    public static IReadOnlyList<CalendarLink>? AskCalendars(IReadOnlyList<CalendarLink> links, Window? owner = null) =>
        Ask(owner, new LinkListWindow(links.Select(l => new NamedLink(l.Name, l.Url)), "Calendars", "calendar", CalendarIntro, CalendarNote, allowWebcal: true))
            ?.Select(l => new CalendarLink(l.Name, l.Url)).ToList();

    /// <summary>Tray → News feeds…, or the news widget's button in the editor: returns the new list, or null if cancelled.</summary>
    public static IReadOnlyList<NewsFeed>? AskFeeds(IReadOnlyList<NewsFeed> feeds, Window? owner = null) =>
        Ask(owner, new LinkListWindow(feeds.Select(f => new NamedLink(f.Name, f.Url)), "News feeds", "feed", FeedIntro, FeedNote, allowWebcal: false))
            ?.Select(l => new NewsFeed(l.Name, l.Url)).ToList();

    internal const string CalendarIntro =
        "Add a calendar's private iCal link to show its events in the agenda widget. Google Calendar: Settings → your calendar → "
        + "Secret address in iCal format. Outlook: Settings → Calendar → Shared calendars → Publish a calendar → ICS. Apple: share as a public calendar.";

    internal const string CalendarNote =
        "A private link lets anyone who has it read the calendar, so it's kept only in this PC's settings. Calendars are checked every 15 minutes.";

    internal const string FeedIntro =
        "Add news feeds (RSS or Atom) to show their headlines in the news widget. Most news sites and blogs have one: look for "
        + "an RSS link, or try the site's address followed by /feed or /rss. YouTube channels and subreddits have them too.";

    internal const string FeedNote = "Feeds are checked every 15 minutes.";

    private static IReadOnlyList<NamedLink>? Ask(Window? owner, LinkListWindow window)
    {
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true ? window.Links : null;
    }

    internal IReadOnlyList<NamedLink> Links => [.. _links];

    /// <summary>Why the link being typed can't be added, or null if it can (or nothing's typed yet).</summary>
    internal string? Problem()
    {
        var link = LinkBox.Text.Trim();
        if (link.Length == 0)
            return null;
        if (!Uri.TryCreate(WebAddress(link), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return _allowWebcal ? "Paste the whole link, starting https:// or webcal://." : "Paste the whole link, starting https://.";
        if (_links.Any(l => l.Url.Trim().Equals(link, StringComparison.OrdinalIgnoreCase)))
            return $"That {_what} is already in the list.";
        if (_links.Any(l => l.Name.Equals(ChosenName(), StringComparison.OrdinalIgnoreCase)))
            return $"Another {_what} has that name.";
        return null;
    }

    private string WebAddress(string link) => _allowWebcal ? CalendarProvider.WebAddress(link) : link.Trim();

    /// <summary>The name typed, or one made up from the link's site.</summary>
    private string ChosenName()
    {
        if (NameBox.Text.Trim() is { Length: > 0 } typed)
            return typed;
        return Uri.TryCreate(WebAddress(LinkBox.Text), UriKind.Absolute, out var uri) ? uri.Host : _what;
    }

    internal void Add()
    {
        if (LinkBox.Text.Trim().Length == 0 || Problem() is not null)
            return;
        _links.Add(new NamedLink(ChosenName(), LinkBox.Text.Trim()));
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
        if (LinkList.SelectedItem is NamedLink link)
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

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>A named link, as the list shows and edits it.</summary>
public sealed record NamedLink(string Name, string Url);

/// <summary>
/// A list of named web links to add to and remove from: calendars (iCal links) and news feeds (RSS and Atom). The
/// Calendars and News feeds pages show one, saving each change; the editor's agenda and news widgets open one in a
/// dialog (<see cref="LinkListWindow"/>).
/// </summary>
public partial class LinkListPanel : UserControl
{
    private readonly ObservableCollection<NamedLink> _links = [];
    private readonly bool _allowWebcal;
    private readonly string _what;

    /// <param name="what">What a link is, in the singular ("calendar", "feed"), for messages.</param>
    /// <param name="allowWebcal">Accept webcal:// links (calendars) as well as http(s).</param>
    internal LinkListPanel(string what, string intro, string note, bool allowWebcal)
    {
        InitializeComponent();
        _what = what;
        _allowWebcal = allowWebcal;
        IntroText.Text = intro;
        IntroText.Visibility = intro.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoteText.Text = note;
        AddHeading.Text = $"Add a {what}";
        Controls.Placeholder.SetText(LinkList, $"No {what}s yet. Add one below.");
        LinkList.ItemsSource = _links;
        Update();
    }

    internal static LinkListPanel ForCalendars(bool withIntro = true) => new("calendar", withIntro ? CalendarIntro : string.Empty, CalendarNote, allowWebcal: true);

    internal static LinkListPanel ForFeeds(bool withIntro = true) => new("feed", withIntro ? FeedIntro : string.Empty, FeedNote, allowWebcal: false);

    internal const string CalendarIntro =
        "Add a calendar's private iCal link to show its events in the agenda widget. Google Calendar: Settings → your calendar → "
        + "Secret address in iCal format. Outlook: Settings → Calendar → Shared calendars → Publish a calendar → ICS. Apple: share as a public calendar.";

    internal const string CalendarNote =
        "A private link lets anyone who has it read the calendar, so it's kept only in this PC's settings. Calendars are checked every 15 minutes.";

    internal const string FeedIntro =
        "Add news feeds (RSS or Atom) to show their headlines in the news widget. Most news sites and blogs have one: look for "
        + "an RSS link, or try the site's address followed by /feed or /rss. YouTube channels and subreddits have them too.";

    internal const string FeedNote = "Feeds are checked every 15 minutes.";

    /// <summary>A link was added or removed.</summary>
    public event EventHandler? Changed;

    internal IReadOnlyList<NamedLink> Links
    {
        get => [.. _links];
        set
        {
            _links.Clear();
            foreach (var link in value)
                _links.Add(link);
            Update();
        }
    }

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

    /// <summary>Adds the link typed, if there is one and it's fine.</summary>
    internal void Add()
    {
        if (LinkBox.Text.Trim().Length == 0 || Problem() is not null)
            return;
        _links.Add(new NamedLink(ChosenName(), LinkBox.Text.Trim()));
        NameBox.Text = LinkBox.Text = string.Empty;
        Update();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Update()
    {
        // TextChanged fires while the panel is still being built; the constructor calls this once it's ready.
        if (!IsInitialized)
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
        if (LinkList.SelectedItem is not NamedLink link)
            return;
        _links.Remove(link);
        Update();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The links in a dialog, with Save and Cancel: the editor's buttons for the agenda and news widgets.</summary>
public sealed class LinkListWindow : Window
{
    private readonly LinkListPanel _panel;

    private LinkListWindow(LinkListPanel panel, IEnumerable<NamedLink> links, string title)
    {
        ThemeMode = ThemeMode.System;
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _panel = panel;
        _panel.Links = [.. links];
        _panel.SetResourceReference(MarginProperty, "DialogPadding");

        var save = new Button { Content = "Save", IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.SetResourceReference(MinWidthProperty, "DialogButtonWidth");
        save.Click += (_, _) =>
        {
            // A link typed but not added yet is what was meant.
            _panel.Add();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.SetResourceReference(MarginProperty, "ButtonGap");
        cancel.SetResourceReference(MinWidthProperty, "DialogButtonWidth");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var footer = new Border { Child = buttons };
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        DockPanel.SetDock(footer, Dock.Bottom);

        var dock = new DockPanel();
        dock.Children.Add(footer);
        dock.Children.Add(_panel);
        Content = dock;
    }

    /// <summary>The agenda widget's button in the editor: returns the new list, or null if cancelled.</summary>
    public static IReadOnlyList<CalendarLink>? AskCalendars(IReadOnlyList<CalendarLink> links, Window? owner = null) =>
        Ask(owner, new LinkListWindow(LinkListPanel.ForCalendars(), links.Select(l => new NamedLink(l.Name, l.Url)), "Calendars"))
            ?.Select(l => new CalendarLink(l.Name, l.Url)).ToList();

    /// <summary>The news widget's button in the editor: returns the new list, or null if cancelled.</summary>
    public static IReadOnlyList<NewsFeed>? AskFeeds(IReadOnlyList<NewsFeed> feeds, Window? owner = null) =>
        Ask(owner, new LinkListWindow(LinkListPanel.ForFeeds(), feeds.Select(f => new NamedLink(f.Name, f.Url)), "News feeds"))
            ?.Select(l => new NewsFeed(l.Name, l.Url)).ToList();

    private static IReadOnlyList<NamedLink>? Ask(Window? owner, LinkListWindow window)
    {
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true ? window._panel.Links : null;
    }
}

using System.Windows.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Settings;

/// <summary>Calendars, or News feeds: the links the agenda or news widget reads, each change saved at once.</summary>
internal sealed class LinkListPage : UserControl, IPage
{
    private readonly LinkListPanel _panel;
    private readonly Func<IReadOnlyList<NamedLink>> _read;

    private LinkListPage(string title, LinkListPanel panel, Func<IReadOnlyList<NamedLink>> read, Action<IReadOnlyList<NamedLink>> save)
    {
        _panel = panel;
        _read = read;
        _panel.Changed += (_, _) => save(_panel.Links);
        var column = SettingsWindow.Styled(new Controls.PageColumn(), "PageColumn");
        column.Children.Add(SettingsWindow.Styled(new TextBlock { Text = title }, "PageTitle"));
        column.Children.Add(_panel);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = column };
    }

    public static LinkListPage ForCalendars(ISettingsHost host) => new("Calendars", LinkListPanel.ForCalendars(),
        () => host.Settings.Calendars.Select(c => new NamedLink(c.Name, c.Url)).ToList(),
        links => host.SetCalendars(links.Select(l => new CalendarLink(l.Name, l.Url)).ToList()));

    public static LinkListPage ForNewsFeeds(ISettingsHost host) => new("News feeds", LinkListPanel.ForFeeds(),
        () => host.Settings.NewsFeeds.Select(f => new NamedLink(f.Name, f.Url)).ToList(),
        links => host.SetNewsFeeds(links.Select(l => new NewsFeed(l.Name, l.Url)).ToList()));

    public void Load() => _panel.Links = _read();

    /// <summary>A link typed but not added yet is what was meant.</summary>
    public void Left() => _panel.Add();
}

using System.Windows;
using System.Windows.Controls;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// A page made of cards built in code: a title and a line about the page, then whatever <see cref="Build"/> adds,
/// built afresh each time the page is shown so it always reflects the settings.
/// </summary>
internal abstract class CardPage : UserControl, IPage
{
    private readonly StackPanel _column;
    private readonly int _fixed;

    internal CardPage(string title, string intro)
    {
        _column = SettingsWindow.Styled(new Controls.PageColumn(), "PageColumn");
        _column.Children.Add(SettingsWindow.Styled(new TextBlock { Text = title }, "PageTitle"));
        _column.Children.Add(SettingsWindow.Styled(new TextBlock { Text = intro }, "PageIntro"));
        _fixed = _column.Children.Count;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _column };
    }

    /// <summary>Adds the page's cards with <see cref="Add"/>.</summary>
    protected abstract void Build();

    protected T Add<T>(T element) where T : UIElement
    {
        _column.Children.Add(element);
        return element;
    }

    public void Load()
    {
        _column.Children.RemoveRange(_fixed, _column.Children.Count - _fixed);
        Build();
    }

    public virtual void Refresh()
    {
    }

    public virtual void Left()
    {
    }
}

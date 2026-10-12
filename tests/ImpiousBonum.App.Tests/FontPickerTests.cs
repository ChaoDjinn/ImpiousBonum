using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class FontPickerTests
{
    // ---- Which fonts are drawn in themselves -----------------------------------------------------

    private static Dictionary<int, ushort> Characters(params int[] codes) => codes.ToDictionary(c => c, _ => (ushort)1);

    private static readonly int[] Letters = [.. Enumerable.Range('A', 26), .. Enumerable.Range('a', 26), .. Enumerable.Range('0', 10)];

    [Fact]
    public void A_font_with_the_letters_of_its_name_is_drawn_in_itself()
    {
        Assert.True(FontChoice.ShowsItsName(symbol: false, Characters(Letters), "Times New Roman"));
        Assert.True(FontChoice.ShowsItsName(symbol: false, Characters(Letters), "Segoe UI Variable Display"));
    }

    [Fact]
    public void Symbol_fonts_and_icon_fonts_are_not()
    {
        // Wingdings has letters, but they're pictures; the font says so.
        Assert.False(FontChoice.ShowsItsName(symbol: true, Characters(Letters), "Wingdings"));
        // Icon fonts have no letters at all.
        Assert.False(FontChoice.ShowsItsName(symbol: false, Characters(0xE700, 0xE701, 0xE702), "Segoe MDL2 Assets"));
    }

    [Fact]
    public void A_sample_is_the_first_few_characters_a_font_has_skipping_spaces()
    {
        Assert.Equal("!  ", FontChoice.SampleOf(Characters(0x09, 0x20, 0xA0, 0xE701, 0x21, 0xE700, 0xE702), count: 3));
        // Beyond the first 65,536 characters too.
        Assert.Equal("😀", FontChoice.SampleOf(Characters(0x20, 0x1F600)));
    }

    [Fact]
    public void Installed_fonts_are_listed_once_each_and_symbol_fonts_get_a_sample()
    {
        Sta.Run(() =>
        {
            var fonts = FontChoice.Installed;
            Assert.Equal(fonts.Count, fonts.Select(f => f.Name).Distinct().Count());
            // Every copy of Windows has these.
            Assert.Null(fonts.Single(f => f.Name == "Arial").Sample);
            Assert.NotNull(fonts.Single(f => f.Name == "Wingdings").Sample);
            if (fonts.SingleOrDefault(f => f.Name == "Segoe MDL2 Assets") is { } icons)
                Assert.NotNull(icons.Sample);
        });
    }

    [Fact]
    public void Each_row_shows_the_name_in_the_font_or_beside_a_sample()
    {
        Sta.Run(() =>
        {
            var arial = Lines(FontChoice.Installed.Single(f => f.Name == "Arial"));
            Assert.Equal(("Arial", "Arial"), (arial.Name.Text, arial.Name.FontFamily.Source));
            Assert.Equal(Visibility.Collapsed, arial.Sample.Visibility);

            var wingdings = Lines(FontChoice.Installed.Single(f => f.Name == "Wingdings"));
            Assert.Equal("Wingdings", wingdings.Name.Text);
            Assert.NotEqual("Wingdings", wingdings.Name.FontFamily.Source);
            Assert.Equal(Visibility.Visible, wingdings.Sample.Visibility);
            Assert.Equal("Wingdings", wingdings.Sample.FontFamily.Source);

            Assert.Equal("Theme font", Lines(FontChoice.ThemeFont).Name.Text);
        });

        static (TextBlock Name, TextBlock Sample) Lines(FontChoice choice)
        {
            var row = new FontSample { Choice = choice };
            var lines = row.Children.OfType<TextBlock>().ToList();
            return (lines[1], lines[0]);
        }
    }

    // ---- The drop-down ---------------------------------------------------------------------------

    [Fact]
    public void Only_the_rows_on_screen_are_made()
    {
        Sta.Run(() =>
        {
            var (_, window, combo) = ShowFontOf(TextWidget());
            combo.IsDropDownOpen = true;
            Pump();

            Assert.True(combo.IsDropDownOpen);
            var made = Enumerable.Range(0, combo.Items.Count).Count(i => combo.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            window.Close();

            Assert.InRange(made, 1, 60);
            Assert.True(made < combo.Items.Count);
            Assert.Same(FontChoice.ThemeFont, combo.Items[0]);
        });
    }

    [Fact]
    public void Hovering_over_fonts_shows_them_and_closing_without_picking_puts_the_font_back()
    {
        Sta.Run(() =>
        {
            var (session, window, combo) = ShowFontOf(TextWidget());
            var before = session.Document.Widgets[0].ToJsonString();
            combo.IsDropDownOpen = true;
            Pump();

            Hover(combo, 1);
            Assert.Equal(Name(combo, 1), Font(session));
            Hover(combo, 2);
            Assert.Equal(Name(combo, 2), Font(session));
            Assert.False(session.IsDirty);
            Assert.False(session.CanUndo);

            // Clicking away closes it without picking.
            combo.IsDropDownOpen = false;
            Pump();
            window.Close();

            Assert.Equal(before, session.Document.Widgets[0].ToJsonString());
            Assert.False(session.IsDirty);
            Assert.False(session.CanUndo);
            Assert.Equal(string.Empty, combo.Text);
        });
    }

    [Fact]
    public void Arrowing_through_the_list_and_pressing_Enter_keeps_one_font_as_one_undo_step()
    {
        Sta.Run(() =>
        {
            var (session, window, combo) = ShowFontOf(TextWidget());
            combo.IsDropDownOpen = true;
            Pump();

            Press(combo, Key.Down, Key.Down, Key.Down);
            var shown = combo.Text;
            Assert.NotEqual(string.Empty, shown);
            Assert.Equal(shown, Font(session));
            Assert.False(session.IsDirty);

            Press(combo, Key.Enter);
            Pump();
            window.Close();

            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(shown, Font(session));
            Assert.True(session.IsDirty);
            session.Undo();
            Assert.Null(Font(session));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Clicking_a_font_keeps_it()
    {
        Sta.Run(() =>
        {
            var (session, window, combo) = ShowFontOf(TextWidget());
            combo.IsDropDownOpen = true;
            Pump();

            Hover(combo, 1);
            Hover(combo, 2);
            Click(combo, 2);
            Pump();
            window.Close();

            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(Name(combo, 2), Font(session));
            Assert.Equal(Name(combo, 2), combo.Text);
            Assert.True(session.IsDirty);
            session.Undo();
            Assert.Null(Font(session));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Esc_puts_the_font_back()
    {
        Sta.Run(() =>
        {
            var (session, window, combo) = ShowFontOf(TextWidget("Arial"));
            combo.IsDropDownOpen = true;
            Pump();

            Press(combo, Key.Down, Key.Down);
            Assert.NotEqual("Arial", Font(session));
            Press(combo, Key.Escape);
            Pump();
            window.Close();

            Assert.Equal("Arial", Font(session));
            Assert.Equal("Arial", combo.Text);
            Assert.False(session.IsDirty);
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Up_and_Down_with_the_list_closed_step_through_fonts_as_one_undo_step()
    {
        Sta.Run(() =>
        {
            var clock = new FakeClock();
            var (session, window, combo) = ShowFontOf(TextWidget(), clock);

            foreach (var _ in Enumerable.Range(0, 3))
            {
                Press(combo, Key.Down);
                // Stopping to look at each doesn't make it more than one step.
                clock.Advance(TimeSpan.FromSeconds(5));
            }
            Assert.Equal(Name(combo, 3), Font(session));
            Assert.Equal(Name(combo, 3), combo.Text);
            Assert.True(session.IsDirty);

            // Back up to the top: the theme's font, which is no font of the widget's own.
            Press(combo, Key.Up, Key.Up, Key.Up, Key.Up);
            Pump();
            window.Close();

            Assert.Null(session.Document.Widgets[0]["fontFamily"]);
            Assert.False(session.IsDirty);
            Assert.True(session.CanUndo);
            session.Undo();
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Typing_a_font_still_sets_it_and_emptying_it_goes_back_to_the_themes()
    {
        Sta.Run(() =>
        {
            var (session, window, combo) = ShowFontOf(TextWidget());
            combo.Text = "Arial";
            Pump();
            Assert.Equal("Arial", Font(session));

            combo.Text = string.Empty;
            Pump();
            window.Close();

            Assert.Null(session.Document.Widgets[0]["fontFamily"]);
        });
    }

    [Fact]
    public void The_themes_own_font_has_no_theme_font_to_pick()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument());
            var (panel, window) = Show(session);
            var combo = FontBox(panel);
            window.Close();

            Assert.NotSame(FontChoice.ThemeFont, combo.Items[0]);
            Assert.Equal(session.ResolvedTheme.FontFamily, combo.Text);
        });
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static JsonObject TextWidget(string? font = null)
    {
        var widget = new JsonObject { ["type"] = "text", ["x"] = 10, ["y"] = 10, ["width"] = 400, ["height"] = 200 };
        if (font is not null)
            widget["fontFamily"] = font;
        return widget;
    }

    private static string? Font(LayoutSession session) => session.Document.Widgets[0].GetString("fontFamily");

    private static string Name(ComboBox combo, int index) => ((FontChoice)combo.Items[index]).Name;

    /// <summary>The widget's settings in a window, with its font drop-down.</summary>
    private static (LayoutSession Session, Window Window, ComboBox Combo) ShowFontOf(JsonObject widget, TimeProvider? clock = null)
    {
        var session = new LayoutSession(new LayoutDocument { Widgets = [widget] }, clock: clock);
        var (panel, window) = Show(session);
        session.Select(0);
        Pump();
        return (session, window, FontBox(panel));
    }

    private static ComboBox FontBox(PropertyPanel panel) => Descendants<ComboBox>(panel).Single(c => c.ItemsSource is IReadOnlyList<FontChoice>);

    /// <summary>The mouse moving over the row for a font.</summary>
    private static void Hover(ComboBox combo, int index)
    {
        var row = (UIElement)combo.ItemContainerGenerator.ContainerFromIndex(index);
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
    }

    /// <summary>A click on the row for a font: the button going down and coming up.</summary>
    private static void Click(ComboBox combo, int index)
    {
        var row = (UIElement)combo.ItemContainerGenerator.ContainerFromIndex(index);
        foreach (var routed in new[] { Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent, Mouse.PreviewMouseUpEvent, Mouse.MouseUpEvent })
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed });
    }

    /// <summary>Keys pressed in the box, which has the keyboard while its list is open too.</summary>
    private static void Press(ComboBox combo, params Key[] keys)
    {
        var box = (UIElement)combo.Template.FindName("PART_EditableTextBox", combo);
        var source = PresentationSource.FromVisual(combo)!;
        foreach (var key in keys)
        {
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            box.RaiseEvent(preview);
            // As with a real key: once the preview is handled, the key press is too.
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent, Handled = preview.Handled });
        }
    }

    private static (PropertyPanel Panel, Window Window) Show(LayoutSession session)
    {
        var panel = new PropertyPanel(session, () => (1920, 480), () => new MetricStore());
        var window = new Window
        {
            Content = panel, Width = 440, Height = 3000, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        window.Show();
        Pump();
        return (panel, window);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}

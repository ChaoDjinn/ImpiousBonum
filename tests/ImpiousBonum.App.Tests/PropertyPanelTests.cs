using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class PropertyPanelTests
{
    public static TheoryData<string> WidgetTypes() => [.. WidgetFactory.Descriptors.Select(d => d.Type)];

    /// <summary>
    /// Looking at a widget's settings mustn't change it. Editable drop-downs (colours, fonts) fill in their text a moment
    /// after they're shown, which used to write each default into the layout, so the editor asked to save changes nobody
    /// had made.
    /// </summary>
    [Theory]
    [MemberData(nameof(WidgetTypes))]
    public void Selecting_a_widget_with_only_its_position_changes_nothing(string type)
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget(type)] });
            var (panel, window) = Show(session);

            session.Select(0);
            Pump();
            panel.RefreshLive();
            session.Select(-1);
            Pump();
            session.Select(0);
            Pump();
            window.Close();

            Assert.False(session.IsDirty, $"Selecting a {type} widget changed the layout to: {session.Document.Widgets[0].ToJsonString()}");
        });
    }

    [Fact]
    public void Looking_at_the_canvas_and_theme_changes_nothing()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(LayoutStore.LoadDefault());
            var (_, window) = Show(session);
            session.Select(-1);
            Pump();
            window.Close();

            Assert.False(session.IsDirty);
        });
    }

    [Fact]
    public void Several_widgets_selected_say_how_many_and_one_shows_its_settings_as_before()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("text"), Widget("clock")] });
            var (panel, window) = Show(session);

            session.Select(0);
            Pump();
            var one = (Heading(panel), Boxes: Descendants<TextBox>(panel).Count());
            session.Select([0, 1]);
            Pump();
            var both = (Heading(panel), Boxes: Descendants<TextBox>(panel).Count());
            session.Select(1);
            Pump();
            var other = Heading(panel);
            window.Close();

            Assert.Equal("Text", one.Item1);
            Assert.True(one.Boxes > 0);
            Assert.Equal(("2 widgets selected", 0), both);
            Assert.Equal("Clock", other);
            Assert.False(session.IsDirty);
        });
    }

    [Fact]
    public void Typing_a_colour_still_sets_it()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("text")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            var colour = Descendants<ComboBox>(panel).First(c => c.IsEditable && c.Text == "foreground");
            colour.Text = "#FF0000";
            Pump();
            window.Close();

            Assert.Equal("#FF0000", (string?)session.Document.Widgets[0]["color"]);
            Assert.True(session.IsDirty);
        });
    }

    [Fact]
    public void List_entries_are_dragged_by_a_handle_rather_than_moved_with_arrows()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Rows("A", "B")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            var handles = Handles(panel);
            var arrows = Descendants<Button>(panel).Where(b => b.ToolTip is "Move up" or "Move down").ToList();
            var removes = Descendants<Button>(panel).Count(b => b.ToolTip is "Remove");
            window.Close();

            Assert.Equal(2, handles.Count);
            Assert.All(handles, h => Assert.True(h.Focusable));
            Assert.Empty(arrows);
            Assert.Equal(2, removes);
        });
    }

    [Fact]
    public void Moving_a_list_entry_from_the_keyboard_keeps_focus_on_it()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Rows("A", "B", "C")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            // What Alt+Down does with focus on the first row's handle.
            Handles(panel)[0].Focus();
            Assert.True(DragReorder.Move(Handles(panel)[0], +1));
            Pump();
            var handleFocused = Handles(panel)[1].IsKeyboardFocused;

            // And Alt+Up while typing in the last row's label: still typing in it afterwards.
            var label = LabelBox(panel, "C");
            label.Focus();
            Assert.True(DragReorder.Move(label, -1));
            Pump();
            var labelFocused = LabelBox(panel, "C").IsKeyboardFocused;
            window.Close();

            Assert.Equal(["B", "C", "A"], Labels(session));
            Assert.True(handleFocused);
            Assert.True(labelFocused);
            session.Undo();
            Assert.Equal(["B", "A", "C"], Labels(session));
            session.Undo();
            Assert.Equal(["A", "B", "C"], Labels(session));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Feeds_are_ticked_unticked_and_put_in_order_an_undo_step_each()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("news")] });
            var (panel, window) = Show(session, Feeds);
            session.Select(0);
            Pump();

            // All feeds to start with: every feed ticked, in the News feeds page's order, and greyed out.
            var all = AllBox(panel);
            Assert.True(all.IsChecked);
            Assert.Equal(Feeds, Picks(panel).Select(b => (string)b.Tag));
            Assert.All(Picks(panel), b => Assert.True(b.IsChecked == true && !b.IsEnabled));
            Assert.False(session.IsDirty);

            // Unticking all starts from every feed ticked, so what shows doesn't change.
            Toggle(all);
            Assert.Equal("BBC, The Verge, Ars Technica", FeedsValue(session));
            Toggle(Pick(panel, "The Verge"));
            Assert.Equal("BBC, Ars Technica", FeedsValue(session));
            // Ticked ones come first; the rest have no handle to drag them by.
            Assert.Equal(["BBC", "Ars Technica", "The Verge"], Picks(panel).Select(b => (string)b.Tag));
            Assert.Equal(2, Handles(panel).Count(h => h.IsVisible));
            // Moved below the last ticked one, among the unticked, it stays last of them: the list lets it go there, and
            // the editor keeps it put.
            Assert.True(DragReorder.Move(Pick(panel, "Ars Technica"), +1));
            Assert.Equal("BBC, Ars Technica", FeedsValue(session));
            Assert.Equal(["BBC", "Ars Technica", "The Verge"], Picks(panel).Select(b => (string)b.Tag));
            Toggle(Pick(panel, "The Verge"));
            Assert.Equal("BBC, Ars Technica, The Verge", FeedsValue(session));

            // Alt+Up with focus on a feed's box moves it up, and focus stays with it.
            var verge = Pick(panel, "The Verge");
            verge.Focus();
            Assert.True(DragReorder.Move(verge, -1));
            Assert.Equal("BBC, The Verge, Ars Technica", FeedsValue(session));
            var focusKept = Pick(panel, "The Verge").IsKeyboardFocused;
            // A drag by its handle does the same, from the handle.
            var handle = Handles(panel).First(h => (string)h.Tag == "Ars Technica");
            handle.Focus();
            Assert.True(DragReorder.Move(handle, -1));
            Assert.Equal("BBC, Ars Technica, The Verge", FeedsValue(session));
            var handleFocused = Handles(panel).First(h => (string)h.Tag == "Ars Technica").IsKeyboardFocused;
            window.Close();

            Assert.True(focusKept);
            Assert.True(handleFocused);
            // However quickly they came, each change is an undo step of its own.
            foreach (var before in new[] { "BBC, The Verge, Ars Technica", "BBC, Ars Technica, The Verge", "BBC, Ars Technica", "BBC, The Verge, Ars Technica", null })
            {
                session.Undo();
                Assert.Equal(before, FeedsValue(session));
            }
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Ticking_all_feeds_greys_out_the_list()
    {
        Sta.Run(() =>
        {
            var news = Widget("news");
            news["feeds"] = "Ars Technica";
            var session = new LayoutSession(new LayoutDocument { Widgets = [news] });
            var (panel, window) = Show(session, Feeds);
            session.Select(0);
            Pump();

            Assert.False(AllBox(panel).IsChecked);
            Assert.True(Pick(panel, "BBC").IsEnabled);
            Toggle(AllBox(panel));
            var greyed = Picks(panel).All(b => b.IsChecked == true && !b.IsEnabled);
            var handles = Handles(panel).Count(h => h.IsVisible);
            window.Close();

            Assert.True(greyed);
            Assert.Equal(0, handles);
            Assert.Equal("all", FeedsValue(session));
        });
    }

    [Fact]
    public void Swapping_one_feed_for_another_never_goes_through_all_of_them()
    {
        Sta.Run(() =>
        {
            var news = Widget("news");
            news["feeds"] = "BBC";
            var session = new LayoutSession(new LayoutDocument { Widgets = [news] });
            var (panel, window) = Show(session, Feeds);
            session.Select(0);
            Pump();

            // Unticking the only feed ticked would leave none, which is all of them, so it waits for another.
            var only = Pick(panel, "BBC");
            var waits = only.IsChecked == true && !only.IsEnabled;
            var why = only.ToolTip as string;
            var whyShows = ToolTipService.GetShowOnDisabled(only);
            Toggle(Pick(panel, "The Verge"));
            var free = Pick(panel, "BBC").IsEnabled && Pick(panel, "The Verge").IsEnabled;
            Toggle(Pick(panel, "BBC"));
            var vergeWaits = !Pick(panel, "The Verge").IsEnabled;
            window.Close();

            Assert.True(waits);
            Assert.Equal("To untick it, first tick another or All feeds.", why);
            Assert.True(whyShows);
            Assert.True(free);
            Assert.Equal("The Verge", FeedsValue(session));
            Assert.True(vergeWaits);
        });
    }

    [Fact]
    public void Feeds_named_with_a_comma_or_all_only_show_with_all_of_them()
    {
        Sta.Run(() =>
        {
            // Feeds added before such names were turned away: a comma splits a name in two, and "all" means every feed.
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("news")] });
            var (panel, window) = Show(session, ["BBC", "News, UK", "All", "The Verge"]);
            session.Select(0);
            Pump();

            var allTicked = Picks(panel).All(b => b.IsChecked == true);
            // Unticking all leaves them out, greyed out and saying why.
            Toggle(AllBox(panel));
            var value = FeedsValue(session);
            var comma = Pick(panel, "News, UK");
            var named = Pick(panel, "All");
            var commaOut = comma.IsChecked == false && !comma.IsEnabled;
            var namedOut = named.IsChecked == false && !named.IsEnabled;
            var commaWhy = comma.ToolTip as string;
            var namedWhy = named.ToolTip as string;
            window.Close();

            Assert.True(allTicked);
            Assert.Equal("BBC, The Verge", value);
            Assert.True(commaOut);
            Assert.StartsWith("Its name has a comma in it, so it can't be chosen on its own.", commaWhy);
            Assert.True(namedOut);
            Assert.StartsWith("Its name means all of them, so it can't be chosen on its own.", namedWhy);

            // With only those, there's nothing to tick in place of all.
            var alone = new LayoutSession(new LayoutDocument { Widgets = [Widget("news")] });
            var (alonePanel, aloneWindow) = Show(alone, ["News, UK", "all"]);
            alone.Select(0);
            Pump();
            var allEnabled = AllBox(alonePanel).IsEnabled;
            aloneWindow.Close();

            Assert.False(allEnabled);
        });
    }

    [Fact]
    public void Feeds_that_have_gone_still_show_ticked_so_they_can_be_unticked()
    {
        Sta.Run(() =>
        {
            var news = Widget("news");
            news["feeds"] = "Old blog, bbc";
            var session = new LayoutSession(new LayoutDocument { Widgets = [news] });
            var (panel, window) = Show(session, Feeds);
            session.Select(0);
            Pump();

            Assert.Equal(["Old blog", "BBC", "The Verge", "Ars Technica"], Picks(panel).Select(b => (string)b.Tag));
            var gone = Pick(panel, "Old blog");
            Assert.True(gone.IsChecked);
            Assert.Contains("not found", Text(gone));
            Assert.DoesNotContain("not found", Text(Pick(panel, "BBC")));
            // Alt+Down on the last ticked one has nowhere to go, so the value isn't touched, not even to tidy "bbc".
            DragReorder.Move(Pick(panel, "BBC"), +1);
            Assert.False(session.IsDirty);

            Toggle(gone);
            var left = Picks(panel).Select(b => (string)b.Tag).ToList();
            window.Close();

            Assert.Equal("BBC", FeedsValue(session));
            Assert.Equal(Feeds, left);
        });
    }

    [Fact]
    public void A_weather_place_picked_by_another_spelling_is_ticked_as_the_widget_shows_it()
    {
        Sta.Run(() =>
        {
            // Picked while there were two Parises; the other has since gone, so this one is plain Paris now.
            string[] places = ["Leeds, England, United Kingdom", "Paris, Texas, United States"];
            var weather = Widget("weather");
            weather["places"] = "Paris (Texas), Leeds";
            var session = new LayoutSession(new LayoutDocument { Widgets = [weather] });
            var (panel, window) = Show(session, WeatherWidget.ShortNames(places), (_, name) => WeatherWidget.PlaceOption(name, places));
            session.Select(0);
            Pump();

            var picks = Picks(panel, "Places").Select(b => (string)b.Tag).ToList();
            var found = Picks(panel, "Places").All(b => b.IsChecked == true && !Text(b).Contains("not found"));
            // A change writes it as it's spelt now.
            Toggle(Pick(panel, "Leeds", "Places"));
            window.Close();

            Assert.Equal(["Paris", "Leeds"], picks);
            Assert.True(found);
            Assert.Equal("Paris", (string?)session.Document.Widgets[0]["places"]);
        });
    }

    [Fact]
    public void The_weather_details_are_picked_in_order_from_their_own_list()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("weather")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            // What it always showed, ticked first, then the rest to tick; nothing to find in a list the app keeps.
            var offered = Picks(panel, "Details to show").Select(b => (string)b.Tag).ToList();
            var ticked = Picks(panel, "Details to show").Count(b => b.IsChecked == true);
            var under = Picks(panel, "Forecast details").Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
            Toggle(Pick(panel, "wind", "Details to show"));
            var withWind = (string?)session.Document.Widgets[0]["details"];
            DragReorder.Move(Pick(panel, "wind", "Details to show"), -3);
            Pump();
            var windFirst = (string?)session.Document.Widgets[0]["details"];
            Toggle(Pick(panel, "UV", "Forecast details"));
            window.Close();

            Assert.Equal([.. WeatherWidget.DetailNames], offered);
            Assert.Equal(3, ticked);
            Assert.Equal(["chance of rain"], under);
            Assert.Equal("conditions, high and low, feels like, wind", withWind);
            Assert.Equal("wind, conditions, high and low, feels like", windFirst);
            Assert.Equal("chance of rain, UV", (string?)session.Document.Widgets[0]["forecastDetails"]);
        });
    }

    [Fact]
    public void Without_any_feeds_the_list_says_where_to_add_them()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("news")] });
            var (panel, window) = Show(session, []);
            session.Select(0);
            Pump();

            var message = Descendants<TextBlock>(panel).Single(t => t.Text.StartsWith("No news feeds yet", StringComparison.Ordinal));
            var shown = message.IsVisible;
            var picks = Picks(panel).Count;
            // Unticking all would only leave all again.
            var allEnabled = AllBox(panel).IsEnabled;
            window.Close();

            Assert.True(shown);
            Assert.Equal("No news feeds yet: add them on the News feeds page.", message.Text);
            Assert.Equal(0, picks);
            Assert.False(allEnabled);
        });
    }

    [Fact]
    public void The_editor_opens_centred_on_the_main_screen_and_fits_it()
    {
        // A 4K main screen at 150% scaling, less the taskbar.
        Assert.Equal((530.0, 130.0, 1500.0, 900.0), ImpiousBonum.App.Settings.SettingsWindow.Centred(new Rect(0, 0, 2560, 1160), 1500, 900));
        // A laptop screen too small for the editor's usual size: it shrinks to fit, with a margin.
        var (left, top, width, height) = ImpiousBonum.App.Settings.SettingsWindow.Centred(new Rect(0, 0, 1280, 680), 1500, 900);
        Assert.Equal((32, 17, 1216, 646), (left, top, width, height));
    }

    private static JsonObject Widget(string type) => new() { ["type"] = type, ["x"] = 10, ["y"] = 10, ["width"] = 400, ["height"] = 200 };

    /// <summary>The panel's heading: the widget type's name, or what's selected.</summary>
    private static string Heading(PropertyPanel panel) => ((TextBlock)panel.Children[0]).Text;

    /// <summary>A rows widget with a row for each label.</summary>
    private static JsonObject Rows(params string[] labels)
    {
        var widget = Widget("rows");
        widget["rows"] = new JsonArray([.. labels.Select(l => (JsonNode)new JsonObject { ["label"] = l })]);
        return widget;
    }

    private static List<string> Labels(LayoutSession session) =>
        [.. ((JsonArray)session.Document.Widgets[0]["rows"]!).Select(r => r!["label"]!.GetValue<string>())];

    /// <summary>The list entries' drag handles, top to bottom.</summary>
    private static List<FrameworkElement> Handles(PropertyPanel panel) => [.. Descendants<FrameworkElement>(panel).Where(DragReorder.GetIsHandle)];

    private static TextBox LabelBox(PropertyPanel panel, string label) => Descendants<TextBox>(panel).Single(b => b.Text == label);

    /// <summary>The news feeds, in the News feeds page's order.</summary>
    private static readonly string[] Feeds = ["BBC", "The Verge", "Ars Technica"];

    private static string? FeedsValue(LayoutSession session) => (string?)session.Document.Widgets[0]["feeds"];

    private static CheckBox AllBox(PropertyPanel panel) => Descendants<CheckBox>(panel).Single(b => b.Content as string == "All feeds");

    /// <summary>The boxes for each feed, top to bottom; for a widget with more than one list of names, those of the setting labelled <paramref name="label"/>.</summary>
    private static List<CheckBox> Picks(PropertyPanel panel, string? label = null) =>
        [.. Descendants<CheckBox>(label is null ? panel : SettingRow(panel, label)).Where(b => b.Tag is string)];

    private static CheckBox Pick(PropertyPanel panel, string name, string? label = null) => Picks(panel, label).Single(b => (string)b.Tag == name);

    /// <summary>A setting's row, by the label at its left.</summary>
    private static Grid SettingRow(PropertyPanel panel, string label) =>
        Descendants<Grid>(panel).First(g => g.Children.Count > 0 && g.Children[0] is TextBlock { Text: var text } && text == label);

    private static string Text(CheckBox box) => string.Concat(((TextBlock)box.Content).Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>Ticks or unticks a box as a click (or Narrator) does.</summary>
    private static void Toggle(CheckBox box)
    {
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box)).Toggle();
        Pump();
    }

    /// <summary>The panel in a window off screen, as in the editor: some controls only fill in their text once shown.</summary>
    /// <param name="options">The names a setting picking from a list the app keeps (the news feeds) is offered.</param>
    /// <param name="pickedOption">For a name picked that's spelt another way now, the one it stands for.</param>
    private static (PropertyPanel Panel, Window Window) Show(LayoutSession session, IReadOnlyList<string>? options = null, Func<string, string, string?>? pickedOption = null)
    {
        var panel = new PropertyPanel(session, () => (1920, 480), () => new MetricStore()) { Options = _ => options ?? [], PickedOption = pickedOption };
        var window = new Window
        {
            Content = panel, Width = 440, Height = 3000, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        window.Show();
        Pump();
        return (panel, window);
    }

    /// <summary>Lets the window lay itself out and run what it queued, as the editor's would between clicks.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }
}

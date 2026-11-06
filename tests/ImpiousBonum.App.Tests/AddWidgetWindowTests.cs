using System.Windows.Media.Imaging;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Tests;

public sealed class AddWidgetWindowTests
{
    private static readonly ThemeSettings Plain = new();

    [Fact]
    public void The_widgets_are_in_groups_down_the_side_in_the_order_of_the_widget_reference() => Sta.Run(() =>
    {
        var window = new AddWidgetWindow(Plain);

        Assert.Equal(
            ["Readings", "Graphs and bars", "Lists", "Games", "Time and calendar", "Weather, news and Claude", "Media and controls", "Pictures and shapes"],
            window.Groups.Select(g => g.Name));
        // Every widget is in one group, and none is in two.
        Assert.Equal(WidgetFactory.Descriptors.Select(d => d.Type), window.Groups.SelectMany(g => g.Cards).Select(c => c.Descriptor.Type));
        Assert.Equal(["Recent games", "Steam downloads", "Steam friends"], window.Groups.Single(g => g.Name == "Games").Cards.Select(c => c.Name));
        Assert.Equal(3, window.Groups.Single(g => g.Name == "Games").Count);
    });

    [Fact]
    public void A_group_shows_its_widgets_with_the_first_chosen_and_is_opened_on_next_time() => Sta.Run(() =>
    {
        var window = new AddWidgetWindow(Plain);
        window.GroupList.SelectedItem = window.Groups.Single(g => g.Name == "Games");

        Assert.Equal(["games", "downloads", "friends"], window.Shown.Select(c => c.Descriptor.Type));
        Assert.Equal("games", window.Choose()!.Type);
        Assert.True(window.AddButton.IsEnabled);
        Assert.StartsWith("Recent games · 600 × 300 · ", window.ChosenText.Text);

        // Opened again, it's on the same group.
        var again = new AddWidgetWindow(Plain);
        Assert.Equal("Games", Assert.IsType<WidgetGroup>(again.GroupList.SelectedItem).Name);
        again.GroupList.SelectedItem = again.Groups[0];
    });

    [Fact]
    public void Finding_a_widget_looks_in_every_group_by_its_name_and_what_it_s_for() => Sta.Run(() =>
    {
        var window = new AddWidgetWindow(Plain);
        window.GroupList.SelectedItem = window.Groups.Single(g => g.Name == "Lists");

        window.SearchBox.Text = "steam";
        Assert.Null(window.GroupList.SelectedItem);
        Assert.Contains("downloads", window.Shown.Select(c => c.Descriptor.Type));
        Assert.Contains("friends", window.Shown.Select(c => c.Descriptor.Type));
        Assert.DoesNotContain("clock", window.Shown.Select(c => c.Descriptor.Type));

        // Every word has to be there, in any order.
        window.SearchBox.Text = "friends steam";
        Assert.Equal(["friends"], window.Shown.Select(c => c.Descriptor.Type));
        Assert.Equal("friends", window.Choose()!.Type);

        // Nothing found: nothing to add.
        window.SearchBox.Text = "spreadsheet";
        Assert.Empty(window.Shown);
        Assert.Null(window.Choose());
        Assert.False(window.AddButton.IsEnabled);

        // Cleared, it's back on the group it was on.
        window.SearchBox.Text = "";
        Assert.Equal("Lists", Assert.IsType<WidgetGroup>(window.GroupList.SelectedItem).Name);
        Assert.Equal("drives", window.Shown[0].Descriptor.Type);

        // Choosing a group while looking leaves the looking.
        window.SearchBox.Text = "weather";
        window.GroupList.SelectedItem = window.Groups.Single(g => g.Name == "Games");
        Assert.Equal("", window.SearchBox.Text);
        Assert.Equal(3, window.Shown.Count);
        window.GroupList.SelectedItem = window.Groups[0];
    });

    [Fact]
    public void A_card_says_what_its_widget_is_for_in_a_sentence() => Sta.Run(() =>
    {
        Assert.Equal("A dial, half dial or ring.", WidgetCard.FirstSentence("A dial, half dial or ring. With warning colours."));
        Assert.Equal("No full stop", WidgetCard.FirstSentence("No full stop"));
        // A decimal point isn't the end of a sentence, nor an e.g.
        Assert.Equal("Gives 1.5 h.", WidgetCard.FirstSentence("Gives 1.5 h. Then more."));
        Assert.Equal("""A line of text with live values, e.g. "{gpu.load}" or "{cpu.temp} °C".""",
            WidgetCard.FirstSentence("""A line of text with live values, e.g. "{gpu.load}" or "{cpu.temp} °C". More."""));
        Assert.Equal("Rows, i.e. a stack.", WidgetCard.FirstSentence("Rows, i.e. a stack. More."));
    });

    [Fact]
    public void Every_widget_has_a_picture_of_itself_at_its_starting_size_kept_for_the_theme() => Sta.Run(() =>
    {
        foreach (var descriptor in WidgetFactory.Descriptors)
        {
            var picture = Assert.IsAssignableFrom<BitmapSource>(WidgetPreviews.Draw(descriptor, Plain));
            Assert.Equal((int)Math.Ceiling(descriptor.DefaultWidth), picture.PixelWidth);
            Assert.Equal((int)Math.Ceiling(descriptor.DefaultHeight), picture.PixelHeight);
            Assert.True(picture.IsFrozen, descriptor.Type);
            Assert.Same(picture, WidgetPreviews.Draw(descriptor, Plain));
        }

        // Another theme, another picture.
        var nord = new ThemeSettings { Background = "#2E3440" };
        Assert.NotSame(WidgetPreviews.Draw(WidgetFactory.Descriptors[0], Plain), WidgetPreviews.Draw(WidgetFactory.Descriptors[0], nord));
    });

    [Fact]
    public void An_image_widget_is_pictured_with_the_game_s_cover_as_it_starts_with_no_picture() => Sta.Run(() =>
    {
        var image = WidgetFactory.Find("image")!;
        var definition = WidgetPreviews.StartingDefinition(image, 300, 200);
        Assert.Equal("game", (string?)definition["source"]);
        // A rows widget, which starts with none, with the rows its description gives as an example.
        var rows = Assert.IsType<System.Text.Json.Nodes.JsonArray>(WidgetPreviews.StartingDefinition(WidgetFactory.Find("rows")!, 425, 110)["rows"]);
        Assert.Equal(["Download", "Upload", "Ping"], rows.Select(row => (string?)row!["label"]));
        // Lists a widget starts with are filled in, as the editor adds them.
        var buttons = WidgetFactory.Find("buttons")!;
        Assert.NotNull(WidgetPreviews.StartingDefinition(buttons, 300, 200)["buttons"]);
    });
}

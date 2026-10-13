using System.Text.Json.Nodes;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Tests;

public sealed class PickListTests
{
    private static readonly string[] Feeds = ["BBC", "The Verge", "Ars Technica"];

    [Theory]
    [InlineData("all", "all")]
    [InlineData("  ALL ", "all")]
    [InlineData("", "all")] // nothing picked has always meant every feed
    [InlineData(null, "all")]
    [InlineData("BBC, The Verge", "BBC, The Verge")]
    [InlineData("  The Verge ,BBC,, ", "The Verge, BBC")]
    [InlineData("BBC, bbc, The Verge, BBC", "BBC, The Verge")] // each name once, where it first is
    [InlineData("Gone, BBC", "Gone, BBC")] // names that aren't feeds any more are kept
    public void Values_read_and_write_back_tidily(string? value, string written)
    {
        var picks = PickList.Parse(value, allowAll: true);
        Assert.Equal(written, picks.ToString());
        // And what's written reads back the same.
        Assert.Equal(written, PickList.Parse(picks.ToString(), allowAll: true).ToString());
    }

    [Fact]
    public void Without_all_allowed_all_is_a_name_and_nothing_is_nothing()
    {
        Assert.Equal(["all"], PickList.Parse("all", allowAll: false).Names);
        var none = PickList.Parse(" , ", allowAll: false);
        Assert.False(none.All);
        Assert.Empty(none.Names);
        Assert.Equal(string.Empty, none.ToString());
    }

    [Fact]
    public void Old_feeds_values_still_read_the_same()
    {
        var settings = new WidgetSettings(new JsonObject { ["feeds"] = "BBC, The Verge" }, NewsWidget.Descriptor.Settings);
        Assert.Equal(["BBC", "The Verge"], settings.Picks("feeds").Names);
        Assert.True(new WidgetSettings(new JsonObject(), NewsWidget.Descriptor.Settings).Picks("feeds").All);
        Assert.True(new WidgetSettings(new JsonObject { ["feeds"] = "All" }, NewsWidget.Descriptor.Settings).Picks("feeds").All);
    }

    [Fact]
    public void A_picker_lists_the_ticked_first_in_order_then_the_rest()
    {
        var entries = PickList.Parse("ars technica, Gone, BBC", allowAll: true).Entries(Feeds);
        Assert.Equal(
            [
                new PickEntry("Ars Technica", Ticked: true, Found: true), // the feed's own spelling
                new PickEntry("Gone", Ticked: true, Found: false),
                new PickEntry("BBC", Ticked: true, Found: true),
                new PickEntry("The Verge", Ticked: false, Found: true),
            ],
            entries);
    }

    [Fact]
    public void With_all_every_option_is_ticked_in_its_own_order() =>
        Assert.Equal(Feeds.Select(f => new PickEntry(f, true, true)), PickList.Everything.Entries(Feeds));

    [Fact]
    public void Ticking_adds_after_the_others_and_unticking_takes_off()
    {
        var picks = PickList.Of(["BBC"]).Tick("Ars Technica").Tick("bbc");
        Assert.Equal("BBC, Ars Technica", picks.ToString());
        Assert.Equal("Ars Technica", picks.Untick("BBC", allowAll: true).ToString());
        // The last one off goes back to all, or to none where all isn't allowed.
        Assert.True(PickList.Of(["BBC"]).Untick("bbc", allowAll: true).All);
        Assert.Equal(string.Empty, PickList.Of(["BBC"]).Untick("BBC", allowAll: false).ToString());
    }

    [Fact]
    public void Moving_reorders_the_ticked_ones()
    {
        var picks = PickList.Of(Feeds);
        Assert.Equal("The Verge, Ars Technica, BBC", picks.Move(0, 2).ToString());
        Assert.Equal("Ars Technica, BBC, The Verge", picks.Move(2, 0).ToString());
        // Past the end stays at the end; nothing to move leaves it be.
        Assert.Equal("The Verge, Ars Technica, BBC", picks.Move(0, 7).ToString());
        Assert.Equal(picks.ToString(), picks.Move(5, 0).ToString());
        Assert.True(PickList.Everything.Move(0, 1).All);
    }
}

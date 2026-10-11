using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class WorldClockTests
{
    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    [InlineData("GMT Standard Time")]
    [InlineData(" Tokyo Standard Time ")]
    public void Zones_are_found_by_either_name(string id) => Assert.NotNull(ClockWidget.FindZone(id));

    [Theory]
    [InlineData("")]
    [InlineData("Narnia/Cair_Paravel")]
    public void Unknown_zones_are_null(string id) => Assert.Null(ClockWidget.FindZone(id));

    [Fact]
    public void Other_places_say_when_they_are_a_day_apart()
    {
        var here = new DateTime(2026, 10, 11, 23, 30, 0);
        Assert.Equal("08:30 +1", ClockWidget.Place(new DateTime(2026, 10, 12, 8, 30, 0), here, "HH:mm"));
        Assert.Equal("15:30 −1", ClockWidget.Place(new DateTime(2026, 10, 10, 15, 30, 0), here.Date, "HH:mm"));
        Assert.Equal("18:30", ClockWidget.Place(new DateTime(2026, 10, 11, 18, 30, 0), here, "HH:mm"));
    }

    [Fact]
    public void Tokyo_is_nine_hours_ahead_of_utc()
    {
        var tokyo = ClockWidget.FindZone("Asia/Tokyo")!;
        var utcNoon = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 11, 21, 0, 0), ClockWidget.InZone(utcNoon.ToLocalTime(), tokyo));
    }

    [Fact]
    public void A_clock_with_other_places_builds_and_refreshes()
    {
        Sta.Run(() =>
        {
            foreach (var layout in new[] { "columns", "rows" })
            {
                var widget = WidgetFactory.Create(new JsonObject
                {
                    ["type"] = "clock",
                    ["timeZone"] = "Europe/London",
                    ["zoneLayout"] = layout,
                    ["zones"] = new JsonArray(
                        new JsonObject { ["label"] = "New York", ["zone"] = "America/New_York" },
                        new JsonObject { ["label"] = "", ["zone"] = "Asia/Tokyo" },
                        new JsonObject { ["label"] = "Nowhere", ["zone"] = "Narnia/Cair_Paravel" }),
                }, Theme.From(new ThemeSettings()));
                widget.Refresh(new MetricStore(), DateTime.Now);
            }
        });
    }
}

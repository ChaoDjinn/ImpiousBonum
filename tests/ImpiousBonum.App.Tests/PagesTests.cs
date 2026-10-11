using System.IO;
using System.Windows;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class PagesTests : IDisposable
{
    private static readonly string[] Layouts = ["Default", "Gaming", "Night", "Work"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public PagesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Pages_are_the_picked_layouts_in_menu_order_or_all_of_them()
    {
        Assert.Equal(Layouts, Pages.Of(Layouts, []));
        Assert.Equal(new[] { "Gaming", "Work" }, Pages.Of(Layouts, ["work", "Gaming"]));
        // Picked layouts that were deleted are skipped; none left means all.
        Assert.Equal(Layouts, Pages.Of(Layouts, ["Gone"]));
    }

    [Theory]
    [InlineData("Gaming", 1, "Night")]
    [InlineData("Work", 1, "Default")]
    [InlineData("Default", -1, "Work")]
    [InlineData("gaming", -1, "Default")]
    [InlineData("Elsewhere", 1, "Default")]
    public void Turning_pages_wraps_round(string current, int step, string expected) =>
        Assert.Equal(expected, Pages.Next(Layouts, current, step));

    [Fact]
    public void One_page_has_nowhere_to_turn() => Assert.Null(Pages.Next(["Default"], "Default", 1));

    [Theory]
    [InlineData(-300, 10, 1)]
    [InlineData(300, -20, -1)]
    [InlineData(-100, 0, null)]
    [InlineData(-300, 200, null)]
    [InlineData(5, 400, null)]
    public void A_swipe_is_long_and_mostly_sideways(double dx, double dy, int? expected) =>
        Assert.Equal(expected, Pages.Swipe(new Vector(dx, dy), 1920));

    [Fact]
    public void Renaming_a_layout_keeps_it_a_page()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings { Pages = ["Default", "Gaming"], SwipePages = true, RotatePagesSeconds = 30 };
            using var store = new LayoutStore(settings);
            store.Save("Gaming", new LayoutDocument());

            store.Rename("Gaming", "Games");
            var saved = AppSettings.Load();
            Assert.Equal(new[] { "Default", "Games" }, saved.Pages);
            Assert.True(saved.SwipePages);
            Assert.Equal(30, saved.RotatePagesSeconds);
        });
    }
}

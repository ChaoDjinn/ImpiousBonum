using System.IO;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class FirstRunTests : IDisposable
{
    private static readonly DisplayMonitor Main = new(@"\\?\DISPLAY#MAIN", @"\\.\DISPLAY1", "Main", new PixelRect(0, 0, 2560, 1440), true);
    private static readonly DisplayMonitor Strip = new(@"\\?\DISPLAY#STRIP", @"\\.\DISPLAY2", "Strip", new PixelRect(2560, 0, 1920, 480), false);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public FirstRunTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_spare_screen_is_filled()
    {
        var settings = new AppSettings();
        Assert.Same(Strip, DisplayMonitor.Resolve(settings, [Main, Strip]));

        Assert.False(settings.PlaceFirstTime(Strip));
        Assert.True(settings.Fill);
        Assert.Equal(Strip.Id, settings.MonitorId);
        Assert.Equal(Strip.Device, settings.MonitorDevice);
    }

    [Fact]
    public void Without_a_spare_screen_it_starts_as_a_window_that_can_be_moved()
    {
        var settings = new AppSettings();
        Assert.Same(Main, DisplayMonitor.Resolve(settings, [Main]));

        Assert.True(settings.PlaceFirstTime(Main));
        Assert.False(settings.Fill);
        Assert.False(settings.Locked);
        Assert.Equal(Main.Id, settings.MonitorId);
    }

    [Fact]
    public void A_monitor_chosen_before_is_left_alone()
    {
        // Moved to the main screen on purpose, or kept from a version that only remembered the device name.
        var chosen = new AppSettings { MonitorId = Main.Id, MonitorDevice = Main.Device };
        Assert.False(chosen.PlaceFirstTime(Main));
        Assert.True(chosen.Fill);

        var legacy = new AppSettings { MonitorDevice = Main.Device };
        Assert.False(legacy.PlaceFirstTime(Main));
        Assert.True(legacy.Fill);
        Assert.Null(legacy.MonitorId);
    }

    [Fact]
    public void Unreadable_settings_start_from_the_defaults_and_keep_a_copy()
    {
        AppPaths.UseDataDirectory(_directory);
        const string broken = "{ \"calendars\": [ { \"name\": \"Work\", \"url\": \"https://example.com/work.ics\" } ], oops }";
        File.WriteAllText(AppPaths.SettingsFile, broken);

        var settings = AppSettings.Load();

        Assert.Empty(settings.Calendars);
        Assert.NotNull(settings.LoadProblem);
        Assert.Contains(AppSettings.UnreadableCopyName, settings.LoadProblem);
        Assert.Equal(broken, File.ReadAllText(Path.Combine(_directory, AppSettings.UnreadableCopyName)));
    }

    [Fact]
    public void Readable_settings_have_no_problem_and_it_isnt_saved()
    {
        AppPaths.UseDataDirectory(_directory);
        new AppSettings { PingHost = "8.8.8.8" }.Save();

        var settings = AppSettings.Load();

        Assert.Null(settings.LoadProblem);
        Assert.Equal("8.8.8.8", settings.PingHost);
        Assert.DoesNotContain("loadProblem", File.ReadAllText(AppPaths.SettingsFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Each_data_folder_has_its_own_instance_so_a_test_copy_can_run_beside_the_real_one()
    {
        AppPaths.UseDataDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImpiousBonum"));
        Assert.Equal(@"Local\ImpiousBonum", AppPaths.InstanceName);

        AppPaths.UseDataDirectory(_directory);
        var other = AppPaths.InstanceName;
        Assert.StartsWith(@"Local\ImpiousBonum-", other);
        AppPaths.UseDataDirectory(_directory.ToUpperInvariant());
        Assert.Equal(other, AppPaths.InstanceName);
    }
}

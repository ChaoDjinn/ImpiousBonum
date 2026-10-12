using System.IO;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class LoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_page_shows_and_returns_the_settings()
    {
        Sta.Run(() =>
        {
            var page = new LoggingPage(null);
            page.Fill(new LogSettings(true, ["cpu.load"], 30, 90), new MetricStore());
            Assert.Equal(new LogSettings(true, ["cpu.load"], 30, 90), page.Settings(), new LogSettingsComparer());

            page.AddMetric("fps");
            page.AddMetric("CPU.LOAD");
            Assert.Equal(["cpu.load", "fps"], page.Settings().Metrics);
        });
    }

    [Fact]
    public void Logging_needs_a_metric_and_unknown_choices_fall_back()
    {
        Sta.Run(() =>
        {
            var page = new LoggingPage(null);
            page.Fill(new LogSettings(true, [], 7, 3), null);
            Assert.NotNull(page.Problem());
            Assert.Equal(1, page.Settings().EverySeconds);
            Assert.Equal(7, page.Settings().KeepDays);
            page.EnabledCheck.IsChecked = false;
            Assert.Null(page.Problem());
        });
    }

    [Fact]
    public void Logging_settings_are_saved_and_default_to_off()
    {
        AppPaths.UseDataDirectory(_directory);
        Assert.Equal(LogSettings.Default, AppSettings.Load().Logging);
        Assert.False(LogSettings.Default.Enabled);

        var settings = new AppSettings { Logging = new LogSettings(true, ["gpu.temp", "fps"], 5, 0) };
        settings.Save();
        Assert.Equal(settings.Logging, AppSettings.Load().Logging, new LogSettingsComparer());
    }

    /// <summary>Records compare lists by reference; these compare their contents.</summary>
    private sealed class LogSettingsComparer : IEqualityComparer<LogSettings>
    {
        public bool Equals(LogSettings? x, LogSettings? y) =>
            x is not null && y is not null && x.Enabled == y.Enabled && x.Metrics.SequenceEqual(y.Metrics) && x.EverySeconds == y.EverySeconds && x.KeepDays == y.KeepDays;

        public int GetHashCode(LogSettings obj) => obj.EverySeconds;
    }
}

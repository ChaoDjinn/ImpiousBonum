using System.IO;
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
    public void The_window_shows_and_returns_the_settings()
    {
        Sta.Run(() =>
        {
            var window = new LoggingWindow(new LogSettings(true, ["cpu.load"], 30, 90), new MetricStore());
            Assert.Equal(new LogSettings(true, ["cpu.load"], 30, 90), window.Settings(), new LogSettingsComparer());

            window.AddMetric("fps");
            window.AddMetric("CPU.LOAD");
            Assert.Equal(["cpu.load", "fps"], window.Settings().Metrics);
            window.Close();
        });
    }

    [Fact]
    public void Logging_needs_a_metric_and_unknown_choices_fall_back()
    {
        Sta.Run(() =>
        {
            var window = new LoggingWindow(new LogSettings(true, [], 7, 3), null);
            Assert.NotNull(window.Problem());
            Assert.Equal(1, window.Settings().EverySeconds);
            Assert.Equal(7, window.Settings().KeepDays);
            window.EnabledCheck.IsChecked = false;
            Assert.Null(window.Problem());
            window.Close();
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

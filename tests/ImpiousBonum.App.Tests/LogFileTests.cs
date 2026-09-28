using System.IO;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

public sealed class LogFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_directory, "dashboard.log");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Creates_the_folder_and_appends_lines()
    {
        var log = new LogFile(LogPath);
        log.Write("INFO", "started");
        log.Write("WARN", "sensor host not running");

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" INFO started", lines[0]);
        Assert.EndsWith(" WARN sensor host not running", lines[1]);
    }

    [Fact]
    public void Repeated_warnings_and_errors_are_written_once_but_info_every_time()
    {
        var log = new LogFile(LogPath);
        for (var i = 0; i < 3; i++)
        {
            log.Write("WARN", "PingProvider sample failed");
            log.Write("ERROR", "Unhandled error: boom");
            log.Write("INFO", "Checking for updates");
        }

        var lines = File.ReadAllLines(LogPath);
        Assert.Single(lines, l => l.EndsWith("PingProvider sample failed"));
        Assert.Single(lines, l => l.EndsWith("Unhandled error: boom"));
        Assert.Equal(3, lines.Count(l => l.EndsWith("Checking for updates")));
    }

    [Fact]
    public void A_large_log_moves_aside_at_startup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LogPath, new string('x', 1024 * 1024 + 1));

        new LogFile(LogPath).Write("INFO", "fresh");

        Assert.True(File.Exists(LogPath + ".old"));
        Assert.Single(File.ReadAllLines(LogPath));
    }

    [Fact]
    public void A_small_log_is_kept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LogPath, "earlier" + Environment.NewLine);

        new LogFile(LogPath).Write("INFO", "later");

        Assert.False(File.Exists(LogPath + ".old"));
        Assert.Equal(2, File.ReadAllLines(LogPath).Length);
    }
}

using ImpiousBonum.Core.Logging;

namespace ImpiousBonum.Core.Tests;

public sealed class MetricLoggerTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 11, 10, 0, 0);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Each_period_becomes_a_row_of_average_lowest_and_highest()
    {
        var logger = new MetricLogger(["cpu.load", "fps"], TimeSpan.FromSeconds(10));
        Assert.Equal("time,cpu.load avg,cpu.load min,cpu.load max,fps avg,fps min,fps max", logger.Header);

        var rows = new List<string>();
        // Starts mid-period: 10:00:05 to 10:00:24, one reading a second.
        for (var i = 5; i < 25; i++)
        {
            var second = i;
            if (logger.Add(id => id == "cpu.load" ? second : null, Ten.AddSeconds(i)) is { } row)
                rows.Add(row);
        }

        Assert.Equal(["2026-10-11 10:00:10,7,5,9,,,", "2026-10-11 10:00:20,14.5,10,19,,,"], rows);
    }

    [Fact]
    public void A_period_with_no_readings_at_all_writes_nothing()
    {
        var logger = new MetricLogger(["x"], TimeSpan.FromSeconds(5));
        Assert.Null(logger.Add(_ => null, Ten));
        Assert.Null(logger.Add(_ => null, Ten.AddSeconds(6)));
        Assert.Null(logger.Add(_ => double.NaN, Ten.AddSeconds(11)));
    }

    [Fact]
    public void Files_are_daily_and_a_new_header_starts_a_new_file()
    {
        var log = new CsvLog(_directory);
        log.Append("time,a", "row1", Ten);
        log.Append("time,a", "row2", Ten.AddMinutes(1));
        log.Append("time,a,b", "row3", Ten.AddMinutes(2));
        log.Append("time,a", "row4", Ten.AddDays(1));

        Assert.Equal(["time,a", "row1", "row2"], File.ReadAllLines(Path.Combine(_directory, "2026-10-11.csv")));
        Assert.Equal(["time,a,b", "row3"], File.ReadAllLines(Path.Combine(_directory, "2026-10-11 (2).csv")));
        Assert.Equal(["time,a", "row4"], File.ReadAllLines(Path.Combine(_directory, "2026-10-12.csv")));

        // A restart that same day carries on in the matching file.
        new CsvLog(_directory).Append("time,a,b", "row5", Ten.AddHours(1));
        Assert.Equal(["time,a,b", "row3", "row5"], File.ReadAllLines(Path.Combine(_directory, "2026-10-11 (2).csv")));
    }

    [Fact]
    public void Old_logs_are_pruned_and_other_files_left_alone()
    {
        Directory.CreateDirectory(_directory);
        foreach (var name in new[] { "2026-09-01.csv", "2026-09-30 (2).csv", "2026-10-10.csv", "notes.csv" })
            File.WriteAllText(Path.Combine(_directory, name), "x");

        Assert.Equal(2, CsvLog.Prune(_directory, 7, Ten));
        Assert.Equal(["2026-10-10.csv", "notes.csv"], Directory.GetFiles(_directory).Select(Path.GetFileName).Order());
        Assert.Equal(0, CsvLog.Prune(_directory, 0, Ten.AddYears(1)));
    }
}

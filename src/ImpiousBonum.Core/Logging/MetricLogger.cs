using System.Globalization;

namespace ImpiousBonum.Core.Logging;

/// <summary>
/// Turns readings taken about once a second into CSV rows, one per period (say every 10 seconds): the time, then each
/// metric's average, lowest and highest reading over the period. A metric with no reading in a period has empty cells.
/// Periods line up with the clock (10:00:00, 10:00:10, …), so logs from different days line up too.
/// </summary>
public sealed class MetricLogger(IReadOnlyList<string> metrics, TimeSpan every)
{
    private readonly Stat[] _stats = metrics.Select(_ => new Stat()).ToArray();
    private DateTime? _periodStart;

    public IReadOnlyList<string> Metrics => metrics;

    public TimeSpan Every => every;

    public string Header => "time," + string.Join(",", metrics.SelectMany(m => new[] { $"{m} avg", $"{m} min", $"{m} max" }).Select(Csv.Quote));

    private sealed class Stat
    {
        public double Sum;
        public double Min = double.MaxValue;
        public double Max = double.MinValue;
        public int Count;

        public void Clear() => (Sum, Min, Max, Count) = (0, double.MaxValue, double.MinValue, 0);
    }

    /// <summary>Takes one set of readings. Returns the row for the period that just ended, if one did, else null.</summary>
    /// <param name="read">A metric's reading, or null when it has none.</param>
    public string? Add(Func<string, double?> read, DateTime now)
    {
        var start = PeriodStart(now);
        string? row = null;
        if (_periodStart is { } current && start != current)
        {
            if (_stats.Any(s => s.Count > 0))
                row = Row(current + every);
            foreach (var stat in _stats)
                stat.Clear();
        }
        _periodStart = start;

        for (var i = 0; i < metrics.Count; i++)
        {
            if (read(metrics[i]) is not { } value || !double.IsFinite(value))
                continue;
            var stat = _stats[i];
            stat.Sum += value;
            stat.Min = Math.Min(stat.Min, value);
            stat.Max = Math.Max(stat.Max, value);
            stat.Count++;
        }
        return row;
    }

    private DateTime PeriodStart(DateTime now)
    {
        var ticks = Math.Max(every.Ticks, TimeSpan.TicksPerSecond);
        var sinceMidnight = now.TimeOfDay.Ticks;
        return now.Date.AddTicks(sinceMidnight - sinceMidnight % ticks);
    }

    private string Row(DateTime end)
    {
        var cells = new List<string> { end.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
        foreach (var stat in _stats)
        {
            if (stat.Count == 0)
                cells.AddRange(["", "", ""]);
            else
                cells.AddRange([Number(stat.Sum / stat.Count), Number(stat.Min), Number(stat.Max)]);
        }
        return string.Join(",", cells);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Daily CSV files in a folder: <c>2026-10-11.csv</c>, or <c>2026-10-11 (2).csv</c> when the columns changed during
/// the day, so every file has one header that matches all its rows.
/// </summary>
public sealed class CsvLog(string folder)
{
    private string? _path;
    private string? _header;
    private DateTime _day;

    public string Folder => folder;

    /// <summary>Adds <paramref name="row"/> to today's file for <paramref name="header"/>, starting a file with the header if needed.</summary>
    public void Append(string header, string row, DateTime now)
    {
        if (_path is null || _header != header || _day != now.Date)
        {
            Directory.CreateDirectory(folder);
            _path = PathFor(header, now.Date);
            _header = header;
            _day = now.Date;
        }
        var text = File.Exists(_path) && new FileInfo(_path).Length > 0 ? row : header + Environment.NewLine + row;
        File.AppendAllText(_path, text + Environment.NewLine);
    }

    /// <summary>The first file for the day that's empty or has this header.</summary>
    private string PathFor(string header, DateTime day)
    {
        var name = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(folder, n == 1 ? $"{name}.csv" : $"{name} ({n}).csv");
            if (!File.Exists(path) || FirstLine(path) is not { Length: > 0 } first || first == header)
                return path;
        }
    }

    private static string? FirstLine(string path)
    {
        using var reader = new StreamReader(path);
        return reader.ReadLine();
    }

    /// <summary>Deletes daily logs older than <paramref name="keepDays"/> days; 0 keeps them all. Returns how many went.</summary>
    public static int Prune(string folder, int keepDays, DateTime today)
    {
        if (keepDays <= 0 || !Directory.Exists(folder))
            return 0;
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.csv"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < 10 || !DateTime.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            if (day < today.Date.AddDays(-keepDays))
            {
                File.Delete(path);
                deleted++;
            }
        }
        return deleted;
    }
}

internal static class Csv
{
    public static string Quote(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}

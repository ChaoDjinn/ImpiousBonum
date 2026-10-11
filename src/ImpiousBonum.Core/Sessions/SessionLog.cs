using System.Globalization;

namespace ImpiousBonum.Core.Sessions;

/// <summary>Game session recaps as lines in a CSV file, one per session, for a spreadsheet.</summary>
public static class SessionLog
{
    public const string Header =
        "start,end,game,minutes,fps average,fps lowest,fps highest,1% low average,cpu average,cpu peak,gpu average,gpu peak,cpu hottest,gpu hottest";

    /// <summary>Adds <paramref name="recap"/> to the end of the file, writing the header first if it's new.</summary>
    public static void Append(string path, SessionRecap recap)
    {
        var line = Line(recap);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            line = Header + Environment.NewLine + line;
        File.AppendAllText(path, line + Environment.NewLine);
    }

    /// <summary>One session's line; times are local, numbers are invariant (a dot for decimals), and missing readings are empty.</summary>
    public static string Line(SessionRecap recap)
    {
        string Number(string key, Func<SessionStat, double> pick) =>
            recap.Stat(key) is { } stat ? pick(stat).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty;

        return string.Join(",",
            recap.Start.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            recap.End.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Quote(recap.Game),
            Math.Floor(recap.Length.TotalMinutes).ToString(CultureInfo.InvariantCulture),
            Number("fps", s => s.Average), Number("fps", s => s.Min), Number("fps", s => s.Max),
            Number("low1", s => s.Average),
            Number("cpu", s => s.Average), Number("cpu", s => s.Max),
            Number("gpu", s => s.Average), Number("gpu", s => s.Max),
            Number("cputemp", s => s.Max), Number("gputemp", s => s.Max));
    }

    private static string Quote(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}

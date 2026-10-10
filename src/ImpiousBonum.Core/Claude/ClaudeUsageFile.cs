using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Claude;

/// <summary>One usage window from Claude Code: how much of it is used, and when it resets (Unix seconds, 0 if not known).</summary>
public sealed record ClaudeUsageWindow(double UsedPercentage, long ResetsAt);

/// <summary>
/// What the Claude Code status line last reported. <see cref="Updated"/> is when it last ran (Unix seconds); while Claude Code
/// is open the status line runs at least every <c>refreshInterval</c> seconds, so an old timestamp means it has closed.
/// </summary>
public sealed record ClaudeUsageSnapshot(long Updated, ClaudeUsageWindow? FiveHour, ClaudeUsageWindow? SevenDay)
{
    /// <summary>Claude Code has reported plan limits at least once. Only Pro and Max plans have them.</summary>
    [JsonIgnore]
    public bool HasLimits => FiveHour is not null || SevenDay is not null;

    /// <summary>
    /// Where a window stands at <paramref name="now"/>. Once limits are known, a window that is missing or past its reset
    /// time has nothing used in it yet (Claude Code drops a window when it resets, and a new one starts with the next message).
    /// </summary>
    public ClaudeUsageReading Reading(ClaudeUsageWindow? window, DateTimeOffset now)
    {
        if (!HasLimits)
            return new ClaudeUsageReading(null, null);
        if (window is null || (window.ResetsAt > 0 && window.ResetsAt <= now.ToUnixTimeSeconds()))
            return new ClaudeUsageReading(0, null);
        return new ClaudeUsageReading(
            Math.Clamp(window.UsedPercentage, 0, 100),
            window.ResetsAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt) - now : null);
    }
}

/// <param name="UsedPercentage">0–100, or null when there are no limits to report.</param>
/// <param name="ResetsIn">Time until the window resets, or null when it hasn't started (or isn't known).</param>
public readonly record struct ClaudeUsageReading(double? UsedPercentage, TimeSpan? ResetsIn)
{
    /// <summary>"resets in 2h 14m", or "not started" for a window with nothing used yet.</summary>
    public string? ResetsText => UsedPercentage is null ? null : ResetsIn is { } span ? $"resets in {FormatDuration(span)}" : "not started";

    public static string FormatDuration(TimeSpan span) => DurationText.Format(span);
}

/// <summary>
/// The file the status line command writes and the dashboard reads: <c>claude-usage.json</c> in the settings folder.
/// Reading and writing never throw; a file that can't be read counts as no report.
/// </summary>
public static class ClaudeUsageFile
{
    public const string FileName = "claude-usage.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>%AppData%\ImpiousBonum\claude-usage.json, the dashboard's default settings folder.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImpiousBonum", FileName);

    public static ClaudeUsageSnapshot? Read(string path)
    {
        // Checked first because the dashboard polls this every couple of seconds, usually before Claude Code is set up.
        if (!File.Exists(path))
            return null;
        try
        {
            // Share delete as well, so the writer can replace the file while it's being read.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ClaudeUsageSnapshot>(stream, Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes to a temporary file and moves it into place, so a reader never sees half a file.</summary>
    public static void Write(string path, ClaudeUsageSnapshot snapshot)
    {
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another session got there first, or the folder is unavailable. The next run writes again.
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

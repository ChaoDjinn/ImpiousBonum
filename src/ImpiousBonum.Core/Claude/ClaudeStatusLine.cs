using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiousBonum.Core.Claude;

/// <summary>
/// Claude Code's status line command (<c>ImpiousBonum.Sensors.exe claude-statusline</c>). Claude Code runs it with the
/// session as JSON on stdin and shows what it prints. It records the plan's usage limits for the dashboard, and prints
/// the model, context use and those limits.
/// </summary>
public static class ClaudeStatusLine
{
    public const string Verb = "claude-statusline";

    /// <summary>Handles one run: updates the usage file at <paramref name="usagePath"/> and returns the line to print. Never throws.</summary>
    public static string Handle(string input, string usagePath, DateTimeOffset now)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(input);
        }
        catch (JsonException)
        {
            root = null;
        }

        // Claude Code sends rate_limits only after a session's first reply, and drops a window once it resets.
        // Until a window is reported again, keep the last one known: the dashboard works out whether it has reset.
        var limits = root?["rate_limits"];
        var previous = ClaudeUsageFile.Read(usagePath);
        var snapshot = new ClaudeUsageSnapshot(
            now.ToUnixTimeSeconds(),
            Window(limits?["five_hour"]) ?? previous?.FiveHour,
            Window(limits?["seven_day"]) ?? previous?.SevenDay);
        ClaudeUsageFile.Write(usagePath, snapshot);

        return Line(root, snapshot, now);
    }

    private static ClaudeUsageWindow? Window(JsonNode? node) =>
        Number(node?["used_percentage"]) is { } used ? new ClaudeUsageWindow(used, (long)(Number(node?["resets_at"]) ?? 0)) : null;

    private static string Line(JsonNode? root, ClaudeUsageSnapshot snapshot, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (Text(root?["model"]?["display_name"]) is { Length: > 0 } model)
            parts.Add(model);
        if (Number(root?["context_window"]?["used_percentage"]) is { } context)
            parts.Add($"context {Percent(context)}");

        var session = snapshot.Reading(snapshot.FiveHour, now);
        if (session.UsedPercentage is { } sessionUsed)
            parts.Add($"session {Percent(sessionUsed)}" + (session.ResetsIn is { } span ? $" ({ClaudeUsageReading.FormatDuration(span)})" : ""));
        if (snapshot.Reading(snapshot.SevenDay, now).UsedPercentage is { } weekUsed)
            parts.Add($"week {Percent(weekUsed)}");

        return parts.Count > 0 ? string.Join(" · ", parts) : "Claude Code";
    }

    private static string Percent(double value) => value.ToString("0", CultureInfo.InvariantCulture) + "%";

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

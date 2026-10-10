using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.Core.Claude;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Points Claude Code's status line at <c>ImpiousBonum.Sensors.exe claude-statusline</c>, which records the plan's usage
/// for the dashboard (see <see cref="ClaudeStatusLine"/>). Edits Claude Code's user settings.json and keeps everything else in it.
/// </summary>
public static class ClaudeCodeSetup
{
    /// <summary>
    /// Claude Code re-runs the status line this often even while idle, which is how the dashboard tells it's still open
    /// (see <see cref="Core.Providers.ClaudeUsageProvider.StaleAfter"/>).
    /// </summary>
    public const int RefreshSeconds = 15;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep the user's own text readable rather than \u-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public enum StatusLine
    {
        None,
        Ours,
        Other,
    }

    /// <summary>Claude Code's user settings: %USERPROFILE%\.claude\settings.json, or under CLAUDE_CONFIG_DIR when that's set.</summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        "settings.json");

    /// <summary>The sensor host shipped with the app. Installed copies run from a fixed "current" folder, so the path survives updates.</summary>
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "sensors", "ImpiousBonum.Sensors.exe");

    /// <summary>Which status line settings.json has. Throws <see cref="JsonException"/> when the file isn't a JSON object.</summary>
    public static (StatusLine Kind, string? Command) Inspect(string? settingsJson)
    {
        if (Parse(settingsJson)["statusLine"] is not { } statusLine)
            return (StatusLine.None, null);
        var command = statusLine["command"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        return (IsOurs(command) ? StatusLine.Ours : StatusLine.Other, command);
    }

    /// <summary>settings.json with the status line set to <paramref name="command"/>; every other setting is kept.</summary>
    public static string Apply(string? settingsJson, string command)
    {
        var root = Parse(settingsJson);
        var statusLine = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["refreshInterval"] = RefreshSeconds,
        };
        if (root["statusLine"]?["padding"] is JsonValue padding)
            statusLine["padding"] = padding.DeepClone();
        root["statusLine"] = statusLine;
        return root.ToJsonString(WriteOptions) + Environment.NewLine;
    }

    /// <summary>
    /// The command line for <paramref name="helperPath"/>. Claude Code runs it through Git Bash when that's installed and
    /// PowerShell otherwise; forward slashes work in both, and a path without spaces needs no quoting in either.
    /// </summary>
    public static string Command(string helperPath, bool gitBash)
    {
        var path = helperPath.Replace('\\', '/');
        if (!path.Contains(' '))
            return $"{path} {ClaudeStatusLine.Verb}";
        return gitBash ? $"\"{path}\" {ClaudeStatusLine.Verb}" : $"& '{path.Replace("'", "''")}' {ClaudeStatusLine.Verb}";
    }

    /// <summary>Whether Claude Code will find Git Bash, and so run the status line through it.</summary>
    public static bool GitBashInstalled()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];
        return candidates.Any(path => path is { Length: > 0 } && File.Exists(path));
    }

    private static bool IsOurs(string? command) =>
        command is not null
        && command.Contains("ImpiousBonum.Sensors", StringComparison.OrdinalIgnoreCase)
        && command.Contains(ClaudeStatusLine.Verb, StringComparison.OrdinalIgnoreCase);

    private static JsonObject Parse(string? settingsJson) =>
        string.IsNullOrWhiteSpace(settingsJson)
            ? []
            : JsonNode.Parse(settingsJson) as JsonObject ?? throw new JsonException("Claude Code's settings.json should hold a JSON object.");
}

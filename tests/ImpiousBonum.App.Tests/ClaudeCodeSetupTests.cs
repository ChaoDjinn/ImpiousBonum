using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

public sealed class ClaudeCodeSetupTests
{
    private const string Ours = "C:/Users/me/AppData/Local/ImpiousBonum/current/sensors/ImpiousBonum.Sensors.exe claude-statusline";

    [Fact]
    public void Recognises_no_status_line_ours_and_someone_elses()
    {
        Assert.Equal(ClaudeCodeSetup.StatusLine.None, ClaudeCodeSetup.Inspect(null).Kind);
        Assert.Equal(ClaudeCodeSetup.StatusLine.None, ClaudeCodeSetup.Inspect("""{ "model": "opus" }""").Kind);
        Assert.Equal(ClaudeCodeSetup.StatusLine.Ours, ClaudeCodeSetup.Inspect($$"""{ "statusLine": { "type": "command", "command": "{{Ours}}" } }""").Kind);
        Assert.Equal(
            (ClaudeCodeSetup.StatusLine.Other, "~/.claude/statusline.sh"),
            ClaudeCodeSetup.Inspect("""{ "statusLine": { "type": "command", "command": "~/.claude/statusline.sh" } }"""));
    }

    [Fact]
    public void Setting_the_status_line_keeps_every_other_setting()
    {
        var updated = ClaudeCodeSetup.Apply("""
            {
              "model": "opus",
              "permissions": { "allow": ["Bash(npm test)"] },
              "statusLine": { "type": "command", "command": "old", "padding": 2 }
            }
            """, Ours);

        var root = JsonNode.Parse(updated)!;
        Assert.Equal("opus", root["model"]!.GetValue<string>());
        Assert.Equal("Bash(npm test)", root["permissions"]!["allow"]![0]!.GetValue<string>());
        Assert.Equal("command", root["statusLine"]!["type"]!.GetValue<string>());
        Assert.Equal(Ours, root["statusLine"]!["command"]!.GetValue<string>());
        Assert.Equal(ClaudeCodeSetup.RefreshSeconds, root["statusLine"]!["refreshInterval"]!.GetValue<int>());
        Assert.Equal(2, root["statusLine"]!["padding"]!.GetValue<int>());
        Assert.Contains("Bash(npm test)", updated);
    }

    [Fact]
    public void Creates_settings_when_there_are_none() =>
        Assert.Equal(ClaudeCodeSetup.StatusLine.Ours, ClaudeCodeSetup.Inspect(ClaudeCodeSetup.Apply(null, Ours)).Kind);

    [Fact]
    public void Settings_that_are_not_a_JSON_object_are_left_alone()
    {
        Assert.ThrowsAny<JsonException>(() => ClaudeCodeSetup.Inspect("{ not json"));
        Assert.ThrowsAny<JsonException>(() => ClaudeCodeSetup.Apply("[1, 2]", Ours));
    }

    [Theory]
    [InlineData(@"C:\Users\me\App\ImpiousBonum.Sensors.exe", true, "C:/Users/me/App/ImpiousBonum.Sensors.exe claude-statusline")]
    [InlineData(@"C:\Users\me\App\ImpiousBonum.Sensors.exe", false, "C:/Users/me/App/ImpiousBonum.Sensors.exe claude-statusline")]
    [InlineData(@"C:\Users\Jo Bloggs\App\ImpiousBonum.Sensors.exe", true, "\"C:/Users/Jo Bloggs/App/ImpiousBonum.Sensors.exe\" claude-statusline")]
    [InlineData(@"C:\Users\Jo O'Neil\App\ImpiousBonum.Sensors.exe", false, "& 'C:/Users/Jo O''Neil/App/ImpiousBonum.Sensors.exe' claude-statusline")]
    public void Commands_work_in_the_shell_Claude_Code_uses(string path, bool gitBash, string expected) =>
        Assert.Equal(expected, ClaudeCodeSetup.Command(path, gitBash));
}

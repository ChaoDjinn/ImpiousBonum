using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Does what an alert asks for beyond notifying and flashing, once, when it goes off: plays its sound, switches the
/// layout, runs its command and sends its web request. Anything that can't be done goes to <paramref name="problem"/>.
/// </summary>
/// <param name="switchLayout">Shows the named layout; returns what's wrong if it can't.</param>
/// <param name="problem">Told what went wrong, e.g. "Couldn't run backup.bat: …", on the UI thread.</param>
/// <param name="send">Sends web requests; HTTP unless a test swaps it.</param>
public sealed class AlertActions(
    Func<string, string?> switchLayout,
    Action<string> problem,
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
{
    // Kept so a sound file isn't collected while it plays.
    private SoundPlayer? _player;

    public void Run(AlertRule rule, string text, MetricStore store)
    {
        if (!string.IsNullOrWhiteSpace(rule.Sound) && Play(rule.Sound) is { } noSound)
            problem(noSound);
        if (!string.IsNullOrWhiteSpace(rule.Layout) && switchLayout(rule.Layout.Trim()) is { } noLayout)
            problem(noLayout);
        if (!string.IsNullOrWhiteSpace(rule.Command) && Start(rule, text, store) is { } noCommand)
            problem(noCommand);
        if (!string.IsNullOrWhiteSpace(rule.Webhook))
            _ = SendAsync(rule, text, store);
    }

    /// <summary>Plays one of the Windows sounds by name, or a .wav file; null when it's playing.</summary>
    public string? Play(string sound)
    {
        sound = sound.Trim();
        if (SystemSound(sound) is { } system)
        {
            system.Play();
            return null;
        }
        var path = Environment.ExpandEnvironmentVariables(sound);
        if (!File.Exists(path))
            return $"There's no sound file {path}.";
        try
        {
            _player?.Dispose();
            _player = new SoundPlayer(path);
            _player.Play();
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return $"Couldn't play {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private static SystemSound? SystemSound(string name) => name.ToLowerInvariant() switch
    {
        "exclamation" => SystemSounds.Exclamation,
        "asterisk" => SystemSounds.Asterisk,
        "hand" => SystemSounds.Hand,
        "question" => SystemSounds.Question,
        "beep" => SystemSounds.Beep,
        _ => null,
    };

    /// <summary>
    /// The command, hidden, with the alert in its environment rather than its command line (see
    /// <see cref="AlertRule.Environment"/>). It's left to run: nothing waits for it.
    /// </summary>
    internal static ProcessStartInfo StartInfo(AlertRule rule, string text, MetricStore store)
    {
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(rule.Command!.Trim()), Environment.ExpandEnvironmentVariables(rule.Arguments ?? string.Empty))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var (name, value) in rule.Environment(text, store))
            start.Environment[name] = value;
        return start;
    }

    private static string? Start(AlertRule rule, string text, MetricStore store)
    {
        try
        {
            using var process = Process.Start(StartInfo(rule, text, store));
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return $"Couldn't run {rule.Command!.Trim()}: {ex.Message}";
        }
    }

    // Called on the UI thread, and carries on there after the request, so the problem is reported on it too.
    private async Task SendAsync(AlertRule rule, string text, MetricStore store)
    {
        if (await AlertWebhook.SendAsync(rule, text, store, DateTimeOffset.UtcNow, send) is { } failed)
            problem($"Couldn't send the alert: {failed}.");
    }
}

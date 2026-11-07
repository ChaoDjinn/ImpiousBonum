using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Obs;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Carries out what a tap on a widget asks for (see <see cref="WidgetAction"/>). Keys are typed with SendInput, so
/// they reach whatever app is in front: the dashboard never takes focus. Returns a message for the user when something
/// couldn't be done, or null.
/// </summary>
/// <param name="microphoneMuted">
/// Hears whether the microphone is muted after a mic-mute button, so the dashboard can show it straight away rather than
/// at the next reading.
/// </param>
public sealed class ActionRunner(Func<string, string?> switchLayout, Action<bool>? microphoneMuted = null)
{
    private const ushort VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF;
    private const ushort MediaNext = 0xB0, MediaPrevious = 0xB1, MediaStop = 0xB2, MediaPlayPause = 0xB3;

    /// <summary>How long a web button waits for an answer.</summary>
    public static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Sends web buttons' requests; HTTP unless a test swaps it.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? WebSender { get; init; }

    /// <summary>Told why a web button's request didn't work, for the log.</summary>
    public Action<string> Failed { get; init; } = AppLog.Warning;

    /// <summary>The connection to OBS that OBS buttons send to; null when there's none, as in the demo.</summary>
    public ObsConnection? Obs { get; init; }

    public string? Run(WidgetAction action)
    {
        switch (action.Kind.ToLowerInvariant())
        {
            case WidgetAction.PlayPause: return Press(MediaPlayPause);
            case WidgetAction.Next: return Press(MediaNext);
            case WidgetAction.Previous: return Press(MediaPrevious);
            case WidgetAction.Stop: return Press(MediaStop);
            case WidgetAction.VolumeUp: return Press(VolumeUp);
            case WidgetAction.VolumeDown: return Press(VolumeDown);
            case WidgetAction.Mute: return Press(VolumeMute);
            case WidgetAction.MicMute: return ToggleMicrophone();
            case WidgetAction.Open: return Open(action.Target, action.Arguments, action.WorkingDirectory);
            case WidgetAction.Layout:
                return action.Target.Length == 0 ? "This button has no layout to switch to. Set its target in the editor." : switchLayout(action.Target);
            case WidgetAction.Keys:
                return KeyChord.Parse(action.Target, out var problem) is { } chord ? Send(chord) : $"This button's keys can't be sent. {problem}";
            case WidgetAction.Web: return SendWebRequest(action);
            case WidgetAction.ObsRecord:
            case WidgetAction.ObsStream:
            case WidgetAction.ObsReplay:
            case WidgetAction.ObsSaveReplay:
            case WidgetAction.ObsScene:
            case WidgetAction.ObsMute:
                return SendToObs(action);
            default:
                return $"Buttons can't \"{action.Kind}\". Pick an action in the editor.";
        }
    }

    /// <summary>
    /// Sends an OBS button's request in the background. Says straight away when OBS can't take it (off in settings, not
    /// running, the wrong password), as the button would otherwise seem to do nothing.
    /// </summary>
    private string? SendToObs(WidgetAction action)
    {
        if (ObsCallFor(action) is not { } call)
            return action.Kind == WidgetAction.ObsScene
                ? "This button has no OBS scene to show. Set its target in the editor to the scene's name."
                : "This button has no OBS audio source to mute. Set its target in the editor to the source's name, such as Mic/Aux.";
        if (Obs is not { } obs)
            return null;
        if (obs.State is ObsState.Off or ObsState.NotRunning or ObsState.WrongPassword or ObsState.Unreachable)
            return obs.State == ObsState.Off ? "OBS buttons need OBS turned on, on the OBS page in settings." : ObsProvider.StatusText(obs.State) + ".";
        _ = Task.Run(async () =>
        {
            if (!await obs.SendAsync(call.Type, call.Data))
                Failed($"OBS didn't do {call.Type}{(action.Target.Length > 0 ? $" for \"{action.Target}\"" : "")}: {ObsProvider.StatusText(obs.State)}");
        });
        return null;
    }

    /// <summary>What an OBS button asks OBS to do; null for one that needs a target and has none.</summary>
    internal static ObsCall? ObsCallFor(WidgetAction action) => action.Kind.ToLowerInvariant() switch
    {
        WidgetAction.ObsRecord => new ObsCall("ToggleRecord"),
        WidgetAction.ObsStream => new ObsCall("ToggleStream"),
        WidgetAction.ObsReplay => new ObsCall("ToggleReplayBuffer"),
        WidgetAction.ObsSaveReplay => new ObsCall("SaveReplayBuffer"),
        WidgetAction.ObsScene when action.Target.Length > 0 => new ObsCall("SetCurrentProgramScene", new JsonObject { ["sceneName"] = action.Target }),
        WidgetAction.ObsMute when action.Target.Length > 0 => new ObsCall("ToggleInputMute", new JsonObject { ["inputName"] = action.Target }),
        _ => null,
    };

    private static string? Open(string target, string arguments, string workingDirectory)
    {
        if (target.Length == 0)
            return "This button has nothing to open. Set its target in the editor.";
        try
        {
            // The tap was the last input, so this process may hand the right to come to the front to what it starts.
            AllowSetForegroundWindow(AsfwAny);
            using var process = Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target))
            {
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return $"Couldn't open {target}: {ex.Message}";
        }
    }

    private string? ToggleMicrophone()
    {
        if (Microphone.ToggleMute() is not { } muted)
            return "There's no microphone to mute. Plug one in, or pick one in Windows' sound settings.";
        microphoneMuted?.Invoke(muted);
        return null;
    }

    private static string? Press(ushort key) => Send(new KeyChord([], key));

    private static string? Send(KeyChord chord)
    {
        var inputs = new List<Input>();
        foreach (var modifier in chord.Modifiers)
            inputs.Add(Key(modifier, up: false));
        inputs.Add(Key(chord.VirtualKey, up: false));
        inputs.Add(Key(chord.VirtualKey, up: true));
        foreach (var modifier in chord.Modifiers.Reverse())
            inputs.Add(Key(modifier, up: true));

        var array = inputs.ToArray();
        // SendInput returns how many went in; fewer means something (such as UIPI) blocked them.
        return SendInput((uint)array.Length, array, Marshal.SizeOf<Input>()) == array.Length
            ? null
            : "Windows didn't accept the keys. An app running as administrator can't receive keys from a normal one.";
    }

    private static Input Key(ushort virtualKey, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputData
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = (ushort)MapVirtualKeyW(virtualKey, MapVkToVsc),
                Flags = (up ? KeyEventKeyUp : 0) | (IsExtended(virtualKey) ? KeyEventExtendedKey : 0),
            },
        },
    };

    /// <summary>
    /// Sends a web button's request in the background and tells the button how it went (see <see cref="WidgetAction.Done"/>).
    /// Settings that can't make a request are a problem for the user now; a request that fails goes to the log with
    /// the server's answer, never with the header, which can hold a password or token.
    /// </summary>
    private string? SendWebRequest(WidgetAction action)
    {
        if (WebRequest(action, out var problem) is not { } request)
        {
            action.Done?.Invoke(false);
            return problem;
        }
        _ = SendAsync(request, action.Done);
        return null;
    }

    // Called on the UI thread, and carries on there after the request, so the button hears back on it too.
    private async Task SendAsync(HttpRequestMessage request, Action<bool>? done)
    {
        string? failed;
        using (request)
            failed = await Core.WebRequestSender.SendAsync(request, WebTimeout, WebSender);
        if (failed is not null)
            Failed($"Button (web): {failed}");
        done?.Invoke(failed is null);
    }

    /// <summary>
    /// A web button's request: its method to its address, with its body (for POST and PUT) and its header. Null, with
    /// what's wrong in <paramref name="problem"/>, if its settings don't make one. The problem never repeats the header's value.
    /// </summary>
    internal static HttpRequestMessage? WebRequest(WidgetAction action, out string? problem)
    {
        var address = Core.WebRequestSender.Address(action.Target);
        var method = action.Method.Length == 0 ? "POST" : action.Method.Trim().ToUpperInvariant();
        MediaTypeHeaderValue? contentType = null;
        var colon = action.Header.IndexOf(':');
        problem =
            action.Target.Length == 0 ? "This button has no web address to send to. Set its target in the editor."
            : address is null ? $"This button's target isn't a web address: {action.Target}. It should start with http:// or https://."
            : !WidgetAction.WebMethods.Contains(method) ? $"Buttons can't send a web request with \"{action.Method}\". Pick GET, POST or PUT in the editor."
            : action.ContentType.Length > 0 && !MediaTypeHeaderValue.TryParse(action.ContentType, out contentType)
                ? $"This button's content type should be one like application/json, not {action.ContentType}."
            : action.Header.Length > 0 && (colon <= 0 || action.Header.Any(char.IsControl))
                ? "This button's header can't be sent. Write it as Name: value, such as Authorization: Bearer and a token."
            : null;
        if (problem is not null)
            return null;

        var request = new HttpRequestMessage(new HttpMethod(method), address);
        if (!string.IsNullOrWhiteSpace(action.Body) && method != "GET")
        {
            // JSON for a body that looks like it, as Home Assistant wants, and plain text for anything else, as ntfy wants.
            contentType ??= new MediaTypeHeaderValue(action.Body.TrimStart() is ['{', ..] or ['[', ..] ? "application/json" : "text/plain");
            contentType.CharSet ??= "utf-8";
            request.Content = new StringContent(action.Body, System.Text.Encoding.UTF8);
            request.Content.Headers.ContentType = contentType;
        }
        // Only a request header: the body's type has a setting of its own.
        var name = colon > 0 ? action.Header[..colon].Trim() : string.Empty;
        if (name.Length > 0 && !request.Headers.TryAddWithoutValidation(name, action.Header[(colon + 1)..].Trim()))
        {
            request.Dispose();
            problem = name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                ? $"This button can't send its {name} header. Set the body's type in Content type instead."
                : $"This button can't send a header called {name}.";
            return null;
        }
        return request;
    }

    /// <summary>Keys Windows expects with the extended flag: navigation, the right-hand modifiers, Windows and media keys.</summary>
    internal static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x2E   // Page Up … Delete, arrows, Print Screen, Insert
            or 0x5B or 0x5C or 0x5D  // Windows keys, menu key
            or 0x6F or 0x90          // numeric keypad divide, Num Lock
            or 0xA3 or 0xA5          // right Ctrl, right Alt
            or >= 0xA6 and <= 0xB7;  // browser, volume, media and launch keys

    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MapVkToVsc = 0;
    private const uint AsfwAny = unchecked((uint)-1);

    // INPUT is a type tag followed by a union whose largest member, MOUSEINPUT, sets its size.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

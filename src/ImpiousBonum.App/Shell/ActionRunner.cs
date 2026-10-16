using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Carries out what a tap on a widget asks for (see <see cref="WidgetAction"/>). Keys are typed with SendInput, so
/// they reach whatever app is in front: the dashboard never takes focus. Returns a message for the user when something
/// couldn't be done, or null.
/// </summary>
public sealed class ActionRunner(Func<string, string?> switchLayout)
{
    private const ushort VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF;
    private const ushort MediaNext = 0xB0, MediaPrevious = 0xB1, MediaStop = 0xB2, MediaPlayPause = 0xB3;

    /// <summary>How long a web button waits for an answer.</summary>
    public static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Sends web buttons' requests; HTTP unless a test swaps it.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? WebSender { get; init; }

    /// <summary>Told why a web button's request didn't work, for the log.</summary>
    public Action<string> Failed { get; init; } = AppLog.Warning;

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
            case WidgetAction.Open: return Open(action.Target, action.Arguments);
            case WidgetAction.Layout:
                return action.Target.Length == 0 ? "This button has no layout to switch to. Set its target in the editor." : switchLayout(action.Target);
            case WidgetAction.Keys:
                return KeyChord.Parse(action.Target, out var problem) is { } chord ? Send(chord) : $"This button's keys can't be sent. {problem}";
            case WidgetAction.Web: return SendWebRequest(action);
            default:
                return $"Buttons can't \"{action.Kind}\". Pick an action in the editor.";
        }
    }

    private static string? Open(string target, string arguments)
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
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return $"Couldn't open {target}: {ex.Message}";
        }
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

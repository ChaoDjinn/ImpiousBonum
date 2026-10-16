using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Native;

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
            case WidgetAction.Open: return Open(action.Target, action.Arguments);
            case WidgetAction.Layout:
                return action.Target.Length == 0 ? "This button has no layout to switch to. Set its target in the editor." : switchLayout(action.Target);
            case WidgetAction.Keys:
                return KeyChord.Parse(action.Target, out var problem) is { } chord ? Send(chord) : $"This button's keys can't be sent. {problem}";
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

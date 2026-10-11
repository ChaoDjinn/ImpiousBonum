using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ImpiousBonum.App.Shell;

/// <summary>A key combination that does something from anywhere in Windows, even while a game has focus.</summary>
/// <param name="Action">One of the constants below.</param>
/// <param name="Keys">The combination, as <see cref="KeyChord"/> writes it, e.g. <c>Ctrl+Alt+D</c>.</param>
/// <param name="Layout">For <see cref="SwitchLayout"/>: the layout to show.</param>
public sealed record Hotkey(string Action, string Keys, string? Layout = null)
{
    public const string ShowHide = "show-hide";
    public const string NextPage = "next-page";
    public const string PreviousPage = "previous-page";
    public const string Edit = "edit";
    public const string SwitchLayout = "layout";

    public string Describe() => Action switch
    {
        ShowHide => "Show or hide the dashboard",
        NextPage => "Next page",
        PreviousPage => "Previous page",
        Edit => "Edit the layout",
        SwitchLayout => $"Show \"{Layout}\"",
        _ => Action,
    };

    /// <summary>
    /// Why <paramref name="chord"/> can't be a hotkey, or null if it can. One that types something on its own (A, Shift+A,
    /// Enter) would take that key away from every other app, so it needs Ctrl, Alt or Win, unless it's F13 to F24,
    /// which keyboards don't type with and macro pads send.
    /// </summary>
    public static string? Problem(KeyChord chord) =>
        chord.Modifiers.Any(m => m is Native.Control or Native.Alt or Native.Windows) || chord.VirtualKey is >= 0x7C and <= 0x87
            ? null
            : "Hold Ctrl, Alt or Win with it, so it doesn't take the key away from other apps (F13 to F24 work on their own).";

    /// <summary>
    /// What a key press in the hotkeys window reads as, e.g. <c>Ctrl+Shift+F5</c>; null while only modifiers are held,
    /// since the combination isn't finished.
    /// </summary>
    public static string? Text(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.System or Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
            return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>The modifiers and key as RegisterHotKey takes them; holding it down doesn't repeat.</summary>
    internal static (uint Modifiers, uint Key) ToNative(KeyChord chord)
    {
        uint modifiers = Native.NoRepeat;
        foreach (var modifier in chord.Modifiers)
        {
            modifiers |= modifier switch
            {
                Native.Alt => 0x1u,
                Native.Control => 0x2u,
                Native.Shift => 0x4u,
                Native.Windows => 0x8u,
                _ => 0u,
            };
        }
        return (modifiers, chord.VirtualKey);
    }

    internal static class Native
    {
        public const ushort Control = 0x11, Shift = 0x10, Alt = 0x12, Windows = 0x5B;
        public const uint NoRepeat = 0x4000;
    }
}

/// <summary>
/// Registers <see cref="Hotkey"/>s with Windows and raises <see cref="Pressed"/> when one is pressed. Uses a
/// message-only window of its own, so it works whether or not the dashboard is showing.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HwndSource _window;
    private readonly Dictionary<int, Hotkey> _registered = new();
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        // HWND_MESSAGE as the parent: never shown, only gets messages.
        _window = new HwndSource(new HwndSourceParameters("Impious Bonum hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(OnMessage);
    }

    public event EventHandler<Hotkey>? Pressed;

    /// <summary>Replaces the registered hotkeys with <paramref name="hotkeys"/>. Returns a line for each one that couldn't be set.</summary>
    public IReadOnlyList<string> Apply(IEnumerable<Hotkey> hotkeys)
    {
        Clear();
        var problems = new List<string>();
        foreach (var hotkey in hotkeys)
        {
            if (KeyChord.Parse(hotkey.Keys, out var problem) is not { } chord)
            {
                problems.Add($"{hotkey.Keys} ({hotkey.Describe()}): {problem}");
                continue;
            }
            if (Hotkey.Problem(chord) is { } notAllowed)
            {
                problems.Add($"{hotkey.Keys} ({hotkey.Describe()}): {notAllowed}");
                continue;
            }
            var (modifiers, key) = Hotkey.ToNative(chord);
            var id = _nextId++;
            if (RegisterHotKey(_window.Handle, id, modifiers, key))
                _registered[id] = hotkey;
            else
            {
                problems.Add(Marshal.GetLastWin32Error() == ErrorHotkeyAlreadyRegistered
                    ? $"{hotkey.Keys} ({hotkey.Describe()}): another app already uses it."
                    : $"{hotkey.Keys} ({hotkey.Describe()}): Windows wouldn't set it.");
            }
        }
        return problems;
    }

    /// <summary>Lets go of every hotkey, e.g. while new ones are being chosen.</summary>
    public void Clear()
    {
        foreach (var id in _registered.Keys)
            UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var hotkey))
        {
            handled = true;
            Pressed?.Invoke(this, hotkey);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

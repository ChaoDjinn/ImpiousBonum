using System.Windows.Input;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// A keyboard shortcut written as text, e.g. <c>Ctrl+Shift+M</c>, <c>Win+D</c>, <c>F13</c> or <c>VolumeMute</c>,
/// as Windows virtual-key codes: the modifiers to hold, then the key.
/// </summary>
public sealed record KeyChord(IReadOnlyList<ushort> Modifiers, ushort VirtualKey)
{
    private const ushort Control = 0x11, Shift = 0x10, Alt = 0x12, Windows = 0x5B;

    private static readonly Dictionary<string, ushort> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Control, ["control"] = Control, ["shift"] = Shift, ["alt"] = Alt, ["win"] = Windows, ["windows"] = Windows,
    };

    /// <summary>Names people write that WPF's <see cref="System.Windows.Input.Key"/> spells differently.</summary>
    private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = Key.Escape,
        ["del"] = Key.Delete,
        ["ins"] = Key.Insert,
        ["pgup"] = Key.PageUp,
        ["pgdn"] = Key.PageDown,
        ["backspace"] = Key.Back,
        ["prtsc"] = Key.PrintScreen,
        ["plus"] = Key.OemPlus,
        ["minus"] = Key.OemMinus,
        ["mute"] = Key.VolumeMute,
        ["playpause"] = Key.MediaPlayPause,
        ["nexttrack"] = Key.MediaNextTrack,
        ["previoustrack"] = Key.MediaPreviousTrack,
    };

    /// <summary>The chord, or null if <paramref name="text"/> isn't one; <paramref name="problem"/> says why.</summary>
    public static KeyChord? Parse(string? text, out string? problem)
    {
        problem = null;
        var parts = (text ?? string.Empty).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            problem = "No keys given.";
            return null;
        }

        var modifiers = new List<ushort>();
        foreach (var part in parts[..^1])
        {
            if (!ModifierNames.TryGetValue(part, out var modifier))
            {
                problem = $"\"{part}\" isn't Ctrl, Shift, Alt or Win.";
                return null;
            }
            if (!modifiers.Contains(modifier))
                modifiers.Add(modifier);
        }

        var name = parts[^1];
        if (ModifierNames.TryGetValue(name, out var alone))
            return new KeyChord(modifiers, alone);
        if (ToKey(name) is not { } key || KeyInterop.VirtualKeyFromKey(key) is not (> 0 and var vk))
        {
            problem = $"\"{name}\" isn't a key name. Use names like A, 5, F13, Enter, Space, Esc, Home, Up, VolumeUp or MediaPlayPause.";
            return null;
        }
        return new KeyChord(modifiers, (ushort)vk);
    }

    private static Key? ToKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return Key.D0 + (name[0] - '0');
        if (Aliases.TryGetValue(name, out var alias))
            return alias;
        // Enum.TryParse accepts numbers too ("13" would be a key code); only names are wanted.
        return name.All(char.IsAsciiLetterOrDigit) && !name.All(char.IsAsciiDigit) && Enum.TryParse<Key>(name, ignoreCase: true, out var key) && key != Key.None
            ? key
            : null;
    }
}

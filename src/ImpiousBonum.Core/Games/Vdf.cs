using System.Text;

namespace ImpiousBonum.Core.Games;

/// <summary>
/// One <c>{ }</c> block of a Valve KeyValues file: each key has a text value or a block of its own. Keys are matched
/// ignoring case, as Steam does; a key given twice keeps its last value.
/// </summary>
public sealed class VdfBlock
{
    private readonly Dictionary<string, object> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _keys = [];

    /// <summary>The keys, in the order they're in the file.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>The key's text, or null if it's missing or a block.</summary>
    public string? Text(string key) => _entries.GetValueOrDefault(key) as string;

    /// <summary>The key's block, or null if it's missing or text.</summary>
    public VdfBlock? Block(string key) => _entries.GetValueOrDefault(key) as VdfBlock;

    internal void Add(string key, object value)
    {
        if (!_entries.ContainsKey(key))
            _keys.Add(key);
        _entries[key] = value;
    }
}

/// <summary>
/// Reads Valve's text KeyValues files, which Steam keeps its library folders (<c>libraryfolders.vdf</c>) and its games'
/// manifests (<c>appmanifest_&lt;id&gt;.acf</c>) in: quoted keys, each followed by a quoted value or a <c>{ }</c> block of
/// more. Anything it can't make sense of is skipped rather than failing the whole file.
/// </summary>
public static class Vdf
{
    /// <summary>The file as a block: its top-level keys, usually just one (<c>"libraryfolders"</c>, <c>"AppState"</c>).</summary>
    public static VdfBlock Parse(string text)
    {
        var position = 0;
        return ReadBlock(text, ref position, nested: false);
    }

    private static VdfBlock ReadBlock(string text, ref int position, bool nested)
    {
        var block = new VdfBlock();
        while (Next(text, ref position) is { } token)
        {
            var (key, quoted) = token;
            if (!quoted && key == "}")
            {
                if (nested)
                    return block;
                continue;
            }
            if (!quoted && key == "{")
            {
                // A block with no key: read past it.
                ReadBlock(text, ref position, nested: true);
                continue;
            }

            if (Next(text, ref position) is not { } next)
                break;
            var (value, valueQuoted) = next;
            if (!valueQuoted && value == "{")
                block.Add(key, ReadBlock(text, ref position, nested: true));
            else if (!valueQuoted && value == "}")
                return block;
            else
                block.Add(key, value);
        }
        return block;
    }

    /// <summary>The next token: a quoted string (unescaped), a brace, or a bare word; null at the end.</summary>
    private static (string Text, bool Quoted)? Next(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                // A comment, to the end of the line.
                while (position < text.Length && text[position] != '\n')
                    position++;
            }
            else if (c == '[')
            {
                // A condition such as [$WIN32] after a value, which Steam's own files don't use.
                while (position < text.Length && text[position] != ']')
                    position++;
                position++;
            }
            else if (c is '{' or '}')
            {
                position++;
                return (c.ToString(), false);
            }
            else if (c == '"')
            {
                return (Quoted(text, ref position), true);
            }
            else
            {
                var start = position;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
                    position++;
                return (text[start..position], false);
            }
        }
        return null;
    }

    private static string Quoted(string text, ref int position)
    {
        var result = new StringBuilder();
        position++;
        while (position < text.Length && text[position] != '"')
        {
            var c = text[position++];
            if (c == '\\' && position < text.Length)
            {
                var escaped = text[position];
                switch (escaped)
                {
                    case '\\' or '"':
                        result.Append(escaped);
                        position++;
                        continue;
                    case 'n':
                        result.Append('\n');
                        position++;
                        continue;
                    case 't':
                        result.Append('\t');
                        position++;
                        continue;
                }
            }
            result.Append(c);
        }
        position++;
        return result.ToString();
    }
}

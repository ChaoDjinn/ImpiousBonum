using System.Text.Json.Nodes;

namespace ImpiousBonum.Core.CustomData;

/// <summary>
/// Finds a value in JSON by a simple path: names separated by dots, with <c>[n]</c> for list items, e.g.
/// <c>attributes.temperature</c>, <c>data[0].value</c> or <c>$.sensors["outside temp"]</c>. A leading <c>$.</c> is allowed.
/// </summary>
public static class JsonPath
{
    public static JsonNode? Find(JsonNode root, string path)
    {
        var node = (JsonNode?)root;
        foreach (var step in Steps(path))
        {
            node = (node, step) switch
            {
                (JsonArray array, int index) => index >= 0 && index < array.Count ? array[index] : null,
                (JsonObject obj, string name) => Lookup(obj, name),
                _ => null,
            };
            if (node is null)
                return null;
        }
        return node;
    }

    /// <summary>The path as names (strings) and indexes (ints).</summary>
    internal static IEnumerable<object> Steps(string path)
    {
        var text = path.Trim();
        if (text.StartsWith("$"))
            text = text[1..];
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '.')
            {
                i++;
                continue;
            }
            if (text[i] == '[')
            {
                var end = text.IndexOf(']', i);
                if (end < 0)
                    yield break;
                var inside = text[(i + 1)..end].Trim();
                if (inside.Length >= 2 && inside[0] is '"' or '\'' && inside[^1] == inside[0])
                    yield return inside[1..^1];
                else if (int.TryParse(inside, out var index))
                    yield return index;
                else
                    yield return inside;
                i = end + 1;
                continue;
            }
            var next = text.IndexOfAny(['.', '['], i);
            var name = next < 0 ? text[i..] : text[i..next];
            yield return name;
            i = next < 0 ? text.Length : next;
        }
    }

    // JSON keys are case-sensitive, but people type paths by hand: an exact match first, then any case.
    private static JsonNode? Lookup(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var exact) ? exact : obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

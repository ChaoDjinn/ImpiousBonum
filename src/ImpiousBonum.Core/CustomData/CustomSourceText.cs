namespace ImpiousBonum.Core.CustomData;

/// <summary>
/// The plain-text forms the setup window edits: one field per line as <c>name = path : unit</c> (the path and unit
/// optional), and one header per line as <c>Name: value</c>.
/// </summary>
public static class CustomSourceText
{
    /// <summary>Fields from lines like <c>outside = state : celsius</c>; null with <paramref name="problem"/> set if a line can't be read.</summary>
    public static IReadOnlyList<CustomField>? ParseFields(string text, out string? problem)
    {
        problem = null;
        var fields = new List<CustomField>();
        foreach (var (line, number) in Lines(text))
        {
            var equals = line.IndexOf('=');
            var name = (equals < 0 ? line : line[..equals]).Trim();
            var rest = equals < 0 ? string.Empty : line[(equals + 1)..];
            string? unit = null;
            // The unit is after the last colon, if that's a unit name; paths could contain colons in quoted keys.
            var colon = rest.LastIndexOf(':');
            if (colon >= 0 && CustomField.ParseUnit(rest[(colon + 1)..]) is not null)
            {
                unit = rest[(colon + 1)..].Trim();
                rest = rest[..colon];
            }
            var path = rest.Trim();
            if (!CustomSource.IsValidName(name))
            {
                problem = $"Line {number}: start with a field name made of letters, digits, - and _, like temp = main.temp : celsius";
                return null;
            }
            fields.Add(new CustomField(name, path.Length == 0 ? null : path, string.IsNullOrWhiteSpace(unit) || unit.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : unit.ToLowerInvariant()));
        }
        return fields;
    }

    public static string FormatFields(IEnumerable<CustomField>? fields) => string.Join(Environment.NewLine, (fields ?? []).Select(Format));

    /// <summary>"temp = main.temp : celsius", "status = state", "value = : text" or just "value".</summary>
    private static string Format(CustomField field)
    {
        var hasPath = !string.IsNullOrWhiteSpace(field.Path);
        var hasUnit = !string.IsNullOrWhiteSpace(field.Unit);
        var line = field.Name;
        if (hasPath || hasUnit)
            line += " = " + (hasPath ? field.Path : string.Empty);
        if (hasUnit)
            line += (hasPath ? " : " : ": ") + field.Unit;
        return line;
    }

    /// <summary>Headers from lines like <c>Authorization: Bearer abc</c>.</summary>
    public static IReadOnlyDictionary<string, string>? ParseHeaders(string text, out string? problem)
    {
        problem = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (line, number) in Lines(text))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                problem = $"Header line {number}: write it as Name: value";
                return null;
            }
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    public static string FormatHeaders(IReadOnlyDictionary<string, string>? headers) =>
        string.Join(Environment.NewLine, (headers ?? new Dictionary<string, string>()).Select(h => $"{h.Key}: {h.Value}"));

    private static IEnumerable<(string Line, int Number)> Lines(string text) =>
        text.Split('\n')
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(l => l.Line.Length > 0 && !l.Line.StartsWith('#'));
}

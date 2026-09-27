using System.Text;

namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// A piece of display text with metric placeholders, e.g. <c>"{cpu.load} / {cpu.threads} threads"</c>.
/// Placeholders are <c>{metric.id}</c> or <c>{metric.id:spec}</c> (see <see cref="FormatSpec"/>).
/// Use <c>{{</c> and <c>}}</c> for literal braces. Unknown metrics render as <see cref="MetricFormatter.Missing"/>.
/// </summary>
public sealed class ValueTemplate
{
    private readonly IReadOnlyList<Part> _parts;

    private ValueTemplate(IReadOnlyList<Part> parts) => _parts = parts;

    public IEnumerable<string> MetricIds => _parts.Where(p => p.MetricId is not null).Select(p => p.MetricId!);

    public static ValueTemplate Parse(string? template)
    {
        var parts = new List<Part>();
        if (string.IsNullOrEmpty(template))
            return new ValueTemplate(parts);

        var literal = new StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
            }
            else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
            }
            else if (c == '{' && template.IndexOf('}', i + 1) is var close && close > i)
            {
                if (literal.Length > 0)
                {
                    parts.Add(new Part(literal.ToString(), null, null));
                    literal.Clear();
                }

                var body = template[(i + 1)..close];
                var colon = body.IndexOf(':');
                var id = (colon < 0 ? body : body[..colon]).Trim();
                var spec = FormatSpec.Parse(colon < 0 ? null : body[(colon + 1)..]);
                parts.Add(new Part(null, id, spec));
                i = close + 1;
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }

        if (literal.Length > 0)
            parts.Add(new Part(literal.ToString(), null, null));

        return new ValueTemplate(parts);
    }

    public string Render(MetricStore store)
    {
        var builder = new StringBuilder();
        foreach (var part in _parts)
        {
            if (part.MetricId is null)
                builder.Append(part.Literal);
            else if (store.TryGet(part.MetricId, out var sample))
                builder.Append(MetricFormatter.Format(sample, part.Spec));
            else
                builder.Append(MetricFormatter.Missing);
        }
        return builder.ToString();
    }

    private sealed record Part(string? Literal, string? MetricId, FormatSpec? Spec);
}

namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// Optional formatting hints for a metric inside a template, written after a colon:
/// <c>{gpu.vram.used:N0 MB}</c>. Tokens are space separated and may appear in any order:
/// a byte unit (B, KB, MB, GB, TB) forces that unit, <c>nounit</c> drops the suffix,
/// and anything else is treated as a .NET numeric format string such as <c>0.0</c> or <c>N0</c>.
/// </summary>
public sealed record FormatSpec(string? NumberFormat = null, ByteUnit? ByteUnit = null, bool NoUnit = false)
{
    public static readonly FormatSpec Default = new();

    public static FormatSpec Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return Default;

        string? numberFormat = null;
        ByteUnit? byteUnit = null;
        var noUnit = false;

        foreach (var token in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("nounit", StringComparison.OrdinalIgnoreCase))
                noUnit = true;
            else if (Enum.TryParse<ByteUnit>(token, ignoreCase: true, out var unit) && !int.TryParse(token, out _))
                byteUnit = unit;
            else
                numberFormat = token;
        }

        return new FormatSpec(numberFormat, byteUnit, noUnit);
    }
}

public enum ByteUnit
{
    B,
    KB,
    MB,
    GB,
    TB,
}

using System.Text;

namespace ImpiousBonum.Core.Metrics;

/// <summary>Turns a name, such as a network adapter's, into a part of a metric id.</summary>
public static class MetricIdPart
{
    /// <summary>
    /// Lower case, with each run of anything but letters and digits a single hyphen: "Ethernet 2" is <c>ethernet-2</c>,
    /// "Wi-Fi" <c>wi-fi</c>. Empty for a name with no letters or digits.
    /// </summary>
    public static string From(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        return builder.ToString().TrimEnd('-');
    }
}

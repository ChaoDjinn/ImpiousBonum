using System.Text.RegularExpressions;

namespace ImpiousBonum.Core.Metrics;

/// <summary>
/// Text for one drive's row in the Drives widget, turned into an ordinary template for that drive:
/// <list type="bullet">
/// <item>a short placeholder such as <c>{free}</c> or <c>{life:0}</c> stands for that drive's metric (<c>{disk.C.free}</c>);</item>
/// <item>a <c>*</c> in a metric id stands for the drive's letter (<c>{disk.*.read}</c> → <c>{disk.C.read}</c>);</item>
/// <item><c>{letter}</c> is the letter itself.</item>
/// </list>
/// Any other metric works as anywhere else, e.g. <c>{cpu.temp}</c>.
/// </summary>
public static partial class DriveTemplate
{
    /// <summary>Per-drive metric suffixes that have a short placeholder, e.g. "life" for <c>disk.C.life</c>.</summary>
    public static readonly IReadOnlyList<string> Placeholders =
        ["free", "used", "total", "usedPct", "label", "read", "write", "active", "life", "temp", "status", "model", "hours"];

    /// <summary>Stands for "this row's drive" in a metric id.</summary>
    public const string AnyDrive = "disk.*.";

    public static string Expand(string template, string letter) =>
        ShortPlaceholder().Replace(
            template.Replace("{letter}", letter, StringComparison.OrdinalIgnoreCase).Replace(AnyDrive, $"disk.{letter}.", StringComparison.OrdinalIgnoreCase),
            match => $"{{disk.{letter}.{Canonical(match.Groups[1].Value)}{match.Groups[2].Value}}}");

    /// <summary>
    /// The placeholder to insert for a metric chosen from the picker: <c>{disk.C.life:0}</c> becomes <c>{life:0}</c>
    /// and any other per-drive metric <c>{disk.*.…}</c>, so the row shows its own drive. Anything else is unchanged.
    /// </summary>
    public static string ForRow(string placeholder)
    {
        var match = DriveMetric().Match(placeholder);
        if (!match.Success)
            return placeholder;
        var suffix = match.Groups[1].Value;
        var spec = match.Groups[2].Value;
        return Placeholders.Contains(suffix, StringComparer.OrdinalIgnoreCase) ? $"{{{Canonical(suffix)}{spec}}}" : $"{{{AnyDrive}{suffix}{spec}}}";
    }

    private static string Canonical(string name) => Placeholders.First(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\{(free|used|total|usedPct|label|read|write|active|life|temp|status|model|hours)(:[^{}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex ShortPlaceholder();

    [GeneratedRegex(@"^\{disk\.[A-Za-z]\.([^{}:]+)(:[^{}]*)?\}$")]
    private static partial Regex DriveMetric();
}

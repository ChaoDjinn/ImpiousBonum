using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// Checks a layout against the widget descriptors and explains problems in plain words,
/// e.g. "Widget 3 (graph): unknown setting 'fontsize' (did you mean 'fontSize'?)".
/// Problems never stop a layout loading; widgets fall back to defaults. This just makes hand-editing forgiving.
/// </summary>
public static class LayoutValidator
{
    private static readonly string[] NamedColors = ["foreground", "secondary", "accent"];

    public static IReadOnlyList<string> Validate(LayoutDocument layout)
    {
        var issues = new List<string>();

        if (layout.Width <= 0 || layout.Height <= 0)
            issues.Add($"Canvas size {layout.Width}×{layout.Height} must be positive.");

        CheckTheme(layout.Theme, issues);

        for (var i = 0; i < layout.Widgets.Count; i++)
        {
            var widget = layout.Widgets[i];
            var type = widget.GetString("type");
            var where = $"Widget {i + 1} ({type ?? "no type"})";

            var descriptor = WidgetFactory.Find(type);
            if (descriptor is null)
            {
                var suggestion = Closest(type ?? string.Empty, WidgetFactory.Descriptors.Select(d => d.Type));
                issues.Add($"{where}: unknown type{Suggest(suggestion)}. Types: {string.Join(", ", WidgetFactory.Descriptors.Select(d => d.Type))}.");
                continue;
            }

            foreach (var key in new[] { "x", "y", "width", "height" })
            {
                if (widget[key] is null)
                    issues.Add($"{where}: missing '{key}'.");
                else if (!IsNumber(widget[key]))
                    issues.Add($"{where}: '{key}' should be a number.");
            }
            if (widget.GetDouble("width") is <= 0 || widget.GetDouble("height") is <= 0)
                issues.Add($"{where}: width and height must be positive.");

            CheckSettings(widget, descriptor.Settings, where, issues, skip: WidgetDescriptor.GeometryKeys);
        }

        return issues;
    }

    private static void CheckSettings(JsonObject obj, IReadOnlyList<SettingDescriptor> settings, string where, List<string> issues, IReadOnlyList<string> skip)
    {
        foreach (var (key, value) in obj)
        {
            if (skip.Contains(key))
                continue;

            var setting = settings.FirstOrDefault(s => s.Key == key);
            if (setting is null)
            {
                issues.Add($"{where}: unknown setting '{key}'{Suggest(Closest(key, settings.Select(s => s.Key)))}.");
                continue;
            }

            if (value is null)
                continue;

            var problem = setting.Kind switch
            {
                SettingKind.Number when !IsNumber(value) => "should be a number",
                SettingKind.Number when JsonDefaults.TryGetNumber(value, out var n) && (n < setting.Min || n > setting.Max) =>
                    $"should be between {setting.Min} and {setting.Max}",
                SettingKind.Toggle when value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False) => "should be true or false",
                SettingKind.Items when value is not JsonArray => "should be a list",
                SettingKind.Text or SettingKind.Template or SettingKind.Metric or SettingKind.Color or SettingKind.Choice or SettingKind.Icon
                    when value.GetValueKind() != JsonValueKind.String => "should be text",
                SettingKind.Choice or SettingKind.Icon when !setting.Choices!.Contains(value.GetValue<string>()) =>
                    $"should be one of {string.Join(", ", setting.Choices!)}",
                SettingKind.Color when !IsColor(value.GetValue<string>()) =>
                    "should be foreground, secondary, accent or a colour like #FF9800",
                _ => null,
            };
            if (problem is not null)
            {
                issues.Add($"{where}: '{key}' {problem}.");
                continue;
            }

            if (setting.Kind == SettingKind.Items && value is JsonArray items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is JsonObject item)
                        CheckSettings(item, setting.ItemSettings ?? [], $"{where} {key}[{i + 1}]", issues, skip: []);
                    else
                        issues.Add($"{where}: {key}[{i + 1}] should be an object.");
                }
            }
        }
    }

    private static void CheckTheme(ThemeSettings theme, List<string> issues)
    {
        foreach (var (name, value) in new[]
        {
            ("foreground", theme.Foreground), ("secondary", theme.Secondary), ("accent", theme.Accent),
            ("background", theme.Background), ("track", theme.Track),
        })
        {
            if (!IsColor(value, allowNamed: false))
                issues.Add($"Theme: '{name}' should be a colour like #FF9800.");
        }

        if (!string.IsNullOrWhiteSpace(theme.FontFile) && !File.Exists(theme.FontFile))
            issues.Add($"Theme: font file not found: {theme.FontFile}");
    }

    private static bool IsNumber(JsonNode? node) => JsonDefaults.TryGetNumber(node, out _);

    private static bool IsColor(string value, bool allowNamed = true)
    {
        if (allowNamed && NamedColors.Contains(value, StringComparer.OrdinalIgnoreCase))
            return true;
        try
        {
            ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Suggest(string? suggestion) => suggestion is null ? string.Empty : $" (did you mean '{suggestion}'?)";

    /// <summary>The candidate that differs by case only, or else by at most two edits.</summary>
    private static string? Closest(string value, IEnumerable<string> candidates)
    {
        var list = candidates.ToList();
        return list.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? list.Select(c => (Candidate: c, Distance: EditDistance(value.ToLowerInvariant(), c.ToLowerInvariant())))
                .Where(x => x.Distance <= 2)
                .OrderBy(x => x.Distance)
                .Select(x => x.Candidate)
                .FirstOrDefault();
    }

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }
}

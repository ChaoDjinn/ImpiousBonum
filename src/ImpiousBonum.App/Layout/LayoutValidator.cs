using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// Checks a layout against the widget descriptors and explains problems in plain words,
/// e.g. "Widget 3 (graph): unknown setting 'fontsize' (did you mean 'fontSize'?)".
/// Problems never stop a layout loading; widgets fall back to defaults. This just makes hand-editing forgiving.
/// </summary>
public static class LayoutValidator
{
    /// <param name="themes">The themes the layout's theme can name; null for only the built-in ones.</param>
    public static IReadOnlyList<string> Validate(LayoutDocument layout, ThemeLibrary? themes = null)
    {
        var issues = new List<string>();

        if (layout.Width <= 0 || layout.Height <= 0)
            issues.Add($"Canvas size {layout.Width}×{layout.Height} must be positive.");

        themes ??= new ThemeLibrary(null);
        if (themes.Problem(layout.Theme) is { } themeProblem)
            issues.Add($"Theme: {themeProblem}");
        CheckThemeKeys(layout.Theme, issues);
        CheckTheme(themes.Resolve(layout.Theme), issues);

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
                SettingKind.Text or SettingKind.Template or SettingKind.DriveTemplate or SettingKind.Metric or SettingKind.Color or SettingKind.Choice or SettingKind.Icon or SettingKind.Font or SettingKind.FontFile or SettingKind.ImageFile
                    when value.GetValueKind() != JsonValueKind.String => "should be text",
                SettingKind.Choice or SettingKind.Icon when !setting.Choices!.Contains(value.GetValue<string>()) =>
                    $"should be one of {string.Join(", ", setting.Choices!)}",
                SettingKind.Color when !IsColor(value.GetValue<string>()) =>
                    "should be foreground, secondary, accent, warning, critical or a colour like #FF9800",
                SettingKind.Metric when !IsMetricId(value.GetValue<string>()) =>
                    "should be a metric id like cpu.temp, without braces or spaces",
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
                    {
                        CheckSettings(item, setting.ItemSettings ?? [], $"{where} {key}[{i + 1}]", issues, skip: []);
                        if (key == Setting.ThresholdsKey && item["above"] is null && item["below"] is null)
                            issues.Add($"{where} {key}[{i + 1}]: needs 'above' or 'below', or it never applies.");
                    }
                    else
                        issues.Add($"{where}: {key}[{i + 1}] should be an object.");
                }
            }
        }
    }

    /// <summary>Unknown keys in the layout's own theme values, and values of the wrong type (which make the whole theme fall back).</summary>
    private static void CheckThemeKeys(JsonObject theme, List<string> issues)
    {
        var keys = EditorDescriptors.Theme.Select(s => s.Key).Append(ThemeLibrary.BaseKey).ToList();
        foreach (var (key, _) in theme)
        {
            if (!keys.Contains(key))
                issues.Add($"Theme: unknown setting '{key}'{Suggest(Closest(key, keys))}.");
        }

        try
        {
            theme.Deserialize<ThemeSettings>(JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            issues.Add($"Theme: {ex.Message}");
        }
    }

    private static void CheckTheme(ThemeSettings theme, List<string> issues)
    {
        foreach (var (name, value) in new[]
        {
            ("foreground", theme.Foreground), ("secondary", theme.Secondary), ("accent", theme.Accent),
            ("background", theme.Background), ("track", theme.Track), ("warning", theme.Warning), ("critical", theme.Critical),
        })
        {
            if (!IsColor(value, allowNamed: false))
                issues.Add($"Theme: '{name}' should be a colour like #FF9800.");
        }

        if (!string.IsNullOrWhiteSpace(theme.FontFile) && !File.Exists(theme.FontFile))
            issues.Add($"Theme: font file not found: {theme.FontFile}");

        if (!string.IsNullOrWhiteSpace(theme.BackgroundImage))
        {
            if (!File.Exists(theme.BackgroundImage))
                issues.Add($"Theme: background image not found: {theme.BackgroundImage}");
            else if (!BackgroundImage.IsSupported(theme.BackgroundImage))
                issues.Add($"Theme: background image should be one of {string.Join(", ", BackgroundImage.Extensions)}: {theme.BackgroundImage}");
        }

        if (!BackgroundImage.Fits.Contains(theme.BackgroundFit))
            issues.Add($"Theme: 'backgroundFit' should be one of {string.Join(", ", BackgroundImage.Fits)}.");
        if (theme.BackgroundOpacity is < 0 or > 1 || double.IsNaN(theme.BackgroundOpacity))
            issues.Add("Theme: 'backgroundOpacity' should be between 0 and 1.");
    }

    /// <summary>
    /// Metric ids come from the running sensors, so they can't all be known here; this catches the likely slips,
    /// such as writing <c>{cpu.temp}</c> or a template where a single id belongs. Empty means "the widget's default".
    /// </summary>
    private static bool IsMetricId(string value) =>
        !value.Any(c => char.IsWhiteSpace(c) || c is '{' or '}' or ':');

    private static bool IsNumber(JsonNode? node) => JsonDefaults.TryGetNumber(node, out _);

    private static bool IsColor(string value, bool allowNamed = true)
    {
        if (allowNamed && Setting.NamedColors.Contains(value, StringComparer.OrdinalIgnoreCase))
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

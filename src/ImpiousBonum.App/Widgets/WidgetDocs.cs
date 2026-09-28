using System.Globalization;
using System.Text;
using ImpiousBonum.App.Editor;

namespace ImpiousBonum.App.Widgets;

/// <summary>Generates docs/widgets.md from the widget descriptors, so the reference can't fall out of date.</summary>
public static class WidgetDocs
{
    public static string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine("# Widget reference");
        md.AppendLine();
        md.AppendLine("<!-- Generated from the widget descriptors: ImpiousBonum.exe --widget-docs docs/widgets.md. Don't edit by hand. -->");
        md.AppendLine();
        md.AppendLine("Every widget in a layout file has `type`, `x`, `y`, `width` and `height` (in canvas units), plus the settings below.");
        md.AppendLine("Anything left out uses its default.");
        md.AppendLine();
        md.AppendLine("Templates are text with live values: `{metric.id}` or `{metric.id:spec}`, where spec can be a number format (`0.0`, `N0`),");
        md.AppendLine("a byte unit (`MB`, `GB`, …) or `nounit`. Colours are `foreground`, `secondary`, `accent`, `warning`, `critical`");
        md.AppendLine("or `#RRGGBB` / `#AARRGGBB`; the named ones come from the layout's theme.");
        md.AppendLine();
        md.AppendLine("Widgets with a `thresholds` list change colour when a value crosses a limit, e.g.");
        md.AppendLine("`\"thresholds\": [{ \"above\": 80, \"color\": \"warning\" }, { \"above\": 90, \"color\": \"critical\" }]`.");
        md.AppendLine("Each rule checks its `metric` (empty means the widget's own) against `above` and/or `below`. When several rules match,");
        md.AppendLine("the last one in the list wins, so list them mildest first. A metric with no reading keeps the normal colour.");

        foreach (var widget in WidgetFactory.Descriptors)
        {
            md.AppendLine();
            md.AppendLine($"## {widget.Name} (`{widget.Type}`)");
            md.AppendLine();
            md.AppendLine(widget.Description);
            md.AppendLine();
            AppendTable(md, widget.Settings);

            foreach (var items in widget.Settings.Where(s => s.Kind == SettingKind.Items))
            {
                md.AppendLine();
                md.AppendLine($"Each entry in `{items.Key}`:");
                md.AppendLine();
                AppendTable(md, items.ItemSettings ?? []);
            }
        }

        md.AppendLine();
        md.AppendLine("## Theme");
        md.AppendLine();
        md.AppendLine("The layout's `theme` object sets the font and colours every widget uses, and the canvas background.");
        md.AppendLine("A `backgroundImage` is drawn over the `background` colour and behind the widgets, and scales with the canvas.");
        md.AppendLine();
        AppendTable(md, EditorDescriptors.Theme);

        return md.ToString();
    }

    private static void AppendTable(StringBuilder md, IReadOnlyList<SettingDescriptor> settings)
    {
        md.AppendLine("| Setting | Type | Default | Description |");
        md.AppendLine("|---|---|---|---|");
        foreach (var s in settings)
        {
            var description = s.Help is null ? s.Label : $"{s.Label}. {s.Help}";
            md.AppendLine($"| `{s.Key}` | {Kind(s)} | {Default(s)} | {Escape(description)} |");
        }
    }

    private static string Kind(SettingDescriptor s) => s.Kind switch
    {
        SettingKind.Number when s.Min is > double.MinValue && s.Max is < double.MaxValue => $"number ({Number(s.Min.Value)}–{Number(s.Max.Value)})",
        SettingKind.Number => "number",
        SettingKind.Toggle => "true/false",
        SettingKind.Choice or SettingKind.Icon => string.Join(" / ", s.Choices!.Select(c => $"`{c}`")),
        SettingKind.Items => "list",
        _ => s.Kind.ToString().ToLowerInvariant(),
    };

    private static string Default(SettingDescriptor s) => s.Default switch
    {
        null => "—",
        double d => $"`{Number(d)}`",
        bool b => b ? "`true`" : "`false`",
        string text => $"`{Escape(text)}`",
        var other => $"`{other}`",
    };

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string text) => text.Replace("|", "\\|");
}

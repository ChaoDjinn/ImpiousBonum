using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Everything the app knows about a widget type: what it's called, how big it starts, and which settings it has.
/// The layout editor builds its property panel from this, the validator checks layout.json against it,
/// and docs/widgets.md is generated from it.
/// </summary>
public sealed record WidgetDescriptor(
    string Type,
    string Name,
    string Description,
    double DefaultWidth,
    double DefaultHeight,
    IReadOnlyList<SettingDescriptor> Settings,
    Func<WidgetSettings, Theme, Widget> Create)
{
    /// <summary>Keys every widget has, handled by the canvas rather than the widget.</summary>
    public static readonly IReadOnlyList<string> GeometryKeys = ["type", "x", "y", "width", "height"];

    public SettingDescriptor? Find(string key) => Settings.FirstOrDefault(s => s.Key == key);
}

public enum SettingKind
{
    /// <summary>Plain text, e.g. a label or a date format.</summary>
    Text,

    /// <summary>Text with <c>{metric.id}</c> placeholders.</summary>
    Template,

    /// <summary>A single metric id, e.g. <c>cpu.load</c>.</summary>
    Metric,

    Number,

    Toggle,

    /// <summary>One of <see cref="SettingDescriptor.Choices"/>.</summary>
    Choice,

    /// <summary><c>foreground</c>, <c>secondary</c>, <c>accent</c> or <c>#RRGGBB</c>/<c>#AARRGGBB</c>.</summary>
    Color,

    /// <summary>One of the built-in icon names (<see cref="SettingDescriptor.Choices"/>).</summary>
    Icon,

    /// <summary>A list of objects, each with <see cref="SettingDescriptor.ItemSettings"/>.</summary>
    Items,

    /// <summary>An installed font family name.</summary>
    Font,

    /// <summary>A path to a .ttf/.otf file.</summary>
    FontFile,
}

/// <summary>One setting of a widget type. Create these with the <see cref="Setting"/> helpers.</summary>
public sealed record SettingDescriptor(
    string Key,
    string Label,
    SettingKind Kind,
    object? Default,
    string Group,
    string? Help = null,
    IReadOnlyList<string>? Choices = null,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<SettingDescriptor>? ItemSettings = null);

/// <summary>Terse constructors for <see cref="SettingDescriptor"/>, so widget declarations read like a table.</summary>
public static class Setting
{
    public const string Content = "Content";
    public const string Text = "Text";
    public const string Appearance = "Appearance";

    public static SettingDescriptor PlainText(string key, string label, string? @default, string group = Content, string? help = null) =>
        new(key, label, SettingKind.Text, @default, group, help);

    public static SettingDescriptor Template(string key, string label, string? @default, string group = Content, string? help = null) =>
        new(key, label, SettingKind.Template, @default, group, help);

    public static SettingDescriptor Metric(string key, string label, string? @default, string group = Content, string? help = null) =>
        new(key, label, SettingKind.Metric, @default, group, help);

    /// <summary>A number setting. A null default means "not set" and is described by <paramref name="help"/>.</summary>
    public static SettingDescriptor Number(string key, string label, double? @default, double min, double max, string group = Text, string? help = null) =>
        new(key, label, SettingKind.Number, @default, group, help, Min: min, Max: max);

    public static SettingDescriptor Toggle(string key, string label, bool @default, string group = Text, string? help = null) =>
        new(key, label, SettingKind.Toggle, @default, group, help);

    public static SettingDescriptor Choice(string key, string label, string @default, IReadOnlyList<string> choices, string group = Text, string? help = null) =>
        new(key, label, SettingKind.Choice, @default, group, help, choices);

    /// <summary>A colour. With <paramref name="named"/>, the theme's foreground/secondary/accent are offered as choices too.</summary>
    public static SettingDescriptor Color(string key, string label, string @default, string group = Appearance, string? help = null, bool named = true) =>
        new(key, label, SettingKind.Color, @default, group, help, Choices: named ? NamedColors : null);

    public static readonly IReadOnlyList<string> NamedColors = ["foreground", "secondary", "accent"];

    public static SettingDescriptor Icon(string key, string label, string @default, string group = Content) =>
        new(key, label, SettingKind.Icon, @default, group, Choices: Icons.Names);

    public static SettingDescriptor Items(string key, string label, IReadOnlyList<SettingDescriptor> itemSettings, string group = Content, string? help = null) =>
        new(key, label, SettingKind.Items, null, group, help, ItemSettings: itemSettings);

    public static readonly IReadOnlyList<string> HorizontalAlignments = ["left", "center", "right"];
}
